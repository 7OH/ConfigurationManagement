using Configuration_Management.Models;
using Configuration_Management.Services;
using Xunit;

namespace ConfigurationManagement.Tests;

/// <summary>
/// Тесты построения подсказок по результату диагностики (этап 0.3.9.230, функция 12):
/// матрица правил «состояния → набор ключей», порядок (критичные раньше справки)
/// и аргументы подстановок.
/// </summary>
public sealed class NetworkDiagnosticsHintsTests
{
    private static NetworkDiagnosticsResult Result(
        bool hostValid = true,
        bool dnsOk = true,
        bool pingPerformed = true,
        bool pingOk = true,
        string? pingErrorCode = null,
        params NetworkPortProbe[] ports) =>
        new()
        {
            Host = "srv",
            HostValid = hostValid,
            DnsOk = dnsOk,
            ResolvedAddresses = new[] { "10.0.0.5" },
            PingPerformed = pingPerformed,
            PingOk = pingOk,
            PingErrorCode = pingErrorCode,
            Ports = ports
        };

    private static NetworkPortProbe Port(int port, DiagnosticPortState state, string? errorCode = null)
        => new(port, OneCPorts.GetServiceKey(port), state, state == DiagnosticPortState.Available ? 5 : null, errorCode);

    [Fact]
    public void Build_InvalidAddress_ReturnsSingleInvalidAddressHint()
    {
        var hints = NetworkDiagnosticsHints.Build(Result(hostValid: false));

        var hint = Assert.Single(hints);
        Assert.Equal("Diagnostics.HintInvalidAddress", hint.Key);
    }

    [Fact]
    public void Build_DnsFailure_ReturnsSingleDnsHint_WithHostArgument()
    {
        var hints = NetworkDiagnosticsHints.Build(Result(dnsOk: false));

        var hint = Assert.Single(hints);
        Assert.Equal("Diagnostics.HintDnsFailed", hint.Key);
        Assert.Equal("srv", Assert.Single(hint.Args));
    }

    [Fact]
    public void Build_PortClosed_AddsPortClosedHint_WithPortAndServiceKey()
    {
        var hints = NetworkDiagnosticsHints.Build(Result(ports: Port(1541, DiagnosticPortState.Closed, "refused")));

        var hint = Assert.Single(hints, h => h.Key == "Diagnostics.HintPortClosed");
        Assert.Equal(1541, hint.Args[0]);
        Assert.Equal("Diagnostics.PortCluster", hint.Args[1]);
    }

    [Fact]
    public void Build_PortTimeout_AddsPortTimeoutHint()
    {
        var hints = NetworkDiagnosticsHints.Build(Result(ports: Port(1545, DiagnosticPortState.Timeout, "timeout")));

        var hint = Assert.Single(hints, h => h.Key == "Diagnostics.HintPortTimeout");
        Assert.Equal(1545, hint.Args[0]);
        Assert.Equal("Diagnostics.PortRas", hint.Args[1]);
    }

    [Fact]
    public void Build_AgentUpClusterDown_AddsAggregateHint()
    {
        var hints = NetworkDiagnosticsHints.Build(Result(ports: new[]
        {
            Port(1540, DiagnosticPortState.Available),
            Port(1541, DiagnosticPortState.Closed, "refused")
        }));

        Assert.Contains(hints, h => h.Key == "Diagnostics.HintAgentOkClusterDown" &&
                                    h.Args.Count == 1 &&
                                    Equals(h.Args[0], 1541));
    }

    [Fact]
    public void Build_AgentUpClusterAbsent_AddsAggregateHint()
    {
        // 1540 открыт, порт кластера в списке не проверялся — кластер неизвестен.
        var hints = NetworkDiagnosticsHints.Build(Result(ports: Port(1540, DiagnosticPortState.Available)));

        Assert.Contains(hints, h => h.Key == "Diagnostics.HintAgentOkClusterDown");
    }

    [Fact]
    public void Build_AllDown_AddsAllUnreachable_AfterPortHints()
    {
        var hints = NetworkDiagnosticsHints.Build(Result(ports: new[]
        {
            Port(1540, DiagnosticPortState.Closed, "refused"),
            Port(1541, DiagnosticPortState.Timeout, "timeout")
        }));

        // Порядок: сначала портовые (критичные), потом агрегатная.
        Assert.Equal("Diagnostics.HintPortClosed", hints[0].Key);
        Assert.Equal("Diagnostics.HintPortTimeout", hints[1].Key);
        Assert.Contains(hints, h => h.Key == "Diagnostics.HintAllUnreachable");
    }

    [Fact]
    public void Build_AllAvailable_AddsAllOk()
    {
        var hints = NetworkDiagnosticsHints.Build(Result(ports: new[]
        {
            Port(1540, DiagnosticPortState.Available),
            Port(1541, DiagnosticPortState.Available)
        }));

        Assert.Contains(hints, h => h.Key == "Diagnostics.HintAllOk");
        Assert.DoesNotContain(hints, h => h.Key == "Diagnostics.HintAllUnreachable");
    }

    [Fact]
    public void Build_PingNotPermitted_AddsPermissionHint_AfterPortHints()
    {
        var hints = NetworkDiagnosticsHints.Build(Result(
            pingPerformed: false, pingOk: false, pingErrorCode: "ping_permission",
            ports: Port(1540, DiagnosticPortState.Closed, "refused")));

        Assert.Equal("Diagnostics.HintPortClosed", hints[0].Key);   // критичное первым
        Assert.Contains(hints, h => h.Key == "Diagnostics.HintPingPermission");
    }

    [Fact]
    public void Build_PingDownButTcpUp_AddsPingBlockedHint_NotPermission()
    {
        var hints = NetworkDiagnosticsHints.Build(Result(
            pingPerformed: true, pingOk: false, pingErrorCode: "timeout",
            ports: Port(1540, DiagnosticPortState.Available)));

        Assert.Contains(hints, h => h.Key == "Diagnostics.HintPingBlockedButTcpOk");
        Assert.DoesNotContain(hints, h => h.Key == "Diagnostics.HintPingPermission");
    }

    [Fact]
    public void Build_RasOnly_AddsRasHint()
    {
        var hints = NetworkDiagnosticsHints.Build(Result(ports: new[]
        {
            Port(1545, DiagnosticPortState.Available),
            Port(1540, DiagnosticPortState.Closed, "refused"),
            Port(1541, DiagnosticPortState.Closed, "refused")
        }));

        Assert.Contains(hints, h => h.Key == "Diagnostics.HintRasOnly");
    }

    [Fact]
    public void Build_AllOk_NoProblematicHints()
    {
        var hints = NetworkDiagnosticsHints.Build(Result(ports: new[]
        {
            Port(1540, DiagnosticPortState.Available),
            Port(1541, DiagnosticPortState.Available),
            Port(1545, DiagnosticPortState.Available)
        }));

        Assert.DoesNotContain(hints, h => h.Key is
            "Diagnostics.HintPortClosed" or "Diagnostics.HintPortTimeout" or
            "Diagnostics.HintAllUnreachable" or "Diagnostics.HintRasOnly");
        Assert.Contains(hints, h => h.Key == "Diagnostics.HintAllOk");
    }

    [Fact]
    public void Build_NullResult_ReturnsEmpty()
    {
        Assert.Empty(NetworkDiagnosticsHints.Build(null!));
    }
}