using Configuration_Management.Models;
using Configuration_Management.Services;
using Xunit;

namespace ConfigurationManagement.Tests;

/// <summary>
/// Тесты сервиса сетевой диагностики (этап 0.3.9.229, функция 12): парсер адреса
/// (host / host:port / IPv4 / IPv6 в скобках / невалидные), резолв DNS через
/// fake-делегат, ICMP-зонд (успех / нет прав / нет ответа), TCP-зонд
/// (успех / refused / timeout с RTT) и агрегация результата.
/// </summary>
public sealed class NetworkDiagnosticsServiceTests
{
    // ===================== Парсер адреса =====================

    [Theory]
    [InlineData("srv", "srv", 1541)]
    [InlineData("srv:1545", "srv", 1545)]
    [InlineData("  srv : 1540 ", "srv", 1540)]
    [InlineData("192.168.1.10", "192.168.1.10", 1541)]
    [InlineData("192.168.1.10:1540", "192.168.1.10", 1540)]
    [InlineData("host:", "host", 1541)]
    public void ParseAddress_ValidForms_ReturnsHostAndPort(string input, string expectedHost, int expectedPort)
    {
        var parsed = NetworkDiagnosticsService.ParseAddress(input);

        Assert.NotNull(parsed);
        Assert.Equal(expectedHost, parsed!.Host);
        Assert.Equal(expectedPort, parsed.Port);
    }

    [Theory]
    [InlineData("[2001:db8::1]", "2001:db8::1", 1541)]
    [InlineData("[2001:db8::1]:1540", "2001:db8::1", 1540)]
    [InlineData("[::1]", "::1", 1541)]
    public void ParseAddress_Ipv6Bracketed_ReturnsHostWithoutBrackets(string input, string expectedHost, int expectedPort)
    {
        var parsed = NetworkDiagnosticsService.ParseAddress(input);

        Assert.NotNull(parsed);
        Assert.Equal(expectedHost, parsed!.Host);
        Assert.Equal(expectedPort, parsed.Port);
    }

    [Theory]
    [InlineData("2001:db8::1", "2001:db8::1", 1541)]   // голый IPv6 без скобок — порта нет
    [InlineData("fe80::1", "fe80::1", 1541)]
    public void ParseAddress_BareIpv6_TreatedAsHostWithoutPort(string input, string expectedHost, int expectedPort)
    {
        var parsed = NetworkDiagnosticsService.ParseAddress(input);

        Assert.NotNull(parsed);
        Assert.Equal(expectedHost, parsed!.Host);
        Assert.Equal(expectedPort, parsed.Port);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("host:abc")]
    [InlineData("host:0")]
    [InlineData("host:-1")]
    [InlineData("host:70000")]
    [InlineData("[]")]
    [InlineData("[:1540]")]
    public void ParseAddress_Invalid_ReturnsNull(string input)
    {
        Assert.Null(NetworkDiagnosticsService.ParseAddress(input));
    }

    [Fact]
    public void ParseAddress_CustomDefaultPort_Applied()
    {
        var parsed = NetworkDiagnosticsService.ParseAddress("srv", defaultPort: 1545);

        Assert.NotNull(parsed);
        Assert.Equal("srv", parsed!.Host);
        Assert.Equal(1545, parsed.Port);
    }

    // ===================== Резолв DNS =====================

    [Fact]
    public async Task RunAsync_ResolveSuccess_CollectsAddresses_AndPings()
    {
        var service = new NetworkDiagnosticsService(
            resolveHost: (host, ct) => Task.FromResult<IReadOnlyList<string>>(
                new[] { "10.0.0.5", "2001:db8::5" }),
            ping: (host, timeoutMs, ct) => Task.FromResult(
                new PingProbeResult(true, true, 12, null)),
            tcpProbe: (host, port, timeoutMs, ct) => Task.FromResult(
                new TcpProbeResult(true, 5, null)));

        var result = await service.RunAsync("srv", new[] { 1540 }, 3000);

        Assert.True(result.HostValid);
        Assert.True(result.DnsOk);
        Assert.Null(result.DnsErrorCode);
        Assert.Equal(new[] { "10.0.0.5", "2001:db8::5" }, result.ResolvedAddresses);
        Assert.True(result.PingPerformed);
        Assert.True(result.PingOk);
        Assert.Equal(12, result.PingRttMs);
        Assert.Single(result.Ports);
        Assert.Equal(DiagnosticPortState.Available, result.Ports[0].State);
        Assert.Equal(5, result.Ports[0].RttMs);
    }

    [Fact]
    public async Task RunAsync_HostIsIp_SkipsDns_NoResolverCall()
    {
        var resolverCalled = false;
        var service = new NetworkDiagnosticsService(
            resolveHost: (host, ct) =>
            {
                resolverCalled = true;
                return Task.FromResult<IReadOnlyList<string>>(new[] { "10.0.0.5" });
            },
            ping: (host, timeoutMs, ct) => Task.FromResult(
                new PingProbeResult(true, true, 1, null)),
            tcpProbe: (host, port, timeoutMs, ct) => Task.FromResult(
                new TcpProbeResult(true, 1, null)));

        var result = await service.RunAsync("10.0.0.5", new[] { 1540 }, 3000);

        Assert.True(result.DnsOk);
        Assert.False(resolverCalled);
        Assert.Equal(new[] { "10.0.0.5" }, result.ResolvedAddresses);
        Assert.True(result.PingPerformed);
    }

    [Fact]
    public async Task RunAsync_DnsFailure_SkipsPingAndTcp_MarksPortsNotChecked()
    {
        var pingCalled = false;
        var tcpCalled = false;
        var service = new NetworkDiagnosticsService(
            resolveHost: (host, ct) => throw new System.Net.Sockets.SocketException(),
            ping: (host, timeoutMs, ct) =>
            {
                pingCalled = true;
                return Task.FromResult(new PingProbeResult(true, true, 1, null));
            },
            tcpProbe: (host, port, timeoutMs, ct) =>
            {
                tcpCalled = true;
                return Task.FromResult(new TcpProbeResult(true, 1, null));
            });

        var result = await service.RunAsync("unknown-host", new[] { 1540, 1541 }, 3000);

        Assert.True(result.HostValid);
        Assert.False(result.DnsOk);
        Assert.Equal("dns_failed", result.DnsErrorCode);
        Assert.False(pingCalled);
        Assert.False(tcpCalled);
        Assert.Equal(2, result.Ports.Count);
        Assert.All(result.Ports, p =>
        {
            Assert.Equal(DiagnosticPortState.NotChecked, p.State);
            Assert.Equal("dns_error", p.ErrorCode);
        });
    }

    [Fact]
    public async Task RunAsync_DnsReturnsEmpty_MarksFailure()
    {
        var service = new NetworkDiagnosticsService(
            resolveHost: (host, ct) => Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>()));

        var result = await service.RunAsync("srv", Array.Empty<int>(), 3000);

        Assert.False(result.DnsOk);
        Assert.Equal("dns_failed", result.DnsErrorCode);
    }

    [Fact]
    public async Task RunAsync_InvalidAddress_ReturnsHostInvalid_NoNetworkCalls()
    {
        var pingCalled = false;
        var service = new NetworkDiagnosticsService(
            ping: (host, timeoutMs, ct) =>
            {
                pingCalled = true;
                return Task.FromResult(new PingProbeResult(true, true, 1, null));
            });

        var result = await service.RunAsync("host:abc", new[] { 1540 }, 3000);

        Assert.False(result.HostValid);
        Assert.False(result.DnsOk);
        Assert.False(pingCalled);
        Assert.Empty(result.Ports);
    }

    // ===================== ICMP =====================

    [Fact]
    public async Task RunAsync_PingNotPermitted_ContinuesWithTcp_AndFlagsNotPerformed()
    {
        var service = new NetworkDiagnosticsService(
            resolveHost: (host, ct) => Task.FromResult<IReadOnlyList<string>>(new[] { "10.0.0.5" }),
            ping: (host, timeoutMs, ct) => Task.FromResult(
                new PingProbeResult(false, false, null, "ping_permission")),
            tcpProbe: (host, port, timeoutMs, ct) => Task.FromResult(
                new TcpProbeResult(true, 10, null)));

        var result = await service.RunAsync("srv", new[] { 1540 }, 3000);

        Assert.False(result.PingPerformed);
        Assert.False(result.PingOk);
        Assert.Equal("ping_permission", result.PingErrorCode);
        Assert.Single(result.Ports);
        Assert.Equal(DiagnosticPortState.Available, result.Ports[0].State); // TCP всё равно выполнен
    }

    [Fact]
    public async Task RunAsync_PingException_FlagsPermissionOrError_WithoutCrashing()
    {
        var service = new NetworkDiagnosticsService(
            resolveHost: (host, ct) => Task.FromResult<IReadOnlyList<string>>(new[] { "10.0.0.5" }),
            ping: (host, timeoutMs, ct) => throw new System.Net.NetworkInformation.PingException("no"),
            tcpProbe: (host, port, timeoutMs, ct) => Task.FromResult(
                new TcpProbeResult(true, 1, null)));

        var result = await service.RunAsync("srv", new[] { 1540 }, 3000);

        Assert.False(result.PingPerformed);
        Assert.Equal("ping_error", result.PingErrorCode);
        Assert.Single(result.Ports); // шаги не прерываются
    }

    // ===================== TCP =====================

    [Fact]
    public async Task RunAsync_TcpRefused_StateClosed_ErrorRefused()
    {
        var service = new NetworkDiagnosticsService(
            resolveHost: (host, ct) => Task.FromResult<IReadOnlyList<string>>(new[] { "10.0.0.5" }),
            ping: (host, timeoutMs, ct) => Task.FromResult(new PingProbeResult(true, true, 2, null)),
            tcpProbe: (host, port, timeoutMs, ct) => Task.FromResult(
                new TcpProbeResult(false, null, "refused")));

        var result = await service.RunAsync("srv", new[] { 1541 }, 3000);

        var probe = Assert.Single(result.Ports);
        Assert.Equal(DiagnosticPortState.Closed, probe.State);
        Assert.Equal("refused", probe.ErrorCode);
        Assert.Null(probe.RttMs);
    }

    [Fact]
    public async Task RunAsync_TcpTimeout_StateTimeout()
    {
        var service = new NetworkDiagnosticsService(
            resolveHost: (host, ct) => Task.FromResult<IReadOnlyList<string>>(new[] { "10.0.0.5" }),
            ping: (host, timeoutMs, ct) => Task.FromResult(new PingProbeResult(true, true, 2, null)),
            tcpProbe: (host, port, timeoutMs, ct) => Task.FromResult(
                new TcpProbeResult(false, null, "timeout")));

        var result = await service.RunAsync("srv", new[] { 1545 }, 3000);

        var probe = Assert.Single(result.Ports);
        Assert.Equal(DiagnosticPortState.Timeout, probe.State);
        Assert.Equal("timeout", probe.ErrorCode);
    }

    [Fact]
    public async Task RunAsync_PassesTimeoutMs_ToProbeDelegates()
    {
        int? seenTimeout = null;
        var service = new NetworkDiagnosticsService(
            resolveHost: (host, ct) => Task.FromResult<IReadOnlyList<string>>(new[] { "10.0.0.5" }),
            ping: (host, timeoutMs, ct) => Task.FromResult(new PingProbeResult(true, true, 1, null)),
            tcpProbe: (host, port, timeoutMs, ct) =>
            {
                seenTimeout = timeoutMs;
                return Task.FromResult(new TcpProbeResult(true, 1, null));
            });

        await service.RunAsync("srv", new[] { 1540 }, 1234);

        Assert.Equal(1234, seenTimeout);
    }

    // ===================== Агрегация =====================

    [Fact]
    public async Task RunAsync_MultiplePorts_ResultPreservesOrder_AndServiceKeys()
    {
        var service = new NetworkDiagnosticsService(
            resolveHost: (host, ct) => Task.FromResult<IReadOnlyList<string>>(new[] { "10.0.0.5" }),
            ping: (host, timeoutMs, ct) => Task.FromResult(new PingProbeResult(true, true, 1, null)),
            tcpProbe: (host, port, timeoutMs, ct) => Task.FromResult(
                new TcpProbeResult(true, (long)port, null)));

        var result = await service.RunAsync("srv", new[] { 1540, 1541, 1545 }, 3000);

        Assert.Equal(3, result.Ports.Count);
        Assert.Equal(1540, result.Ports[0].Port);
        Assert.Equal(1541, result.Ports[1].Port);
        Assert.Equal(1545, result.Ports[2].Port);
        Assert.Equal("Diagnostics.PortAgent", result.Ports[0].ServiceKey);
        Assert.Equal("Diagnostics.PortCluster", result.Ports[1].ServiceKey);
        Assert.Equal("Diagnostics.PortRas", result.Ports[2].ServiceKey);
    }

    [Fact]
    public async Task RunAsync_CustomPort_UsesCustomServiceKey()
    {
        var service = new NetworkDiagnosticsService(
            resolveHost: (host, ct) => Task.FromResult<IReadOnlyList<string>>(new[] { "10.0.0.5" }),
            ping: (host, timeoutMs, ct) => Task.FromResult(new PingProbeResult(true, true, 1, null)),
            tcpProbe: (host, port, timeoutMs, ct) => Task.FromResult(new TcpProbeResult(true, 1, null)));

        var result = await service.RunAsync("srv", new[] { 8080 }, 3000);

        Assert.Equal("Diagnostics.PortCustom", Assert.Single(result.Ports).ServiceKey);
    }

    [Fact]
    public async Task RunAsync_EmptyPorts_SkipsTcp_SetsStateNotChecked()
    {
        var tcpCalled = false;
        var service = new NetworkDiagnosticsService(
            resolveHost: (host, ct) => Task.FromResult<IReadOnlyList<string>>(new[] { "10.0.0.5" }),
            ping: (host, timeoutMs, ct) => Task.FromResult(new PingProbeResult(true, true, 1, null)),
            tcpProbe: (host, port, timeoutMs, ct) =>
            {
                tcpCalled = true;
                return Task.FromResult(new TcpProbeResult(true, 1, null));
            });

        var result = await service.RunAsync("srv", Array.Empty<int>(), 3000);

        Assert.True(result.DnsOk);
        Assert.True(result.PingPerformed);
        Assert.Empty(result.Ports);
        Assert.False(tcpCalled);
    }

    // ===================== OneCPorts =====================

    [Theory]
    [InlineData(1540, "Diagnostics.PortAgent")]
    [InlineData(1541, "Diagnostics.PortCluster")]
    [InlineData(1545, "Diagnostics.PortRas")]
    [InlineData(8080, "Diagnostics.PortCustom")]
    public void OneCPorts_GetServiceKey_MapsKnownAndCustom(int port, string expectedKey)
    {
        Assert.Equal(expectedKey, OneCPorts.GetServiceKey(port));
    }
}