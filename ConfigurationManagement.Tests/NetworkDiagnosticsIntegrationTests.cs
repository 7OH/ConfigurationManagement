using Configuration_Management.Models;
using Configuration_Management.Services;
using Configuration_Management.ViewModels;
using Xunit;

namespace ConfigurationManagement.Tests;

/// <summary>
/// Сквозной тест интеграции функции диагностики (этап 0.3.9.232): база списка →
/// экстракция цели → ViewModel → прогон с fake-сервисом → строки таблицы портов.
/// Проверяет связку сервисов без реальной сети.
/// </summary>
public sealed class NetworkDiagnosticsIntegrationTests
{
    private sealed class FakeDiagnosticsService : INetworkDiagnosticsService
    {
        public int CallCount { get; private set; }

        public Task<NetworkDiagnosticsResult> RunAsync(
            string address, IReadOnlyList<int> ports,
            int timeoutMs = 3000, CancellationToken cancellationToken = default,
            IReadOnlyList<string>? serviceKeys = null)
        {
            CallCount++;
            return Task.FromResult(new NetworkDiagnosticsResult
            {
                Host = address,
                HostValid = true,
                DnsOk = true,
                ResolvedAddresses = new[] { "10.0.0.5" },
                PingPerformed = true,
                PingOk = true,
                PingRttMs = 4,
                Ports = ports.Select(p => new NetworkPortProbe(p, OneCPorts.GetServiceKey(p),
                    DiagnosticPortState.Available, 3, null)).ToList()
            });
        }
    }

    private static Infobase ClientServerBase(string server, int port)
        => new() { Connection = new ConnectionSettings { Type = ConnectionType.ClientServer, Server = server, Port = port } };

    private static Infobase WebBase(string url)
        => new() { Connection = new ConnectionSettings { Type = ConnectionType.WebServer, WebUrl = url } };

    [Fact]
    public async Task ClientServerBase_TargetToVm_Run_FillsPortRowWithBasePort()
    {
        var target = NetworkDiagnosticsTargets.FromInfobase(ClientServerBase("srv", 1541));
        Assert.NotNull(target);

        var vm = new NetworkDiagnosticsViewModel(new FakeDiagnosticsService(), target!);
        await vm.RunAsync();

        Assert.Equal("srv", vm.Host);
        var row = Assert.Single(vm.Ports);
        Assert.Equal(1541, row.Port);
        Assert.True(row.IsAvailable);
        Assert.False(string.IsNullOrWhiteSpace(row.RttText));
    }

    [Fact]
    public async Task ClientServerBase_CustomPort_TargetCarriesIt_AndRunUsesIt()
    {
        var target = NetworkDiagnosticsTargets.FromInfobase(ClientServerBase("srv:1540", 1540));
        Assert.NotNull(target);

        var vm = new NetworkDiagnosticsViewModel(new FakeDiagnosticsService(), target!);
        await vm.RunAsync();

        Assert.Equal(1540, Assert.Single(vm.Ports).Port);
    }

    [Fact]
    public async Task WebBase_HostAndPortFromUrl_AreUsedInRun()
    {
        var target = NetworkDiagnosticsTargets.FromInfobase(WebBase("https://web-srv:8443/base"));
        Assert.NotNull(target);
        Assert.Equal("web-srv", target!.Host);
        Assert.Equal(new[] { 8443 }, target.Ports);

        var vm = new NetworkDiagnosticsViewModel(new FakeDiagnosticsService(), target);
        await vm.RunAsync();

        Assert.Equal("web-srv", vm.Host);
        Assert.Equal(8443, Assert.Single(vm.Ports).Port);
    }

    [Fact]
    public async Task FileBase_TargetIsNull_VmNotCreatable()
    {
        var target = NetworkDiagnosticsTargets.FromInfobase(
            new Infobase { Connection = new ConnectionSettings { Type = ConnectionType.File, FilePath = @"C:\b" } });

        Assert.Null(target);
    }
}