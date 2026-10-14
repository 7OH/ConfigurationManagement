using System.Linq;
using Configuration_Management.Themes;
using Xunit;

namespace ConfigurationManagement.Tests;

/// <summary>
/// Тесты резолва иконок по набору (Задача 2): выбор ключа геометрии из активного
/// набора (<see cref="ThemeFeatureMap.IconKeyForSet"/>) и порядок fallback-кандидатов
/// (<see cref="ThemeFeatureMap.IconResolutionCandidates"/>) — суффиксный ключ набора,
/// затем Fluent-ключ (fallback, когда варианта набора нет), затем запасная «папка».
/// Чистая логика без Application.Current, как в <see cref="ThemeFeatureMapTests"/>.
/// </summary>
public sealed class IconSetResolutionTests
{
    /// <summary>Все наборы иконок в стабильном порядке.</summary>
    private static readonly IconSet[] AllSets =
    {
        IconSet.Fluent, IconSet.Outline, IconSet.Material,
        IconSet.MacOS, IconSet.WindowsXp, IconSet.Glass
    };

    /// <summary>Наборы с суффиксными словарями (все, кроме Fluent).</summary>
    private static readonly IconSet[] SuffixedSets =
    {
        IconSet.Outline, IconSet.Material, IconSet.MacOS, IconSet.WindowsXp, IconSet.Glass
    };

    [Theory]
    [InlineData(IconSet.Fluent, "IconAdd", "IconAdd")]
    [InlineData(IconSet.Outline, "IconAdd", "IconAdd.Outline")]
    [InlineData(IconSet.Material, "IconAdd", "IconAdd.Material")]
    [InlineData(IconSet.MacOS, "IconAdd", "IconAdd.MacOS")]
    [InlineData(IconSet.WindowsXp, "IconAdd", "IconAdd.WindowsXp")]
    [InlineData(IconSet.Glass, "IconAdd", "IconAdd.Glass")]
    [InlineData(IconSet.Fluent, "IconFolder", "IconFolder")]
    [InlineData(IconSet.Outline, "IconFolder", "IconFolder.Outline")]
    [InlineData(IconSet.Material, "IconFolder", "IconFolder.Material")]
    [InlineData(IconSet.MacOS, "IconFolder", "IconFolder.MacOS")]
    [InlineData(IconSet.WindowsXp, "IconFolder", "IconFolder.WindowsXp")]
    [InlineData(IconSet.Glass, "IconFolder", "IconFolder.Glass")]
    public void IconKeyForSet_SelectsKeyFromActiveSet(IconSet set, string key, string expected)
    {
        // «Выбор ключа из активного набора»: для не-Fluent наборов — ключ с суффиксом
        // ".Имя" (словари IconsOutline/Material/MacOS/WindowsXp/Glass.xaml), для
        // Fluent — исходный ключ (Icons.xaml).
        Assert.Equal(expected, ThemeFeatureMap.IconKeyForSet(key, set));
    }

    [Fact]
    public void IconKeyForSet_SuffixIsStable()
    {
        // Суффиксы зафиксированы: их используют словари *.xaml/.axaml наборов.
        Assert.Equal("IconDelete.Outline", ThemeFeatureMap.IconKeyForSet("IconDelete", IconSet.Outline));
        Assert.Equal("IconDelete.Material", ThemeFeatureMap.IconKeyForSet("IconDelete", IconSet.Material));
        Assert.Equal("IconDelete.MacOS", ThemeFeatureMap.IconKeyForSet("IconDelete", IconSet.MacOS));
        Assert.Equal("IconDelete.WindowsXp", ThemeFeatureMap.IconKeyForSet("IconDelete", IconSet.WindowsXp));
        Assert.Equal("IconDelete.Glass", ThemeFeatureMap.IconKeyForSet("IconDelete", IconSet.Glass));
        Assert.Equal("IconSettings.Material", ThemeFeatureMap.IconKeyForSet("IconSettings", IconSet.Material));
        Assert.Equal("IconSettings.Glass", ThemeFeatureMap.IconKeyForSet("IconSettings", IconSet.Glass));
    }

    [Fact]
    public void IconResolutionCandidates_Fluent_KeyThenFolder()
    {
        // Fluent: сначала исходный ключ, затем запасная «папка» (без дублей).
        var candidates = ThemeFeatureMap.IconResolutionCandidates("IconAdd", IconSet.Fluent);

        Assert.Equal(new[] { "IconAdd", "IconFolder" }, candidates);
    }

    [Theory]
    [InlineData(IconSet.Outline, "IconAdd.Outline")]
    [InlineData(IconSet.Material, "IconAdd.Material")]
    [InlineData(IconSet.MacOS, "IconAdd.MacOS")]
    [InlineData(IconSet.WindowsXp, "IconAdd.WindowsXp")]
    [InlineData(IconSet.Glass, "IconAdd.Glass")]
    public void IconResolutionCandidates_NonFluent_SuffixedKeyThenFluentThenFolder(
        IconSet set, string suffixedKey)
    {
        // Не-Fluent: суффиксный ключ → fallback на Fluent (исходный ключ) → «папка».
        var candidates = ThemeFeatureMap.IconResolutionCandidates("IconAdd", set);

        Assert.Equal(new[] { suffixedKey, "IconAdd", "IconFolder" }, candidates);
    }

    [Theory]
    [InlineData(IconSet.Outline, "IconAdd.Outline.Outline")]
    [InlineData(IconSet.Material, "IconAdd.Material.Material")]
    [InlineData(IconSet.MacOS, "IconAdd.MacOS.MacOS")]
    [InlineData(IconSet.WindowsXp, "IconAdd.WindowsXp.WindowsXp")]
    [InlineData(IconSet.Glass, "IconAdd.Glass.Glass")]
    public void IconResolutionCandidates_NoDuplicateWhenKeyAlreadySuffixed(
        IconSet set, string expectedFirst)
    {
        // Если ключ уже содержит суффикс набора, fallback-кандидат не дублируется.
        var suffixed = ThemeFeatureMap.IconKeyForSet("IconAdd", set);
        var candidates = ThemeFeatureMap.IconResolutionCandidates(suffixed, set);

        Assert.Equal(new[] { expectedFirst, suffixed, "IconFolder" }, candidates);
    }

    [Fact]
    public void IconResolutionCandidates_AlwaysEndsWithFolderFallback()
    {
        // Последний кандидат — запасная «папка»: резолв никогда не остаётся без геометрии.
        foreach (var set in AllSets)
        {
            var candidates = ThemeFeatureMap.IconResolutionCandidates("IconSearch", set);
            Assert.Equal("IconFolder", candidates[^1]);
        }
    }

    [Theory]
    [InlineData("Outline", IconSet.Outline)]
    [InlineData("outline", IconSet.Outline)]
    [InlineData("OUTLINE", IconSet.Outline)]
    [InlineData("Material", IconSet.Material)]
    [InlineData("material", IconSet.Material)]
    [InlineData("MacOS", IconSet.MacOS)]
    [InlineData("macos", IconSet.MacOS)]
    [InlineData("WindowsXp", IconSet.WindowsXp)]
    [InlineData("windowsxp", IconSet.WindowsXp)]
    [InlineData("Glass", IconSet.Glass)]
    [InlineData("glass", IconSet.Glass)]
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
    [InlineData("MaterialPro")]
    public void ParseIconSet_UnknownOrEmpty_FallsBackToFluent(string? name)
    {
        // Неизвестное имя не ошибка: набор по умолчанию — Fluent.
        Assert.Equal(IconSet.Fluent, ThemeFeatureMap.ParseIconSet(name));
    }

    [Fact]
    public void IconSetId_IsExactSuffixName()
    {
        // Имя набора — это суффикс в ключах словаря (IconAdd.Material и т.п.).
        Assert.Equal("Fluent", ThemeFeatureMap.IconSetId(IconSet.Fluent));
        Assert.Equal("Outline", ThemeFeatureMap.IconSetId(IconSet.Outline));
        Assert.Equal("Material", ThemeFeatureMap.IconSetId(IconSet.Material));
        Assert.Equal("MacOS", ThemeFeatureMap.IconSetId(IconSet.MacOS));
        Assert.Equal("WindowsXp", ThemeFeatureMap.IconSetId(IconSet.WindowsXp));
        Assert.Equal("Glass", ThemeFeatureMap.IconSetId(IconSet.Glass));
    }

    [Fact]
    public void IconSetId_ParseIconSet_RoundTrip()
    {
        // Значение, которое кладёт ApplyIconSet в ресурс IconSetName, разбирается
        // обратно в тот же набор — резолв и применение согласованы.
        foreach (var set in AllSets)
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
        foreach (var key in KeyIcons)
        {
            var outlineKey = ThemeFeatureMap.IconKeyForSet(key, IconSet.Outline);
            Assert.NotEqual(key, outlineKey);
            Assert.EndsWith(".Outline", outlineKey, System.StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData(IconSet.Material, ".Material")]
    [InlineData(IconSet.MacOS, ".MacOS")]
    [InlineData(IconSet.WindowsXp, ".WindowsXp")]
    [InlineData(IconSet.Glass, ".Glass")]
    public void NewSetCoversKeyIcons_EveryKeyHasSuffixedVariant(IconSet set, string suffix)
    {
        // Каждый новый набор покрывает ключевые иконки: суффиксный ключ всегда
        // отличается от Fluent-ключа (словарь набора реально подключён), а отсутствующие
        // иконки фолбэчатся на Fluent через IconResolutionCandidates (проверено выше).
        foreach (var key in KeyIcons)
        {
            var suffixedKey = ThemeFeatureMap.IconKeyForSet(key, set);
            Assert.NotEqual(key, suffixedKey);
            Assert.EndsWith(suffix, suffixedKey, System.StringComparison.Ordinal);
        }
    }

    /// <summary>Ключевые иконки приложения из Icons.xaml (проверяются по наборам).</summary>
    private static readonly string[] KeyIcons =
    {
        "IconAdd", "IconDelete", "IconEdit", "IconSave", "IconPlay", "IconStop",
        "IconSettings", "IconFolder", "IconDatabase", "IconOk", "IconWarning",
        "IconError", "IconStopped", "IconRunning", "IconInfo", "IconUnknown",
        "IconStar", "IconPin", "IconTag", "IconFilter", "IconConfiguration",
        "IconScheduler", "IconLogs", "IconRights", "IconUsers", "IconSearch",
        "IconRefresh", "IconSync", "IconHistory", "IconSun", "IconMoon", "IconTheme"
    };
}
