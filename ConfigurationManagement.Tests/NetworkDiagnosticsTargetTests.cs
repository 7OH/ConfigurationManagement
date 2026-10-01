using Configuration_Management.Models;
using Configuration_Management.Services;
using Xunit;

namespace ConfigurationManagement.Tests;

/// <summary>
/// Тесты экстракции цели диагностики (этап 0.3.9.230, функция 12): из базы
/// клиент-серверной (хост + порт кластера из настроек), веб-базы (хост/порт из URL,
/// включая IPv6), файловой (null) и из монитора серверов.
/// </summary>
public sealed class NetworkDiagnosticsTargetTests
{
    private static Infobase BaseWith(Action<ConnectionSettings> configure)
    {
        var connection = new ConnectionSettings();
        configure(connection);
        return new Infobase { Connection = connection };
    }

    [Fact]
    public void FromInfobase_ClientServer_DefaultPort_UsesCluster1541()
    {
        var target = NetworkDiagnosticsTargets.FromInfobase(BaseWith(c =>
        {
            c.Type = ConnectionType.ClientServer;
            c.Server = "srv";
        }));

        Assert.NotNull(target);
        Assert.Equal("srv", target!.Host);
        Assert.Equal(new[] { 1541 }, target.Ports);
    }

    [Fact]
    public void FromInfobase_ClientServer_CustomPort_IsUsed()
    {
        var target = NetworkDiagnosticsTargets.FromInfobase(BaseWith(c =>
        {
            c.Type = ConnectionType.ClientServer;
            c.Server = "srv:1540";
        }));

        Assert.NotNull(target);
        Assert.Equal("srv", target!.Host);
        Assert.Equal(new[] { 1540 }, target.Ports);
    }

    [Fact]
    public void FromInfobase_ClientServer_EmptyServer_ReturnsNull()
    {
        var target = NetworkDiagnosticsTargets.FromInfobase(BaseWith(c =>
        {
            c.Type = ConnectionType.ClientServer;
            c.Server = "   ";
        }));

        Assert.Null(target);
    }

    [Fact]
    public void FromInfobase_WebServer_Http_ReturnsHostAndPort80()
    {
        var target = NetworkDiagnosticsTargets.FromInfobase(BaseWith(c =>
        {
            c.Type = ConnectionType.WebServer;
            c.WebUrl = "http://srv/base";
        }));

        Assert.NotNull(target);
        Assert.Equal("srv", target!.Host);
        Assert.Equal(new[] { 80 }, target.Ports);
    }

    [Fact]
    public void FromInfobase_WebServer_HttpsCustomPort_ReturnsHostAndPort()
    {
        var target = NetworkDiagnosticsTargets.FromInfobase(BaseWith(c =>
        {
            c.Type = ConnectionType.WebServer;
            c.WebUrl = "https://srv:8443/base";
        }));

        Assert.NotNull(target);
        Assert.Equal("srv", target!.Host);
        Assert.Equal(new[] { 8443 }, target.Ports);
    }

    [Fact]
    public void FromInfobase_WebServer_Ipv6Url_NormalizesHostWithoutBrackets()
    {
        var target = NetworkDiagnosticsTargets.FromInfobase(BaseWith(c =>
        {
            c.Type = ConnectionType.WebServer;
            c.WebUrl = "http://[::1]:8080/base";
        }));

        Assert.NotNull(target);
        Assert.Equal("::1", target!.Host);
        Assert.Equal(new[] { 8080 }, target.Ports);
    }

    [Fact]
    public void FromInfobase_WebServer_EmptyOrBrokenUrl_ReturnsNull()
    {
        Assert.Null(NetworkDiagnosticsTargets.FromInfobase(BaseWith(c =>
        {
            c.Type = ConnectionType.WebServer;
            c.WebUrl = "   ";
        })));
        Assert.Null(NetworkDiagnosticsTargets.FromInfobase(BaseWith(c =>
        {
            c.Type = ConnectionType.WebServer;
            c.WebUrl = "not-a-url";
        })));
    }

    [Fact]
    public void FromInfobase_File_ReturnsNull()
    {
        var target = NetworkDiagnosticsTargets.FromInfobase(BaseWith(c =>
        {
            c.Type = ConnectionType.File;
            c.FilePath = @"C:\base";
        }));

        Assert.Null(target);
    }

    [Fact]
    public void FromInfobase_NullBase_ReturnsNull()
    {
        Assert.Null(NetworkDiagnosticsTargets.FromInfobase(null));
    }

    [Fact]
    public void FromServerMonitor_AddressAndPort_AreUsed()
    {
        var target = NetworkDiagnosticsTargets.FromServerMonitor("srv", 1545);

        Assert.Equal("srv", target.Host);
        Assert.Equal(new[] { 1545 }, target.Ports);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void FromServerMonitor_EmptyAddress_FallsBackToLocalhost(string? address)
    {
        var target = NetworkDiagnosticsTargets.FromServerMonitor(address, 1540);

        Assert.Equal("localhost", target.Host);
        Assert.Equal(new[] { 1540 }, target.Ports);
    }

    [Fact]
    public void FromServerMonitor_NonPositivePort_FallsBackToAgent1540()
    {
        var target = NetworkDiagnosticsTargets.FromServerMonitor("srv", 0);

        Assert.Equal("srv", target.Host);
        Assert.Equal(new[] { 1540 }, target.Ports);
    }
}