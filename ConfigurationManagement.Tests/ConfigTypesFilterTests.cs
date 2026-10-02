using Configuration_Management.Models;
using Configuration_Management.ViewModels;
using Xunit;

namespace ConfigurationManagement.Tests;

/// <summary>
/// Тесты чистого фильтра строк списка типовых конфигураций (issue #321, часть 2):
/// подстрока без учёта регистра по наименованию, коду, сегменту адреса, нику и сводке
/// редакций; пустой запрос пропускает весь список.
/// </summary>
public sealed class ConfigTypesFilterTests
{
    private static ConfigTypeItemViewModel Create(OneCConfigType model) =>
        new(model, _ => { }, _ => { });

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Matches_EmptyQuery_AlwaysTrue(string? query)
    {
        var vm = Create(new OneCConfigType { Code = "ZUP", Name = "Зарплата и управление персоналом" });

        Assert.True(ConfigTypesFilter.Matches(vm, query));
    }

    [Fact]
    public void Matches_Name_Substring_IgnoreCase()
    {
        var vm = Create(new OneCConfigType { Code = "ZUP", Name = "Зарплата и управление персоналом" });

        Assert.True(ConfigTypesFilter.Matches(vm, "УПРАВЛЕНИЕ"));
        Assert.True(ConfigTypesFilter.Matches(vm, "зарплата"));
        Assert.False(ConfigTypesFilter.Matches(vm, "бухгалтерия"));
    }

    [Fact]
    public void Matches_Code_Substring()
    {
        var vm = Create(new OneCConfigType { Code = "ZUP_30", Name = "ЗУП" });

        Assert.True(ConfigTypesFilter.Matches(vm, "zup"));
        Assert.True(ConfigTypesFilter.Matches(vm, "30"));
        Assert.False(ConfigTypesFilter.Matches(vm, "BP"));
    }

    [Fact]
    public void Matches_UrlCode_Substring()
    {
        var vm = Create(new OneCConfigType { Code = "ACC", Name = "Бухгалтерия", UrlCode = "AccountingCorp30" });

        Assert.True(ConfigTypesFilter.Matches(vm, "accounting"));
        Assert.True(ConfigTypesFilter.Matches(vm, "corp30"));
    }

    [Fact]
    public void Matches_UrlCode_FallsBackToName_WhenEmpty()
    {
        // Сегмент пуст → отображаемый UrlCode равен имени (паттерн VM).
        var vm = Create(new OneCConfigType { Code = "MY", Name = "Моя конфигурация", UrlCode = string.Empty });

        Assert.True(ConfigTypesFilter.Matches(vm, "конфигурация"));
    }

    [Fact]
    public void Matches_Nick_Substring()
    {
        var vm = Create(new OneCConfigType { Code = "X", Name = "Конфигурация", Nick = "SomeNick" });

        Assert.True(ConfigTypesFilter.Matches(vm, "somenick"));
        Assert.True(ConfigTypesFilter.Matches(vm, "nick"));
    }

    [Fact]
    public void Matches_EditionsSummary_Substring()
    {
        var vm = Create(new OneCConfigType
        {
            Code = "ZUP",
            Name = "ЗУП",
            Editions = { new OneCConfigEdition { Name = "3.0", Red = "3.0" }, new OneCConfigEdition { Name = "3.1" } },
        });

        Assert.True(ConfigTypesFilter.Matches(vm, "3.1"));
        Assert.True(ConfigTypesFilter.Matches(vm, "3.0, 3.1"));
    }

    [Fact]
    public void Matches_TrimsWhitespaceAroundQuery()
    {
        var vm = Create(new OneCConfigType { Code = "ZUP", Name = "Зарплата" });

        Assert.True(ConfigTypesFilter.Matches(vm, "  зарплата  "));
    }
}