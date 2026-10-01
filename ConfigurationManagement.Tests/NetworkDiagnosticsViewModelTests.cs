using Configuration_Management.Localization;
using Configuration_Management.Models;
using Configuration_Management.Services;
using Configuration_Management.ViewModels;
using Xunit;

namespace ConfigurationManagement.Tests;

/// <summary>
/// Тесты ViewModel окна «Диагностика подключения» (этап 0.3.9.231, функция 12):
/// дефолты, прогон с fake-сервисом (таблица портов, подсказки, статусы), DNS-ошибка,
/// ошибка сервиса, команда «Проверить порты 1С», повтор после правки адреса и защита
/// от повторного запуска во время выполнения.
/// </summary>
public sealed class NetworkDiagnosticsViewModelTests
{
    private static NetworkDiagnosticsResult SuccessResult()
    {
        var ports = new List<NetworkPortProbe>
        {
            new(1540, "Diagnostics.PortAgent", DiagnosticPortState.Available, 4, null),
            new(1541, "Diagnostics.PortCluster", DiagnosticPortState.Available, 6, null)
        };
        return new NetworkDiagnosticsResult
        {
            Host = "srv",
            HostValid = true,
            DnsOk = true,
            ResolvedAddresses = new[] { "10.0.0.5" },
            PingPerformed = true,
            PingOk = true,
            PingRttMs = 12,
            Ports = ports
        };
    }

    private static NetworkDiagnosticsResult DnsErrorResult() => new()
    {
        Host = "unknown-host",
        HostValid = true,
        DnsOk = false,
        DnsErrorCode = "dns_failed",
        ResolvedAddresses = Array.Empty<string>(),
        Ports = new[]
        {
            new NetworkPortProbe(1540, "Diagnostics.PortAgent",
                DiagnosticPortState.NotChecked, null, "dns_error")
        }
    };

    private static NetworkDiagnosticsTarget Target(string host = "srv", params int[] ports)
        => new(host, ports.Length > 0 ? ports : new[] { 1541 });

    private sealed class FakeDiagnosticsService : INetworkDiagnosticsService
    {
        public NetworkDiagnosticsResult Result { get; set; } = SuccessResult();
        public Exception? Throw { get; set; }
        public TaskCompletionSource? Gate { get; set; }
        public string? LastAddress { get; private set; }
        public IReadOnlyList<int>? LastPorts { get; private set; }
        public int CallCount { get; private set; }

        public async Task<NetworkDiagnosticsResult> RunAsync(
            string address, IReadOnlyList<int> ports,
            int timeoutMs = 3000, CancellationToken cancellationToken = default)
        {
            CallCount++;
            LastAddress = address;
            LastPorts = ports;
            if (Gate is not null)
                await Gate.Task.WaitAsync(cancellationToken);
            if (Throw is not null)
                throw Throw;
            return Result;
        }
    }

    [Fact]
    public void Defaults_HostFromTarget_CommandsExist_PortsAndHintsEmpty()
    {
        var vm = new NetworkDiagnosticsViewModel(new FakeDiagnosticsService(), Target("srv", 1541));

        Assert.Equal("srv", vm.Host);
        Assert.False(vm.IsRunning);
        Assert.Empty(vm.Ports);
        Assert.Empty(vm.Hints);
        Assert.NotNull(vm.RunCommand);
        Assert.NotNull(vm.CheckPortsCommand);
        Assert.NotNull(vm.RetryCommand);
    }

    [Fact]
    public async Task RunAsync_Success_PopulatesPortsAndHints_NotRunning()
    {
        var service = new FakeDiagnosticsService { Result = SuccessResult() };
        var vm = new NetworkDiagnosticsViewModel(service, Target("srv", 1540, 1541));

        await vm.RunAsync();

        Assert.Equal("srv", service.LastAddress);
        Assert.Equal(new[] { 1540, 1541 }, service.LastPorts);
        Assert.Equal(2, vm.Ports.Count);
        Assert.All(vm.Ports, p => Assert.True(p.IsAvailable));
        Assert.Equal("10.0.0.5", vm.IpAddressesText);
        Assert.NotEmpty(vm.PingText);
        // В тестовой среде LocalizationManager не инициализирован — T возвращает ключ;
        // сравнение ведём через сам T, как в существующих тестах (образец RacJobRowTests).
        Assert.Contains(LocalizationManager.T("Diagnostics.HintAllOk"), vm.Hints);
        Assert.False(vm.IsRunning);
        Assert.False(string.IsNullOrWhiteSpace(vm.StatusText));
    }

    [Fact]
    public async Task RunAsync_DnsFailure_PortsEmpty_SingleDnsHint_UnavailableStatus()
    {
        var service = new FakeDiagnosticsService { Result = DnsErrorResult() };
        var vm = new NetworkDiagnosticsViewModel(service, Target("unknown-host", 1540));

        await vm.RunAsync();

        Assert.True(vm.Ports.Count == 1);          // строка порта остаётся (NotChecked)
        Assert.Single(vm.Hints);                   // одна DNS-подсказка
        // Подсказка формируется тем же путём, что и в VM (string.Format(T(key), args)):
        // сравнение устойчиво к неинициализированной локализации тестов.
        Assert.Equal(
            string.Format(LocalizationManager.T("Diagnostics.HintDnsFailed"), "unknown-host"),
            vm.Hints[0]);
        Assert.Equal(LocalizationManager.T("Diagnostics.StatusUnavailable"), vm.StatusText);
        Assert.False(vm.IsRunning);
    }

    [Fact]
    public async Task RunAsync_ServiceThrows_SetsErrorStatus_PortsUnchanged()
    {
        var service = new FakeDiagnosticsService
        {
            Result = SuccessResult(),
            Throw = new InvalidOperationException("boom")
        };
        var vm = new NetworkDiagnosticsViewModel(service, Target("srv", 1541));

        await vm.RunAsync();

        Assert.Empty(vm.Ports);
        Assert.Empty(vm.Hints);
        Assert.Equal(
            string.Format(LocalizationManager.T("Diagnostics.ErrorFormat"), "boom"),
            vm.StatusText);
        Assert.False(vm.IsRunning);
    }

    [Fact]
    public async Task CheckPortsAsync_UsesOneCPorts1540_1541_1545()
    {
        var service = new FakeDiagnosticsService();
        var vm = new NetworkDiagnosticsViewModel(service, Target("srv", 1541));

        await vm.CheckPortsAsync();

        Assert.Equal(new[] { 1540, 1541, 1545 }, service.LastPorts);
    }

    [Fact]
    public async Task RetryAsync_AfterHostEdit_UsesNewHost()
    {
        var service = new FakeDiagnosticsService();
        var vm = new NetworkDiagnosticsViewModel(service, Target("old", 1541));
        await vm.RunAsync();
        Assert.Equal("old", service.LastAddress);

        vm.Host = "new-host";
        await vm.RetryAsync();

        Assert.Equal("new-host", service.LastAddress);
        Assert.Equal(2, service.CallCount);
    }

    [Fact]
    public async Task ConcurrentRun_SecondCallIsIgnored()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = new FakeDiagnosticsService { Gate = gate };
        var vm = new NetworkDiagnosticsViewModel(service, Target("srv", 1541));

        var first = vm.RunAsync();
        // Ждём, пока сервис зашёл в делегат (заблокирован на Gate).
        while (service.CallCount == 0)
            await Task.Delay(10);

        var second = vm.RetryAsync();   // во время прогона — должен быть проигнорирован
        await Task.Delay(50);
        Assert.Equal(1, service.CallCount);

        gate.SetResult();
        await first;
        await second;

        Assert.Equal(1, service.CallCount);
        Assert.False(vm.IsRunning);
    }
}