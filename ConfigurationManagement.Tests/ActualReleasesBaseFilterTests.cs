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

    private static Infobase Base(string name, string updateConfigCode = "", string configurationName = "") => new()
    {
        Id = name,
        Name = name,
        UpdateConfigCode = updateConfigCode,
        ConfigurationName = configurationName,
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
}
