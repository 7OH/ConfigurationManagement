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

    /// <summary>Fake-хранилище портов (issue #335) для тестов VM.</summary>
    private sealed class FakeServerPortsStore : IServerPortsStore
    {
        public Dictionary<string, ServerPortsSettings> Data { get; } =
            new(StringComparer.OrdinalIgnoreCase);

        public IReadOnlyDictionary<string, ServerPortsSettings> Load() => Data;

        public void Save(string server, ServerPortsSettings ports) => Data[server.Trim()] = ports;
    }

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
        // Run проверяет активный порт поля (issue #335): стартовый порт = первый порт цели.
        var service = new FakeDiagnosticsService { Result = SuccessResult() };
        var vm = new NetworkDiagnosticsViewModel(service, Target("srv", 1540));

        await vm.RunAsync();

        Assert.Equal("srv", service.LastAddress);
        Assert.Equal(new[] { 1540 }, service.LastPorts);
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
    public async Task CheckPortsAsync_UsesOneCPortsIncludingRepository1542()
    {
        // 1542 — сервер хранилища конфигурации (issue #335): включён в стандартный набор.
        var service = new FakeDiagnosticsService();
        var vm = new NetworkDiagnosticsViewModel(service, Target("srv", 1541));

        await vm.CheckPortsAsync();

        Assert.Equal(new[] { 1540, 1541, 1542, 1545 }, service.LastPorts);
    }

    // ===================== Пустой список серверов (issue #335) =====================

    [Fact]
    public void EmptyExternalServers_TargetHostPresentAndSelected()
    {
        // Клиент-серверных баз/сохранённых портов нет — адрес цели обязан попасть
        // в «Серверы» и быть выбранным, поле для ручного ввода остаётся рабочим.
        var vm = new NetworkDiagnosticsViewModel(
            new FakeDiagnosticsService(), Target("srv", 1541),
            availableServers: Array.Empty<string>());

        Assert.Equal(new[] { "srv" }, vm.AvailableServers);
        Assert.Equal("srv", vm.SelectedServer);
        Assert.Equal("srv", vm.Host);
    }

    [Fact]
    public void NullAvailableServers_TargetHostPresentAndSelected()
    {
        var vm = new NetworkDiagnosticsViewModel(
            new FakeDiagnosticsService(), Target("srv", 1541));

        Assert.Equal(new[] { "srv" }, vm.AvailableServers);
        Assert.Equal("srv", vm.SelectedServer);
        Assert.Equal("srv", vm.Host);
    }

    [Fact]
    public void ExternalServersPlusTargetHost_DeduplicatedAndSorted()
    {
        // Внешний список + адрес цели: дубликаты снимаются (регистронезависимо),
        // итоговый список отсортирован по алфавиту.
        var vm = new NetworkDiagnosticsViewModel(
            new FakeDiagnosticsService(), Target("beta", 1541),
            availableServers: new[] { "  ALPHA ", "beta", "gamma" });

        // Регистр исходных значений сохраняется; порядок — по алфавиту
        // (без учёта регистра); дубликаты сняты регистронезависимо.
        Assert.Equal(new[] { "ALPHA", "beta", "gamma" }, vm.AvailableServers);
        Assert.Equal("beta", vm.SelectedServer);
    }

    // ===================== Сервер с портом (issue #335) =====================

    [Fact]
    public void StartWithTarget_FillsPortFieldFromTarget()
    {
        var vm = new NetworkDiagnosticsViewModel(
            new FakeDiagnosticsService(), Target("srv", 1541));

        Assert.Equal("srv", vm.Host);
        Assert.Equal("1541", vm.PortText);
        Assert.Equal(1541, vm.Port);
    }

    [Fact]
    public void StartWithTarget_PortFieldPrefersTargetPortOverSaved()
    {
        // Порт цели (базы) важнее сохранённого: база могла быть настроена на
        // конкретный порт, и он обязан попасть в поле при открытии (issue #335).
        var store = new FakeServerPortsStore { Data = { ["srv"] = new ServerPortsSettings(1599, 0, 0) } };
        var vm = new NetworkDiagnosticsViewModel(
            new FakeDiagnosticsService(), Target("srv", 1541), portsStore: store);

        Assert.Equal("1541", vm.PortText);
    }

    [Fact]
    public void ChangeSelectedServer_AppliesHostAndSavedPort()
    {
        var store = new FakeServerPortsStore { Data = { ["b"] = new ServerPortsSettings(1580, 1540, 1542) } };
        var vm = new NetworkDiagnosticsViewModel(
            new FakeDiagnosticsService(), Target("srv", 1541),
            portsStore: store,
            availableServers: new[] { "a", "b" });

        vm.SelectedServer = "b";

        Assert.Equal("b", vm.Host);
        Assert.Equal("1580", vm.PortText);
    }

    [Fact]
    public void ChangeSelectedServer_WithoutSavedPort_UsesClusterDefault()
    {
        var store = new FakeServerPortsStore();
        var vm = new NetworkDiagnosticsViewModel(
            new FakeDiagnosticsService(), Target("srv", 1541),
            portsStore: store,
            availableServers: new[] { "b" });

        vm.SelectedServer = "b";

        Assert.Equal("b", vm.Host);
        Assert.Equal(OneCPorts.Cluster, vm.Port);
    }

    [Fact]
    public async Task RunAsync_SavesClusterPortForServer()
    {
        var store = new FakeServerPortsStore();
        var vm = new NetworkDiagnosticsViewModel(
            new FakeDiagnosticsService(), Target("srv", 1541), portsStore: store);

        vm.PortText = "1600";
        await vm.RunAsync();

        Assert.True(store.Data.TryGetValue("srv", out var saved));
        Assert.Equal(1600, saved!.Cluster);
    }

    [Fact]
    public async Task CheckRepositoryAsync_UsesDefaultRepositoryPort1542()
    {
        var service = new FakeDiagnosticsService();
        var vm = new NetworkDiagnosticsViewModel(service, Target("srv", 1541));

        await vm.CheckRepositoryAsync();

        Assert.Equal(new[] { OneCPorts.Repository }, service.LastPorts);
    }

    [Fact]
    public async Task CheckRepositoryAsync_UsesSavedRepositoryPort()
    {
        var store = new FakeServerPortsStore { Data = { ["srv"] = new ServerPortsSettings(0, 0, 1549) } };
        var service = new FakeDiagnosticsService();
        var vm = new NetworkDiagnosticsViewModel(
            service, Target("srv", 1541), portsStore: store);

        await vm.CheckRepositoryAsync();

        Assert.Equal(new[] { 1549 }, service.LastPorts);
    }

    [Fact]
    public async Task CheckRepositoryAsync_SavesRepositoryPortForServer()
    {
        var store = new FakeServerPortsStore();
        var vm = new NetworkDiagnosticsViewModel(
            new FakeDiagnosticsService(), Target("srv", 1541), portsStore: store);

        await vm.CheckRepositoryAsync();

        Assert.True(store.Data.TryGetValue("srv", out var saved));
        Assert.Equal(OneCPorts.Repository, saved!.Repository);
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