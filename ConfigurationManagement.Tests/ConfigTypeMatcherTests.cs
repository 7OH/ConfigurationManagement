using System.Collections.Generic;
using Configuration_Management.Models;
using Configuration_Management.Services;
using Xunit;

namespace ConfigurationManagement.Tests;

/// <summary>
/// Тесты сопоставления имени конфигурации информационной базы с типовой конфигурацией 1С
/// (issue #322): приоритет признаков (точное имя → сегмент URL → вхождение по самому длинному
/// имени), ЗУП определяется как ЗУП, а не как БУХГАЛТЕРИЯ.
/// </summary>
public sealed class ConfigTypeMatcherTests
{
    private static OneCConfigType Config(string code, string name, string urlCode = "") => new()
    {
        Code = code,
        Name = name,
        UrlCode = urlCode,
        Editions = { new OneCConfigEdition { Name = "1.0", Red = "1.0" } },
    };

    [Fact]
    public void ExactName_ZupMatchesZup_NotBukh()
    {
        var configs = new List<OneCConfigType>
        {
            Config("BP", "Бухгалтерия предприятия", "Бухгалтерия предприятия"),
            Config("ZUP", "Зарплата и управление персоналом", "Зарплата и управление персоналом"),
            Config("UT", "Управление торговлей", "Управление торговлей"),
        };

        var match = ConfigTypeMatcher.FindByInfobaseName(configs, "Зарплата и управление персоналом");

        Assert.NotNull(match);
        Assert.Equal("ZUP", match!.Code);
        Assert.NotEqual("BP", match.Code);
    }

    [Fact]
    public void BaseNameWithPrefixOrSuffix_ZupStillZup()
    {
        var configs = new List<OneCConfigType>
        {
            Config("BP", "Бухгалтерия предприятия"),
            Config("ZUP", "Зарплата и управление персоналом"),
        };

        // «1С:» префикс и хвост «, редакция 3.1» — вхождение имени типовой в имя базы.
        var match = ConfigTypeMatcher.FindByInfobaseName(configs, "1С:Зарплата и управление персоналом, редакция 3.1");

        Assert.NotNull(match);
        Assert.Equal("ZUP", match!.Code);
    }

    [Fact]
    public void LongestContainedName_Wins_OverShortSubstring()
    {
        // Если имя базы содержит сразу несколько имён типовых, выбирается самое длинное —
        // короткое «Управление» не должно перекрыть «Зарплата и управление персоналом».
        var configs = new List<OneCConfigType>
        {
            Config("SHORT", "Управление"),
            Config("ZUP", "Зарплата и управление персоналом"),
            Config("BP", "Бухгалтерия предприятия"),
        };

        var match = ConfigTypeMatcher.FindByInfobaseName(configs, "Конфигурация 1С:Зарплата и управление персоналом");

        Assert.NotNull(match);
        Assert.Equal("ZUP", match!.Code);
    }

    [Fact]
    public void UrlCodeMatch_WhenNamesDiffer()
    {
        // Имя базы совпадает с сегментом URL типовой, хотя имена различаются (приоритет сегмента URL).
        var configs = new List<OneCConfigType>
        {
            Config("MY", "Моя конфигурация", "Бухгалтерия предприятия"),
        };

        var match = ConfigTypeMatcher.FindByInfobaseName(configs, "Бухгалтерия предприятия");

        Assert.NotNull(match);
        Assert.Equal("MY", match!.Code);
    }

    [Fact]
    public void EmptyOrNullName_ReturnsNull()
    {
        var configs = new List<OneCConfigType> { Config("BP", "Бухгалтерия предприятия") };

        Assert.Null(ConfigTypeMatcher.FindByInfobaseName(configs, null));
        Assert.Null(ConfigTypeMatcher.FindByInfobaseName(configs, "   "));
        Assert.Null(ConfigTypeMatcher.FindByInfobaseName(configs, string.Empty));
    }

    [Fact]
    public void UnknownName_ReturnsNull()
    {
        var configs = new List<OneCConfigType> { Config("BP", "Бухгалтерия предприятия") };

        Assert.Null(ConfigTypeMatcher.FindByInfobaseName(configs, "Совершенно другая конфигурация"));
    }

    [Fact]
    public void BuiltInSet_ZupVersionedBase_MatchesZupNotBukh()
    {
        // Реальный сценарий из issue #322: база ЗУП 3.1, список типовых — встроенный набор.
        var configs = new List<OneCConfigType>(BuiltInConfigTypes.All);

        var match = ConfigTypeMatcher.FindByInfobaseName(configs, "Зарплата и управление персоналом");

        Assert.NotNull(match);
        Assert.Equal("ZUP", match!.Code);
        Assert.Equal("Зарплата и управление персоналом", match.Name);
    }

    [Fact]
    public void BuiltInSet_BukhVariant_MatchesBukh()
    {
        var configs = new List<OneCConfigType>(BuiltInConfigTypes.All);

        var match = ConfigTypeMatcher.FindByInfobaseName(configs, "Бухгалтерия предприятия, ред. 3.0");

        Assert.NotNull(match);
        Assert.Equal("BP", match!.Code);
    }
}