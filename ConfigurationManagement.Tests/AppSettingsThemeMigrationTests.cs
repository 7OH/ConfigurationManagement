using Configuration_Management.Models;
using Xunit;

namespace ConfigurationManagement.Tests;

/// <summary>
/// Тесты миграции старого выбора темы (устаревшее поле <see cref="AppSettings.Theme"/>
/// со значениями "Light"/"Dark") в идентификатор полноценной темы
/// (<see cref="AppSettings.FullTheme"/>, Задача 1).
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
}