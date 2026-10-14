using Configuration_Management.Models;
using Configuration_Management.Themes;
using Xunit;

namespace ConfigurationManagement.Tests;

/// <summary>
/// Тесты миграции старого выбора темы (устаревшее поле <see cref="AppSettings.Theme"/>
/// со значениями "Light"/"Dark") в идентификатор полноценной темы
/// (<see cref="AppSettings.FullTheme"/>, Задача 1) и обратной совместимости после
/// добавления четырёх новых тем (Задача 4): новые строковые id
/// (material/macos/windowsxp/glass) сохраняются и восстанавливаются, а имена новых
/// встроенных тем (Material Design/macOS/Windows XP/Glass) защищены от конфликта
/// с пользовательскими цветовыми схемами.
/// </summary>
public sealed class AppSettingsThemeMigrationTests
{
    [Fact]
    public void NormalizeThemeSelection_MigratesDarkTheme()
    {
        var settings = new AppSettings { Theme = "Dark" };

        var id = settings.NormalizeThemeSelection();

        Assert.Equal("dark", id);
        Assert.Equal("dark", settings.FullTheme);
    }

    [Fact]
    public void NormalizeThemeSelection_MigratesLightTheme()
    {
        var settings = new AppSettings { Theme = "Light" };

        var id = settings.NormalizeThemeSelection();

        Assert.Equal("light", id);
        Assert.Equal("light", settings.FullTheme);
    }

    [Fact]
    public void NormalizeThemeSelection_DarkComparisonIsCaseInsensitive()
    {
        var settings = new AppSettings { Theme = "dark" };

        Assert.Equal("dark", settings.NormalizeThemeSelection());
        Assert.Equal("dark", settings.FullTheme);
    }

    [Fact]
    public void NormalizeThemeSelection_EmptyThemeFallsBackToLight()
    {
        // Пустой выбор темы (например, у новых пользователей) трактуется как светлая.
        var settings = new AppSettings { Theme = string.Empty };

        Assert.Equal("light", settings.NormalizeThemeSelection());
        Assert.Equal("light", settings.FullTheme);
    }

    [Fact]
    public void NormalizeThemeSelection_KeepsExistingFullTheme()
    {
        // Полноценная тема уже выбрана — устаревшее поле Theme её не перетирает.
        var settings = new AppSettings { Theme = "Dark", FullTheme = "blue" };

        Assert.Equal("blue", settings.NormalizeThemeSelection());
        Assert.Equal("blue", settings.FullTheme);
    }

    [Fact]
    public void NormalizeThemeSelection_IsIdempotent()
    {
        var settings = new AppSettings { Theme = "Dark" };

        settings.NormalizeThemeSelection();
        settings.NormalizeThemeSelection();

        Assert.Equal("dark", settings.FullTheme);
    }

    [Theory]
    [InlineData("material")]
    [InlineData("macos")]
    [InlineData("windowsxp")]
    [InlineData("glass")]
    public void NormalizeThemeSelection_KeepsNewThemeIds(string id)
    {
        // Новые темы (Задача 3) — обычные строковые id: нормализация не должна их
        // трогать, даже если устаревшее поле Theme заполнено (обратная совместимость).
        var settings = new AppSettings { Theme = "Dark", FullTheme = id };

        Assert.Equal(id, settings.NormalizeThemeSelection());
        Assert.Equal(id, settings.FullTheme);
    }

    [Theory]
    [InlineData("Material Design")]
    [InlineData("macOS")]
    [InlineData("Windows XP")]
    [InlineData("Glass")]
    public void IsBuiltInThemeName_RecognizesNewThemeNames(string name)
    {
        // Имена новых встроенных тем защищены от конфликта с пользовательскими схемами.
        Assert.True(ThemeFeatureMap.IsBuiltInThemeName(name));
    }

    [Theory]
    [InlineData("material design")]
    [InlineData("MACOS")]
    [InlineData("windows xp")]
    [InlineData(" glass ")]
    public void IsBuiltInThemeName_NewThemeNamesAreCaseInsensitiveAndTrimmed(string name)
    {
        Assert.True(ThemeFeatureMap.IsBuiltInThemeName(name));
    }

    [Theory]
    [InlineData("Material Design", "Material Design (пользовательская)")]
    [InlineData("macOS", "macOS (пользовательская)")]
    [InlineData("Windows XP", "Windows XP (пользовательская)")]
    [InlineData("Glass", "Glass (пользовательская)")]
    public void ResolveCustomSchemeName_AppendsSuffixForNewThemeNames(string name, string expected)
    {
        var resolved = ThemeFeatureMap.ResolveCustomSchemeName(name);

        Assert.Equal(expected, resolved);
        // Разрешённое имя больше не конфликтует со встроенной темой.
        Assert.False(ThemeFeatureMap.IsBuiltInThemeName(resolved));
    }

    [Fact]
    public void Resolve_OldBlueIdStillAppliesBlueTheme()
    {
        // Старый settings.json с FullTheme=blue открывается и применяет «Синюю».
        var theme = ThemeFeatureMap.Resolve("blue");

        Assert.Equal("blue", theme.Id);
        Assert.Equal("Синяя", theme.Name);
    }

    [Theory]
    [InlineData("material", "Material Design")]
    [InlineData("macos", "macOS")]
    [InlineData("windowsxp", "Windows XP")]
    [InlineData("glass", "Glass")]
    public void Resolve_NewThemeIdsResolveToNewThemes(string id, string name)
    {
        var theme = ThemeFeatureMap.Resolve(id);

        Assert.Equal(id, theme.Id);
        Assert.Equal(name, theme.Name);
    }
}