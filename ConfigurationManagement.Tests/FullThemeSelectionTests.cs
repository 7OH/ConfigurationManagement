using System.Linq;
using Configuration_Management.Themes;
using Configuration_Management.ViewModels;
using Xunit;

namespace ConfigurationManagement.Tests;

/// <summary>
/// Тесты выбора полноценной темы в настройках (Задача 3): ключ локализации имени
/// темы (общий для обеих платформ) и список встроенных тем, доступный вкладке
/// «Темы». Тесты Задач 1–2 (каталог, определение, миграция) не дублируются.
/// </summary>
public sealed class FullThemeSelectionTests
{
    [Theory]
    [InlineData("light", "Theme.Light")]
    [InlineData("dark", "Theme.Dark")]
    [InlineData("blue", "Theme.Blue")]
    [InlineData("green", "Theme.Green")]
    [InlineData("material", "Theme.Material")]
    [InlineData("macos", "Theme.MacOS")]
    [InlineData("windowsxp", "Theme.WindowsXp")]
    [InlineData("glass", "Theme.Glass")]
    public void ThemeNameKey_MapsBuiltInIds(string id, string expectedKey)
    {
        Assert.Equal(expectedKey, ThemeFeatureMap.ThemeNameKey(id));
    }

    [Theory]
    [InlineData("LIGHT", "Theme.Light")]
    [InlineData("Blue", "Theme.Blue")]
    [InlineData("GREEN", "Theme.Green")]
    [InlineData("MATERIAL", "Theme.Material")]
    [InlineData("MacOS", "Theme.MacOS")]
    [InlineData("WindowsXP", "Theme.WindowsXp")]
    [InlineData("GLASS", "Theme.Glass")]
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

    [Fact]
    public void BuiltInCatalog_HasEightThemesInStableOrder()
    {
        // Задача 3: в каталоге 8 встроенных тем; порядок первых четырёх не меняется.
        var ids = ThemeCatalog.BuiltIn.Select(t => t.Id).ToList();

        Assert.Equal(8, ThemeCatalog.BuiltIn.Count);
        Assert.Equal(new[] { "light", "dark", "blue", "green" }, ids.Take(4));
        Assert.Equal(new[] { "material", "macos", "windowsxp", "glass" }, ids.Skip(4));
    }

    [Fact]
    public void ThemePreviewState_UnknownIdResolvesToLightOnApply()
    {
        // Предпросмотр запоминает переданный id, а резолв неизвестного id при применении
        // даёт светлую тему по умолчанию (issue #378) — как ThemeFeatureMap.Resolve.
        var state = new ThemePreviewState();
        state.Begin("light", "light");

        Assert.Equal("unknown", state.Preview("unknown"));
        Assert.Equal("light", ThemeFeatureMap.Resolve("unknown").Id);
    }

    [Fact]
    public void NewThemes_HaveOwnPaletteButtonStyleAndIconSet()
    {
        // Каждая новая тема несёт собственную палитру, стиль кнопок окна и набор иконок.
        var material = ThemeCatalog.Find("material")!;
        var macos = ThemeCatalog.Find("macos")!;
        var windowsXp = ThemeCatalog.Find("windowsxp")!;
        var glass = ThemeCatalog.Find("glass")!;

        Assert.Equal(WindowButtonStyle.Material, material.ButtonStyle);
        Assert.Equal(IconSet.Material, material.Icons);
        Assert.Equal("#7C4DFF", material.AccentColor);

        Assert.Equal(WindowButtonStyle.MacOS, macos.ButtonStyle);
        Assert.Equal(IconSet.MacOS, macos.Icons);
        Assert.Equal("#0A84FF", macos.AccentColor);

        Assert.Equal(WindowButtonStyle.WindowsXp, windowsXp.ButtonStyle);
        Assert.Equal(IconSet.WindowsXp, windowsXp.Icons);
        Assert.Equal("#3B6EA5", windowsXp.AccentColor);

        Assert.Equal(WindowButtonStyle.Glass, glass.ButtonStyle);
        Assert.Equal(IconSet.Glass, glass.Icons);
        Assert.Equal("#007AFF", glass.AccentColor);

        foreach (var theme in new[] { material, macos, windowsXp, glass })
        {
            Assert.NotNull(theme.Scheme);
            Assert.NotEmpty(theme.Scheme.LightColors);
            Assert.NotEmpty(theme.Scheme.DarkColors);
        }
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
    [InlineData("Material Design", true)]
    [InlineData("macOS", true)]
    [InlineData("Windows XP", true)]
    [InlineData("Glass", true)]
    [InlineData("Моя схема", false)]
    [InlineData("", false)]
    public void IsBuiltInFullThemeName_RecognizesBuiltInThemes(string name, bool expected)
    {
        Assert.Equal(expected, SettingsViewModel.IsBuiltInFullThemeName(name));
    }
}
#endif
