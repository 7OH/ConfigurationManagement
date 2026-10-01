using Configuration_Management.Services;
using Xunit;

namespace ConfigurationManagement.Tests;

/// <summary>
/// Тесты быстрой TCP-проверки перед COM (этап 0.3.9.233, функция 12): флаг выключен —
/// COM-путь (Skip); закрытый порт/таймаут — quick-fail (FailFast); открытый порт,
/// пустой/битый адрес и исключение зонда — Continue (финальное слово за COM).
/// </summary>
public sealed class NetworkAvailabilityPrecheckTests
{
    private static Task<TcpProbeResult> Probe(bool reachable, string? errorCode = null)
        => Task.FromResult(new TcpProbeResult(reachable, reachable ? 3 : null, errorCode));

    [Fact]
    public async Task Evaluate_Disabled_ReturnsSkip_WithoutCallingProbe()
    {
        var called = false;
        var verdict = await NetworkAvailabilityPrecheck.EvaluateAsync(
            enabled: false,
            host: "srv",
            port: 1541,
            tcpProbe: (h, p, t, ct) =>
            {
                called = true;
                return Probe(true);
            });

        Assert.Equal(NetworkAvailabilityPrecheck.Verdict.Skip, verdict);
        Assert.False(called);
    }

    [Fact]
    public async Task Evaluate_Enabled_ClosedPort_ReturnsFailFast()
    {
        var verdict = await NetworkAvailabilityPrecheck.EvaluateAsync(
            enabled: true, host: "srv", port: 1541,
            tcpProbe: (h, p, t, ct) => Probe(false, "refused"));

        Assert.Equal(NetworkAvailabilityPrecheck.Verdict.FailFast, verdict);
    }

    [Fact]
    public async Task Evaluate_Enabled_Timeout_ReturnsFailFast()
    {
        var verdict = await NetworkAvailabilityPrecheck.EvaluateAsync(
            enabled: true, host: "srv", port: 1541,
            tcpProbe: (h, p, t, ct) => Probe(false, "timeout"));

        Assert.Equal(NetworkAvailabilityPrecheck.Verdict.FailFast, verdict);
    }

    [Fact]
    public async Task Evaluate_Enabled_OpenPort_ReturnsContinue()
    {
        var verdict = await NetworkAvailabilityPrecheck.EvaluateAsync(
            enabled: true, host: "srv", port: 1541,
            tcpProbe: (h, p, t, ct) => Probe(true));

        Assert.Equal(NetworkAvailabilityPrecheck.Verdict.Continue, verdict);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Evaluate_Enabled_EmptyHost_ReturnsContinue(string? host)
    {
        var verdict = await NetworkAvailabilityPrecheck.EvaluateAsync(
            enabled: true, host: host, port: 1541,
            tcpProbe: (h, p, t, ct) => Probe(true));

        Assert.Equal(NetworkAvailabilityPrecheck.Verdict.Continue, verdict);
    }

    [Fact]
    public async Task Evaluate_Enabled_InvalidAddress_ReturnsContinue_WithoutCallingProbe()
    {
        var called = false;
        var verdict = await NetworkAvailabilityPrecheck.EvaluateAsync(
            enabled: true, host: "host:abc", port: 1541,
            tcpProbe: (h, p, t, ct) =>
            {
                called = true;
                return Probe(true);
            });

        Assert.Equal(NetworkAvailabilityPrecheck.Verdict.Continue, verdict);
        Assert.False(called);
    }

    [Fact]
    public async Task Evaluate_ProbeThrows_ReturnsContinue()
    {
        var verdict = await NetworkAvailabilityPrecheck.EvaluateAsync(
            enabled: true, host: "srv", port: 1541,
            tcpProbe: (h, p, t, ct) => throw new System.Net.Sockets.SocketException());

        Assert.Equal(NetworkAvailabilityPrecheck.Verdict.Continue, verdict);
    }

    [Fact]
    public async Task Evaluate_HostWithPort_ParsesPortFromHost()
    {
        int? probedPort = null;
        var verdict = await NetworkAvailabilityPrecheck.EvaluateAsync(
            enabled: true, host: "srv:1540", port: 1541,
            tcpProbe: (h, p, t, ct) =>
            {
                probedPort = p;
                return Probe(true);
            });

        Assert.Equal(NetworkAvailabilityPrecheck.Verdict.Continue, verdict);
        Assert.Equal(1540, probedPort); // порт из адреса имеет приоритет
    }
}