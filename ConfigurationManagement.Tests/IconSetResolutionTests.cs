using System.Linq;
using Configuration_Management.Themes;
using Xunit;

namespace ConfigurationManagement.Tests;

/// <summary>
/// Тесты резолва иконок по набору (Задача 5): выбор ключа геометрии из активного
/// набора (<see cref="ThemeFeatureMap.IconKeyForSet"/>) и порядок fallback-кандидатов
/// (<see cref="ThemeFeatureMap.IconResolutionCandidates"/>) — контурный ключ, затем
/// Fluent-ключ, затем запасная «папка». Чистая логика без Application.Current,
/// как в <see cref="ThemeFeatureMapTests"/>.
/// </summary>
public sealed class IconSetResolutionTests
{
    [Theory]
    [InlineData(IconSet.Fluent, "IconAdd", "IconAdd")]
    [InlineData(IconSet.Outline, "IconAdd", "IconAdd.Outline")]
    [InlineData(IconSet.Fluent, "IconFolder", "IconFolder")]
    [InlineData(IconSet.Outline, "IconFolder", "IconFolder.Outline")]
    public void IconKeyForSet_SelectsKeyFromActiveSet(IconSet set, string key, string expected)
    {
        // «Выбор ключа из активного набора»: для Outline — ключ с суффиксом ".Outline"
        // (словарь IconsOutline.xaml), для Fluent — исходный ключ (Icons.xaml).
        Assert.Equal(expected, ThemeFeatureMap.IconKeyForSet(key, set));
    }

    [Fact]
    public void IconKeyForSet_OutlineSuffixIsStable()
    {
        // Суффикс зафиксирован: его используют словари IconsOutline.xaml/.axaml.
        Assert.Equal("IconDelete.Outline", ThemeFeatureMap.IconKeyForSet("IconDelete", IconSet.Outline));
        Assert.Equal("IconSettings.Outline", ThemeFeatureMap.IconKeyForSet("IconSettings", IconSet.Outline));
    }

    [Fact]
    public void IconResolutionCandidates_Fluent_KeyThenFolder()
    {
        // Fluent: сначала исходный ключ, затем запасная «папка» (без дублей).
        var candidates = ThemeFeatureMap.IconResolutionCandidates("IconAdd", IconSet.Fluent);

        Assert.Equal(new[] { "IconAdd", "IconFolder" }, candidates);
    }

    [Fact]
    public void IconResolutionCandidates_Outline_OutlineKeyThenFluentThenFolder()
    {
        // Outline: контурный ключ → fallback на Fluent (исходный ключ) → «папка».
        var candidates = ThemeFeatureMap.IconResolutionCandidates("IconAdd", IconSet.Outline);

        Assert.Equal(new[] { "IconAdd.Outline", "IconAdd", "IconFolder" }, candidates);
    }

    [Fact]
    public void IconResolutionCandidates_Outline_NoDuplicateWhenKeyAlreadySuffixed()
    {
        // Если ключ уже содержит суффикс набора, fallback-кандидат не дублируется.
        var candidates = ThemeFeatureMap.IconResolutionCandidates("IconAdd.Outline", IconSet.Outline);

        Assert.Equal(new[] { "IconAdd.Outline.Outline", "IconAdd.Outline", "IconFolder" }, candidates);
    }

    [Fact]
    public void IconResolutionCandidates_AlwaysEndsWithFolderFallback()
    {
        // Последний кандидат — запасная «папка»: резолв никогда не остаётся без геометрии.
        foreach (var set in new[] { IconSet.Fluent, IconSet.Outline })
        {
            var candidates = ThemeFeatureMap.IconResolutionCandidates("IconSearch", set);
            Assert.Equal("IconFolder", candidates[^1]);
        }
    }

    [Theory]
    [InlineData("Outline", IconSet.Outline)]
    [InlineData("outline", IconSet.Outline)]
    [InlineData("OUTLINE", IconSet.Outline)]
    [InlineData("Fluent", IconSet.Fluent)]
    [InlineData("fluent", IconSet.Fluent)]
    public void ParseIconSet_RecognizesSetNames(string? name, IconSet expected)
    {
        // Имя набора из ресурса IconSetName (выставляет ThemeManager.ApplyIconSet).
        Assert.Equal(expected, ThemeFeatureMap.ParseIconSet(name));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("unknown")]
    [InlineData("Outline2")]
    public void ParseIconSet_UnknownOrEmpty_FallsBackToFluent(string? name)
    {
        // Неизвестное имя не ошибка: набор по умолчанию — Fluent.
        Assert.Equal(IconSet.Fluent, ThemeFeatureMap.ParseIconSet(name));
    }

    [Fact]
    public void IconSetId_ParseIconSet_RoundTrip()
    {
        // Значение, которое кладёт ApplyIconSet в ресурс IconSetName, разбирается
        // обратно в тот же набор — резолв и применение согласованы.
        foreach (var set in new[] { IconSet.Fluent, IconSet.Outline })
        {
            Assert.Equal(set, ThemeFeatureMap.ParseIconSet(ThemeFeatureMap.IconSetId(set)));
        }
    }

    [Fact]
    public void OutlineCoversKeyIcons_EveryOutlineKeyHasFluentBase()
    {
        // Ключевые иконки приложения имеют контурный вариант: для каждого из них
        // суффиксный ключ отличается от Fluent-ключа (то есть набор реально меняет
        // геометрию, а не дублирует её).
        var keyIcons = new[]
        {
            "IconAdd", "IconDelete", "IconEdit", "IconSave", "IconPlay", "IconStop",
            "IconSettings", "IconFolder", "IconDatabase", "IconOk", "IconWarning",
            "IconError", "IconStopped", "IconRunning", "IconInfo", "IconUnknown",
            "IconStar", "IconPin", "IconTag", "IconFilter", "IconConfiguration",
            "IconScheduler", "IconLogs", "IconRights", "IconUsers", "IconSearch",
            "IconRefresh", "IconSync", "IconHistory", "IconSun", "IconMoon", "IconTheme"
        };

        foreach (var key in keyIcons)
        {
            var outlineKey = ThemeFeatureMap.IconKeyForSet(key, IconSet.Outline);
            Assert.NotEqual(key, outlineKey);
            Assert.EndsWith(".Outline", outlineKey, System.StringComparison.Ordinal);
        }
    }
}
