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

    [Theory]
    [InlineData("UT", new[] { "11", "10.3" })]
    [InlineData("KA", new[] { "2.5", "2.0", "1.1", "1.0" })]
    [InlineData("Retail", new[] { "3.0", "2.3" })]
    public void Editions_IncludeUserList(string code, string[] editions)
    {
        // Редакции/каталоги релизов по списку 7OH (issue #321).
        var config = GetByCode(code);
        foreach (var edition in editions)
            Assert.Contains(config.Editions, e => e.Red == edition);
    }

    private static OneCConfigType GetByCode(string code)
    {
        var config = BuiltInConfigTypes.All.FirstOrDefault(c => c.Code == code);
        Assert.NotNull(config);
        return config!;
    }
}