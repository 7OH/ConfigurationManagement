using System.Linq;
using Configuration_Management.Models;
using Configuration_Management.Themes;
using Xunit;

namespace ConfigurationManagement.Tests;

/// <summary>
/// Тесты каталога полноценных тем (Задача 1): поиск по идентификатору,
/// стабильный порядок встроенных тем и обёртка пользовательских схем
/// (<see cref="ThemeCatalog.WrapCustom"/>).
/// </summary>
public sealed class ThemeCatalogTests
{
    [Fact]
    public void Find_ReturnsBuiltInThemesById()
    {
        Assert.Equal("light", ThemeCatalog.Find("light")?.Id);
        Assert.Equal("dark", ThemeCatalog.Find("dark")?.Id);
        Assert.Equal("blue", ThemeCatalog.Find("blue")?.Id);
        Assert.Equal("green", ThemeCatalog.Find("green")?.Id);
    }

    [Fact]
    public void Find_IsCaseInsensitive()
    {
        Assert.Equal("light", ThemeCatalog.Find("LIGHT")?.Id);
        Assert.Equal("dark", ThemeCatalog.Find("DaRk")?.Id);
        Assert.Equal("blue", ThemeCatalog.Find("Blue")?.Id);
        Assert.Equal("green", ThemeCatalog.Find("GREEN")?.Id);
    }

    [Fact]
    public void Find_ReturnsNullForEmptyOrUnknownId()
    {
        Assert.Null(ThemeCatalog.Find(null));
        Assert.Null(ThemeCatalog.Find(string.Empty));
        Assert.Null(ThemeCatalog.Find("   "));
        Assert.Null(ThemeCatalog.Find("unknown"));
        Assert.Null(ThemeCatalog.Find("DarkTheme"));
    }

    [Fact]
    public void BuiltIn_OrderIsStable()
    {
        var ids = ThemeCatalog.BuiltIn.Select(t => t.Id).ToArray();

        Assert.Equal(new[] { "light", "dark", "blue", "green" }, ids);
    }

    [Fact]
    public void WrapCustom_BuildsThemeFromScheme()
    {
        var scheme = ColorScheme.Create("Моя схема", isDark: false);

        var theme = ThemeCatalog.WrapCustom(scheme, isDark: true);

        Assert.Equal("custom", theme.Id);
        Assert.Equal("Моя схема", theme.Name);
        Assert.True(theme.IsDark);
        Assert.Same(scheme, theme.Scheme);
        Assert.Equal(WindowButtonStyle.Fluent, theme.ButtonStyle);
        Assert.Equal(IconSet.Fluent, theme.Icons);
        // Акцент берётся из схемы (не из палитры).
        Assert.Equal(scheme.AccentColor, theme.AccentColor);
        Assert.False(string.IsNullOrWhiteSpace(theme.AccentColor));
    }

    [Fact]
    public void WrapCustom_NormalizesEmptySchemeAndFillsAccent()
    {
        // Схема без палитр и без акцента — обёртка не должна падать.
        var scheme = new ColorScheme { Name = "Пустая схема" };

        var theme = ThemeCatalog.WrapCustom(scheme, isDark: false);

        Assert.NotEmpty(theme.Scheme.LightColors);
        Assert.NotEmpty(theme.Scheme.DarkColors);
        Assert.Matches("^#[0-9A-Fa-f]{6}$", theme.AccentColor);
        Assert.Equal(theme.Scheme.AccentColor, theme.AccentColor);
    }

    [Fact]
    public void WrapCustom_NullSchemeFallsBackToLight()
    {
        var theme = ThemeCatalog.WrapCustom(null!, isDark: false);

        Assert.Equal("custom", theme.Id);
        Assert.NotEmpty(theme.Scheme.LightColors);
        Assert.NotEmpty(theme.Scheme.DarkColors);
        Assert.Matches("^#[0-9A-Fa-f]{6}$", theme.AccentColor);
    }
}