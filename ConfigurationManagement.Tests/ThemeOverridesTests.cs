using System.IO;
using System.Linq;
using System.Text.Json;
using Configuration_Management.Models;
using Configuration_Management.Themes;
using Xunit;

namespace ConfigurationManagement.Tests;

/// <summary>
/// Блок «Темы» (0.3.12.62): переопределение набора иконок и стиля кнопок окна независимо
/// от темы, цвета карточек галереи и совместимость настроек.
/// </summary>
public sealed class ThemeOverridesTests
{
    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("unknown", null)]
    [InlineData("Material", IconSet.Material)]
    [InlineData("macos", IconSet.MacOS)]
    [InlineData(" Glass ", IconSet.Glass)]
    [InlineData("Fluent", IconSet.Fluent)]
    public void ParseIconSetOverride(string? value, IconSet? expected)
        => Assert.Equal(expected, ThemeFeatureMap.ParseIconSetOverride(value));

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("bogus", null)]
    [InlineData("MacOS", WindowButtonStyle.MacOS)]
    [InlineData("windowsxp", WindowButtonStyle.WindowsXp)]
    [InlineData("Classic", WindowButtonStyle.Classic)]
    public void ParseButtonStyleOverride(string? value, WindowButtonStyle? expected)
        => Assert.Equal(expected, ThemeFeatureMap.ParseButtonStyleOverride(value));

    [Fact]
    public void Effective_UsesThemeWithoutOverride_AndOverrideWhenSet()
    {
        var macos = ThemeCatalog.Find("macos")!;
        Assert.Equal(IconSet.MacOS, ThemeFeatureMap.EffectiveIconSet(macos, null));
        Assert.Equal(WindowButtonStyle.MacOS, ThemeFeatureMap.EffectiveButtonStyle(macos, null));
        Assert.Equal(IconSet.Glass, ThemeFeatureMap.EffectiveIconSet(macos, IconSet.Glass));
        Assert.Equal(WindowButtonStyle.Classic, ThemeFeatureMap.EffectiveButtonStyle(macos, WindowButtonStyle.Classic));
    }

    [Fact]
    public void Ids_RoundTrip()
    {
        foreach (var set in ThemeFeatureMap.SelectableIconSets)
            Assert.Equal(set, ThemeFeatureMap.ParseIconSetOverride(ThemeFeatureMap.IconSetId(set)));
        foreach (var style in ThemeFeatureMap.SelectableButtonStyles)
            Assert.Equal(style, ThemeFeatureMap.ParseButtonStyleOverride(ThemeFeatureMap.ButtonStyleId(style)));
    }

    [Fact]
    public void PreviewColors_AreDefinedForEveryBuiltInTheme()
    {
        foreach (var theme in ThemeCatalog.BuiltIn)
        {
            var c = ThemeFeatureMap.PreviewColors(theme);
            foreach (var hex in new[] { c.Background, c.Card, c.Text, c.SecondaryText, c.Border, c.Accent })
                Assert.StartsWith("#", hex);
        }
    }

    [Fact]
    public void LocalizationKeys_ExistForOverridesInBothLanguages()
    {
        var root = FindRepoRoot();
        foreach (var lang in new[] { "ru", "en" })
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "Configuration Management",
                "Localization", "Languages", lang + ".json")));
            var json = doc.RootElement.GetRawText();
            var keys = ThemeFeatureMap.SelectableIconSets.Select(ThemeFeatureMap.IconSetDisplayKey)
                .Concat(ThemeFeatureMap.SelectableButtonStyles.Select(ThemeFeatureMap.ButtonStyleNameKey))
                .Concat(new[]
                {
                    "Settings.ThemeCustomize", "Settings.ThemeCustomize.Description",
                    "Settings.IconSetOverride", "Settings.WindowButtonStyleOverride", "Settings.ByTheme"
                });
            foreach (var key in keys)
                Assert.Contains("\"" + key + "\"", json);
        }
    }

    [Fact]
    public void AppSettings_OldFileWithoutOverrides_ReadsAsByTheme()
    {
        var settings = JsonSerializer.Deserialize<AppSettings>("{\"FullTheme\":\"macos\"}")!;
        Assert.Equal(string.Empty, settings.IconSetOverride);
        Assert.Equal(string.Empty, settings.WindowButtonStyleOverride);

        settings.IconSetOverride = "Glass";
        settings.WindowButtonStyleOverride = "Classic";
        var copy = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings))!;
        Assert.Equal("Glass", copy.IconSetOverride);
        Assert.Equal("Classic", copy.WindowButtonStyleOverride);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(System.AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "Configuration Management", "Localization")) &&
                Directory.Exists(Path.Combine(dir.FullName, "ConfigurationManagement.Tests")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("Не найден корень репозитория");
    }
}
