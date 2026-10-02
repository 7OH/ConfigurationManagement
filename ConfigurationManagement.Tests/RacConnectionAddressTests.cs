using Configuration_Management.Services;
using Xunit;

namespace ConfigurationManagement.Tests;

/// <summary>
/// Тесты нормализации точки подключения rac — <see cref="RacConnectionAddress.Split"/>:
/// разделение адреса «host[:port]» на хост и порт с приоритетом порта, указанного в
/// адресе, над значением отдельного поля «Порт» (issue #324). Без нормализации ввод
/// «localhost:27545» в поле адреса при дефолтном поле «Порт» дал бы rac невалидный
/// токен «localhost:27545:1540».
/// </summary>
public sealed class RacConnectionAddressTests
{
    [Fact]
    public void Split_PlainHost_UsesFieldPort()
    {
        var (host, port) = RacConnectionAddress.Split("srv1", 1545);

        Assert.Equal("srv1", host);
        Assert.Equal(1545, port);
    }

    [Fact]
    public void Split_HostPortInAddress_TakesPriorityOverFieldPort()
    {
        // Ключевой сценарий issue #324: порт в адресе (как в командной строке
        // rac.exe localhost:27545 cluster list) главнее поля «Порт» (1540 по умолчанию).
        var (host, port) = RacConnectionAddress.Split("localhost:27545", 1540);

        Assert.Equal("localhost", host);
        Assert.Equal(27545, port);
    }

    [Fact]
    public void Split_EmptyAddress_ReturnsLocalhostWithDefaultPort()
    {
        var (host, port) = RacConnectionAddress.Split("   ", 0);

        Assert.Equal("localhost", host);
        Assert.Equal(IRacClient.DefaultPort, port);
    }

    [Fact]
    public void Split_InvalidPortInAddress_TreatsWholeValueAsHost()
    {
        // «host:abc» — не число: весь введённый текст остаётся хостом, порт из поля.
        var (host, port) = RacConnectionAddress.Split("srv1:abc", 1540);

        Assert.Equal("srv1:abc", host);
        Assert.Equal(1540, port);
    }

    [Fact]
    public void Split_HostWithTrailingColon_TreatsWholeValueAsHost()
    {
        var (host, port) = RacConnectionAddress.Split("srv1:", 1540);

        Assert.Equal("srv1:", host);
        Assert.Equal(1540, port);
    }

    [Fact]
    public void Split_BareIpv6_WithoutBrackets_TreatedAsHost()
    {
        // Голый IPv6 содержит «:», но не является «host:port» — остаётся хостом целиком.
        var (host, port) = RacConnectionAddress.Split("2001:db8::1", 1540);

        Assert.Equal("2001:db8::1", host);
        Assert.Equal(1540, port);
    }

    [Fact]
    public void Split_HostPortInAddress_OverridesInvalidFieldPort()
    {
        var (host, port) = RacConnectionAddress.Split("localhost:27545", 0);

        Assert.Equal("localhost", host);
        Assert.Equal(27545, port);
    }

    [Fact]
    public void Split_PortBounds_AreValidated()
    {
        // Порт 0 и 65536 невалидны — значение адреса целиком остаётся хостом.
        var (host0, port0) = RacConnectionAddress.Split("srv1:0", 1540);
        Assert.Equal("srv1:0", host0);
        Assert.Equal(1540, port0);

        var (hostMax, portMax) = RacConnectionAddress.Split("srv1:65536", 1540);
        Assert.Equal("srv1:65536", hostMax);
        Assert.Equal(1540, portMax);
    }
}