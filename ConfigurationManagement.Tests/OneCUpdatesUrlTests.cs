using Configuration_Management.Services;
using Xunit;

namespace ConfigurationManagement.Tests;

/// <summary>
/// Тесты формирования адреса каталога релизов по нику конфигурации/сегменту базы
/// (<see cref="OneCUpdatesService.BuildNickUrl"/>) — без сетевых запросов (issue #322):
/// персональный сегмент базы должен подставляться в адрес releases.1c.ru/project/<ник>.
/// </summary>
public sealed class OneCUpdatesUrlTests
{
    [Fact]
    public void BuildNickUrl_ReturnsProjectUrlWithEscapedNick()
    {
        var url = OneCUpdatesService.BuildNickUrl("AccountingCorp30");

        Assert.Equal("https://releases.1c.ru/project/AccountingCorp30", url);
    }

    [Fact]
    public void BuildNickUrl_EmptyOrWhitespace_ReturnsEmptyString()
    {
        Assert.Equal(string.Empty, OneCUpdatesService.BuildNickUrl(null));
        Assert.Equal(string.Empty, OneCUpdatesService.BuildNickUrl(string.Empty));
        Assert.Equal(string.Empty, OneCUpdatesService.BuildNickUrl("   "));
    }

    [Fact]
    public void BuildNickUrl_TrimsInput()
    {
        var url = OneCUpdatesService.BuildNickUrl("  AccountingCorp30  ");

        Assert.Equal("https://releases.1c.ru/project/AccountingCorp30", url);
    }

    [Fact]
    public void BuildNickUrl_EscapesNonAsciiAndSpaces()
    {
        var url = OneCUpdatesService.BuildNickUrl("Корп Икс");

        // Пробелы и не-ASCII символы экранируются (Uri.EscapeDataString).
        Assert.StartsWith("https://releases.1c.ru/project/", url);
        Assert.DoesNotContain(" ", url);
        Assert.Contains("%", url);
    }
}