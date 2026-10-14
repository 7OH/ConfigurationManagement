using System.Collections.Generic;
using Configuration_Management.Models;
using Configuration_Management.Services;
using Xunit;

namespace ConfigurationManagement.Tests;

/// <summary>
/// Тесты фильтра «Только в базах» окна «Актуальные релизы» (issue #375): конфигурация
/// считается присутствующей в списке баз по явной привязке (<c>UpdateConfigCode</c>) либо
/// по эвристике сопоставления имени конфигурации базы (<c>ConfigurationName</c>).
/// </summary>
public sealed class ActualReleasesBaseFilterTests
{
    private static OneCConfigType Config(string code, string name, string configName = "") => new()
    {
        Code = code,
        Name = name,
        ConfigName = configName,
        Editions = { new OneCConfigEdition { Name = "1.0", Red = "1.0" } },
    };

    private static Infobase Base(
        string name, string updateConfigCode = "", string configurationName = "", string configurationVersion = "") => new()
    {
        Id = name,
        Name = name,
        UpdateConfigCode = updateConfigCode,
        ConfigurationName = configurationName,
        ConfigurationVersion = configurationVersion,
    };

    [Fact]
    public void CollectPresentConfigCodes_ByBinding_ReturnsConfigCode()
    {
        var configs = new List<OneCConfigType>
        {
            Config("BP", "Бухгалтерия предприятия"),
            Config("ZUP", "Зарплата и управление персоналом"),
        };
        var bases = new List<Infobase> { Base("Бухгалтерия", updateConfigCode: "BP") };

        var present = ActualReleasesBaseFilter.CollectPresentConfigCodes(configs, bases);

        Assert.Contains("BP", present);
        Assert.DoesNotContain("ZUP", present);
    }

    [Fact]
    public void CollectPresentConfigCodes_ByHeuristic_ReturnsConfigCode()
    {
        var configs = new List<OneCConfigType>
        {
            Config("BP", "Бухгалтерия предприятия"),
            Config("ZUP", "Зарплата и управление персоналом"),
        };
        // Привязки нет, но имя конфигурации базы сопоставляется с типовой по эвристике.
        var bases = new List<Infobase>
        {
            Base("Рабочая", configurationName: "1С:Зарплата и управление персоналом, редакция 3.1"),
        };

        var present = ActualReleasesBaseFilter.CollectPresentConfigCodes(configs, bases);

        Assert.Contains("ZUP", present);
        Assert.DoesNotContain("BP", present);
    }

    [Fact]
    public void CollectPresentConfigCodes_NoMatch_ReturnsEmpty()
    {
        var configs = new List<OneCConfigType> { Config("BP", "Бухгалтерия предприятия") };
        var bases = new List<Infobase> { Base("Неизвестная", configurationName: "Моя самописная") };

        var present = ActualReleasesBaseFilter.CollectPresentConfigCodes(configs, bases);

        Assert.Empty(present);
    }

    [Fact]
    public void CollectPresentConfigCodes_EmptyOrNull_ReturnsEmpty()
    {
        var configs = new List<OneCConfigType> { Config("BP", "Бухгалтерия предприятия") };

        Assert.Empty(ActualReleasesBaseFilter.CollectPresentConfigCodes(configs, null));
        Assert.Empty(ActualReleasesBaseFilter.CollectPresentConfigCodes(configs, new List<Infobase>()));
        Assert.Empty(ActualReleasesBaseFilter.CollectPresentConfigCodes(null, new List<Infobase> { Base("X") }));
        Assert.Empty(ActualReleasesBaseFilter.CollectPresentConfigCodes(new List<OneCConfigType>(), new List<Infobase> { Base("X") }));
    }

    [Fact]
    public void CollectPresentConfigCodes_MultipleBases_CollectsAll()
    {
        var configs = new List<OneCConfigType>
        {
            Config("BP", "Бухгалтерия предприятия"),
            Config("ZUP", "Зарплата и управление персоналом"),
            Config("UT", "Управление торговлей"),
        };
        var bases = new List<Infobase>
        {
            Base("Бухгалтерия", updateConfigCode: "BP"),
            Base("Зарплата", configurationName: "Зарплата и управление персоналом"),
        };

        var present = ActualReleasesBaseFilter.CollectPresentConfigCodes(configs, bases);

        Assert.Contains("BP", present);
        Assert.Contains("ZUP", present);
        Assert.DoesNotContain("UT", present);
    }

    [Fact]
    public void CollectPresentConfigCodes_StaleBinding_FallsBackToHeuristic()
    {
        // Привязка указывает на несуществующий код — используется эвристика по имени.
        var configs = new List<OneCConfigType> { Config("BP", "Бухгалтерия предприятия") };
        var bases = new List<Infobase>
        {
            Base("Бухгалтерия", updateConfigCode: "STALE", configurationName: "Бухгалтерия предприятия"),
        };

        var present = ActualReleasesBaseFilter.CollectPresentConfigCodes(configs, bases);

        Assert.Contains("BP", present);
    }

    [Fact]
    public void IsPresent_ConfigWithCodeInSet_ReturnsTrue()
    {
        var config = Config("BP", "Бухгалтерия предприятия");
        var present = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase) { "BP" };

        Assert.True(ActualReleasesBaseFilter.IsPresent(config, present));
    }

    [Fact]
    public void IsPresent_ConfigWithoutCode_ReturnsFalse()
    {
        var config = Config("", "Без кода");
        var present = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase) { "BP" };

        Assert.False(ActualReleasesBaseFilter.IsPresent(config, present));
    }

    [Fact]
    public void IsPresent_NullConfig_ReturnsFalse()
    {
        var present = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase) { "BP" };

        Assert.False(ActualReleasesBaseFilter.IsPresent(null, present));
    }

    [Fact]
    public void CollectPresentEditionReds_ByBinding_ReturnsRedFromVersion()
    {
        var configs = new List<OneCConfigType> { Config("UPP", "Управление производственным предприятием") };
        var bases = new List<Infobase>
        {
            Base("УПП 1.3", updateConfigCode: "UPP", configurationVersion: "1.3.285.1"),
        };

        var reds = ActualReleasesBaseFilter.CollectPresentEditionReds(configs, bases);

        Assert.True(reds.TryGetValue("UPP", out var set));
        Assert.Contains("1.3", set);
    }

    [Fact]
    public void CollectPresentEditionReds_ByHeuristic_ReturnsRedFromVersion()
    {
        var configs = new List<OneCConfigType> { Config("UPP", "Управление производственным предприятием") };
        var bases = new List<Infobase>
        {
            Base("УПП", configurationName: "Управление производственным предприятием", configurationVersion: "1.2.10.1"),
        };

        var reds = ActualReleasesBaseFilter.CollectPresentEditionReds(configs, bases);

        Assert.True(reds.TryGetValue("UPP", out var set));
        Assert.Contains("1.2", set);
    }

    [Fact]
    public void CollectPresentEditionReds_NoVersion_ReturnsEmpty()
    {
        // Версия базы не задана — редакции не сопоставляются, запись для конфигурации
        // не создаётся (в окне тогда показываются все редакции, см. IsEditionPresent_NoReds).
        var configs = new List<OneCConfigType> { Config("UPP", "Управление производственным предприятием") };
        var bases = new List<Infobase> { Base("УПП", updateConfigCode: "UPP") };

        var reds = ActualReleasesBaseFilter.CollectPresentEditionReds(configs, bases);

        Assert.Empty(reds);
    }

    [Fact]
    public void CollectPresentEditionReds_MultipleBases_CollectsAllReds()
    {
        var configs = new List<OneCConfigType> { Config("UPP", "Управление производственным предприятием") };
        var bases = new List<Infobase>
        {
            Base("УПП 1.2", updateConfigCode: "UPP", configurationVersion: "1.2.10.1"),
            Base("УПП 1.3", updateConfigCode: "UPP", configurationVersion: "1.3.285.1"),
        };

        var reds = ActualReleasesBaseFilter.CollectPresentEditionReds(configs, bases);

        Assert.True(reds.TryGetValue("UPP", out var set));
        Assert.Contains("1.2", set);
        Assert.Contains("1.3", set);
    }

    [Fact]
    public void CollectPresentEditionReds_NoMatch_ReturnsEmpty()
    {
        var configs = new List<OneCConfigType> { Config("UPP", "Управление производственным предприятием") };
        var bases = new List<Infobase>
        {
            Base("Неизвестная", configurationName: "Моя самописная", configurationVersion: "1.3.1.1"),
        };

        var reds = ActualReleasesBaseFilter.CollectPresentEditionReds(configs, bases);

        Assert.Empty(reds);
    }

    [Fact]
    public void IsEditionPresent_RedInSet_ReturnsTrue()
    {
        var edition = new OneCConfigEdition { Name = "1.3", Red = "1.3" };
        var reds = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase) { "1.3" };

        Assert.True(ActualReleasesBaseFilter.IsEditionPresent(edition, reds));
    }

    [Fact]
    public void IsEditionPresent_RedNotInSet_ReturnsFalse()
    {
        var edition = new OneCConfigEdition { Name = "1.2", Red = "1.2" };
        var reds = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase) { "1.3" };

        Assert.False(ActualReleasesBaseFilter.IsEditionPresent(edition, reds));
    }

    [Fact]
    public void IsEditionPresent_NoReds_ReturnsTrue()
    {
        var edition = new OneCConfigEdition { Name = "1.2", Red = "1.2" };

        Assert.True(ActualReleasesBaseFilter.IsEditionPresent(edition, null));
        Assert.True(ActualReleasesBaseFilter.IsEditionPresent(edition, new HashSet<string>()));
    }

    [Fact]
    public void IsEditionPresent_EditionWithoutRed_ReturnsTrue()
    {
        var edition = new OneCConfigEdition { Name = "Без версии", Red = "" };
        var reds = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase) { "1.3" };

        Assert.True(ActualReleasesBaseFilter.IsEditionPresent(edition, reds));
    }

    [Fact]
    public void IsEditionPresent_NullEdition_ReturnsFalse()
    {
        Assert.False(ActualReleasesBaseFilter.IsEditionPresent(null, new HashSet<string>()));
    }

    [Fact]
    public void IsEditionPresent_RedIsPrefixOfPresentRed_ReturnsTrue()
    {
        // issue #375: версия базы «11.5.25.72» → извлечённая редакция «11.5»; редакция
        // типовой «11» (Управление торговлей) должна считаться присутствующей.
        var edition = new OneCConfigEdition { Name = "11", Red = "11" };
        var reds = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase) { "11.5" };

        Assert.True(ActualReleasesBaseFilter.IsEditionPresent(edition, reds));
    }

    [Fact]
    public void IsEditionPresent_SingleSegmentRed_MatchesTwoSegmentPresentRed()
    {
        // Бухгалтерия предприятия: редакция «3» (Red = "3") ↔ версия базы «3.0.142.32».
        var edition = new OneCConfigEdition { Name = "3.0", Red = "3" };
        var reds = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase) { "3.0" };

        Assert.True(ActualReleasesBaseFilter.IsEditionPresent(edition, reds));
    }

    [Fact]
    public void IsEditionPresent_RedNotPrefixOfPresentRed_ReturnsFalse()
    {
        // Редакция «10.3» не матчится с версией базы «11.5.25.72» (извлечено «11.5»).
        var edition = new OneCConfigEdition { Name = "10.3", Red = "10.3" };
        var reds = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase) { "11.5" };

        Assert.False(ActualReleasesBaseFilter.IsEditionPresent(edition, reds));
    }

    [Fact]
    public void IsEditionPresent_RedIsPrefixOfPresentRed_Regression13()
    {
        // Регрессия: «1.3» ↔ «1.3.285.1» (извлечено «1.3») — точное совпадение.
        var edition = new OneCConfigEdition { Name = "1.3", Red = "1.3" };
        var reds = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase) { "1.3" };

        Assert.True(ActualReleasesBaseFilter.IsEditionPresent(edition, reds));
    }

    [Fact]
    public void CollectPresentEditionReds_UtVersion115_MatchesEdition11()
    {
        // Полный сценарий issue #375: база УТ с версией «11.5.25.72» и привязкой «UT»;
        // редакция «11» должна быть показана, «10.3» — нет.
        var config = new OneCConfigType
        {
            Code = "UT",
            Name = "Управление торговлей",
            Editions =
            {
                new OneCConfigEdition { Name = "11", Red = "11" },
                new OneCConfigEdition { Name = "10.3", Red = "10.3" },
            },
        };
        var configs = new List<OneCConfigType> { config };
        var bases = new List<Infobase>
        {
            Base("Торговый дом", updateConfigCode: "UT", configurationVersion: "11.5.25.72"),
        };

        var reds = ActualReleasesBaseFilter.CollectPresentEditionReds(configs, bases);

        Assert.True(reds.TryGetValue("UT", out var set));
        Assert.Contains("11.5", set);
        Assert.True(ActualReleasesBaseFilter.IsEditionPresent(config.Editions[0], set));
        Assert.False(ActualReleasesBaseFilter.IsEditionPresent(config.Editions[1], set));
    }
}
