using System.Linq;
using Configuration_Management.Themes;
using Configuration_Management.ViewModels;
using Xunit;

namespace ConfigurationManagement.Tests;

/// <summary>
/// Тесты выбора полноценной темы в настройках (Задача 3): ключ локализации имени
/// темы (общий для обеих платформ) и список встроенных тем, доступный вкладке
/// «Цветовое оформление». Тесты Задач 1–2 (каталог, определение, миграция) не дублируются.
/// </summary>
public sealed class FullThemeSelectionTests
{
    [Theory]
    [InlineData("light", "Theme.Light")]
    [InlineData("dark", "Theme.Dark")]
    [InlineData("blue", "Theme.Blue")]
    [InlineData("green", "Theme.Green")]
    public void ThemeNameKey_MapsBuiltInIds(string id, string expectedKey)
    {
        Assert.Equal(expectedKey, ThemeFeatureMap.ThemeNameKey(id));
    }

    [Theory]
    [InlineData("LIGHT", "Theme.Light")]
    [InlineData("Blue", "Theme.Blue")]
    [InlineData("GREEN", "Theme.Green")]
    public void ThemeNameKey_IsCaseInsensitive(string id, string expectedKey)
    {
        Assert.Equal(expectedKey, ThemeFeatureMap.ThemeNameKey(id));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("unknown")]
    public void ThemeNameKey_UnknownOrEmpty_FallsBackToLight(string? id)
    {
        // Неизвестный идентификатор не ошибка: как и Resolve, даём ключ светлой темы.
        Assert.Equal("Theme.Light", ThemeFeatureMap.ThemeNameKey(id));
    }

    [Fact]
    public void ThemeNameKey_CoversEveryBuiltInThemeWithDistinctKey()
    {
        // Каждая встроенная тема имеет собственный ключ локализации имени.
        var keys = ThemeCatalog.BuiltIn.Select(t => ThemeFeatureMap.ThemeNameKey(t.Id)).ToList();

        Assert.Equal(ThemeCatalog.BuiltIn.Count, keys.Distinct().Count());
    }
}

#if !LINUX
/// <summary>
/// Тесты логики выбора полноценной темы в <see cref="SettingsViewModel"/> (Задача 3):
/// список встроенных тем и распознавание имён встроенных тем. Класс обёрнут в
/// #if !LINUX: SettingsViewModel собирается только в Windows-конфигурации.
/// </summary>
public sealed class SettingsFullThemeTests
{
    [Fact]
    public void FullThemes_IsWiredToBuiltInCatalog()
    {
        // Список тем в настройках — тот же встроенный каталог (без копий и фильтрации).
        Assert.Same(ThemeCatalog.BuiltIn, SettingsViewModel.FullThemes);
    }

    [Theory]
    [InlineData("Светлая", true)]
    [InlineData("Тёмная", true)]
    [InlineData("Синяя", true)]
    [InlineData("Зелёная", true)]
    [InlineData("Моя схема", false)]
    [InlineData("", false)]
    public void IsBuiltInFullThemeName_RecognizesBuiltInThemes(string name, bool expected)
    {
        Assert.Equal(expected, SettingsViewModel.IsBuiltInFullThemeName(name));
    }
}
#endif
