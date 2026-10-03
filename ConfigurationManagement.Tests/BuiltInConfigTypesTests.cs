using System.Linq;
using Configuration_Management.Models;
using Configuration_Management.Services;
using Xunit;

namespace ConfigurationManagement.Tests;

/// <summary>
/// Тесты данных встроенного набора типовых конфигураций (issue #321, часть 1):
/// поле «Имя конфигурации» (<c>OneCConfigType.ConfigName</c>) заполнено по списку
/// пользователя и НЕ участвует в построении адреса обновлений.
/// </summary>
public sealed class BuiltInConfigTypesTests
{
    [Theory]
    [InlineData("BP", "БухгалтерияПредприятия")]
    [InlineData("ZUP", "ЗарплатаИУправлениеПерсоналом")]
    [InlineData("UT", "УправлениеТорговлей")]
    [InlineData("KA", "КомплекснаяАвтоматизация")]
    [InlineData("Retail", "Retail")]
    [InlineData("ERP", "УправлениеПредприятием")]
    [InlineData("BGU", "БухгалтерияГосударственногоУчреждения")]
    public void BuiltInConfigs_HaveConfigName_AsSpecifiedByUser(string code, string expected)
    {
        var config = GetByCode(code);

        Assert.Equal(expected, config.ConfigName);
    }

    [Fact]
    public void AllBuiltInConfigs_HaveNonEmptyConfigName()
    {
        // Список 7OH (issue #321): для всех поставляемых конфигураций имя задано.
        foreach (var config in BuiltInConfigTypes.All)
            Assert.False(string.IsNullOrWhiteSpace(config.ConfigName),
                $"ConfigName пуст у конфигурации {config.Code}");
    }

    [Fact]
    public void ConfigName_DoesNotAffectEffectiveUrlCode()
    {
        // «Имя конфигурации» служит для сопоставления и не попадает в адрес обновлений:
        // EffectiveUrlCode строится из UrlCode/Name, а не из ConfigName.
        foreach (var config in BuiltInConfigTypes.All)
        {
            Assert.NotEqual(config.ConfigName, config.EffectiveUrlCode);
            Assert.Equal(
                string.IsNullOrWhiteSpace(config.UrlCode) ? config.Name : config.UrlCode,
                config.EffectiveUrlCode);
        }
    }

    [Fact]
    public void AllBuiltInConfigs_HaveNonEmptyNick()
    {
        // Ники из списка 7OH (issue #321): у каждой поставляемой конфигурации задан
        // каталог на releases.1c.ru (после «Восстановить типовые» колонка «НИК» не пустая).
        foreach (var config in BuiltInConfigTypes.All)
            Assert.False(string.IsNullOrWhiteSpace(config.Nick),
                $"Nick пуст у конфигурации {config.Code}");
    }

    [Theory]
    [InlineData("UT", new[] { "11", "10.3" })]
    [InlineData("KA", new[] { "2.0", "1.1", "1.0" })]
    [InlineData("Retail", new[] { "3.0", "2.3" })]
    public void Editions_IncludeUserList(string code, string[] editions)
    {
        // Редакции/каталоги релизов по списку 7OH (issue #321).
        var config = GetByCode(code);
        foreach (var edition in editions)
            Assert.Contains(config.Editions, e => e.Red == edition);
    }

    [Theory]
    [InlineData("BP", "3.0", "https://releases.1c.ru/project/Accounting30")]
    [InlineData("BP", "2.0", "https://releases.1c.ru/project/Accounting20_82")]
    [InlineData("UT", "11", "https://releases.1c.ru/project/Trade110")]
    [InlineData("Retail", "2.3", "https://releases.1c.ru/project/Retail23")]
    public void Editions_WithSeparateCatalog_HaveUrlOverride(string code, string name, string expectedUrl)
    {
        // Редакция с собственным каталогом релизов (ник из списка 7OH, issue #321):
        // адрес строится из UrlOverride, а не из ника конфигурации.
        var config = GetByCode(code);
        var edition = config.Editions.FirstOrDefault(e => e.Name == name);
        Assert.NotNull(edition);
        Assert.Equal(expectedUrl, edition!.UrlOverride);
    }

    private static OneCConfigType GetByCode(string code)
    {
        var config = BuiltInConfigTypes.All.FirstOrDefault(c => c.Code == code);
        Assert.NotNull(config);
        return config!;
    }
}