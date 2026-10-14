using Configuration_Management.Themes;
using Xunit;

namespace ConfigurationManagement.Tests;

/// <summary>
/// Тесты разрешения конфликта имени пользовательской схемы со встроенной полноценной
/// темой (issue #376): если имя совпадает с именем встроенной темы (Светлая/Тёмная/
/// Синяя/Зелёная), <see cref="ThemeFeatureMap.SaveCustomScheme"/> должен переименовать
/// схему (суффикс <see cref="ThemeFeatureMap.CustomSchemeSuffix"/>), чтобы встроенная
/// тема не затирала пользовательскую и наоборот. Чистая логика без файловой системы,
/// как остальные тесты <see cref="ThemeFeatureMap"/>.
/// </summary>
public sealed class CustomSchemeNameConflictTests
{
    [Theory]
    [InlineData("Светлая")]
    [InlineData("Тёмная")]
    [InlineData("Синяя")]
    [InlineData("Зелёная")]
    public void IsBuiltInThemeName_RecognizesAllBuiltInThemes(string name)
    {
        Assert.True(ThemeFeatureMap.IsBuiltInThemeName(name));
    }

    [Theory]
    [InlineData("ЗЕЛЁНАЯ")]
    [InlineData("зелёная")]
    [InlineData("  Синяя  ")]
    public void IsBuiltInThemeName_IsCaseInsensitiveAndTrims(string name)
    {
        Assert.True(ThemeFeatureMap.IsBuiltInThemeName(name));
    }

    [Theory]
    [InlineData("Моя схема")]
    [InlineData("Избранная палитра")]
    [InlineData("")]
    [InlineData("   ")]
    public void IsBuiltInThemeName_UnknownOrEmpty_ReturnsFalse(string? name)
    {
        Assert.False(ThemeFeatureMap.IsBuiltInThemeName(name));
    }

    [Fact]
    public void IsBuiltInThemeName_Null_ReturnsFalse()
    {
        Assert.False(ThemeFeatureMap.IsBuiltInThemeName(null));
    }

    [Theory]
    [InlineData("Зелёная", "Зелёная (пользовательская)")]
    [InlineData("Светлая", "Светлая (пользовательская)")]
    [InlineData("тёмная", "тёмная (пользовательская)")]
    [InlineData(" Синяя ", "Синяя (пользовательская)")]
    public void ResolveCustomSchemeName_AppendsSuffixOnConflict(string name, string expected)
    {
        var resolved = ThemeFeatureMap.ResolveCustomSchemeName(name);

        Assert.Equal(expected, resolved);
        // Разрешённое имя больше не конфликтует со встроенной темой.
        Assert.False(ThemeFeatureMap.IsBuiltInThemeName(resolved));
    }

    [Theory]
    [InlineData("Моя схема", "Моя схема")]
    [InlineData("  Избранная палитра  ", "Избранная палитра")]
    [InlineData("", "")]
    public void ResolveCustomSchemeName_LeavesNonConflictingName(string name, string expected)
    {
        Assert.Equal(expected, ThemeFeatureMap.ResolveCustomSchemeName(name));
    }

    [Fact]
    public void ResolveCustomSchemeName_Null_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, ThemeFeatureMap.ResolveCustomSchemeName(null));
    }

    [Fact]
    public void ResolveCustomSchemeName_IsIdempotent()
    {
        // Повторное применение не добавляет второй суффикс: «Зелёная (пользовательская)»
        // со встроенными темами не конфликтует.
        var once = ThemeFeatureMap.ResolveCustomSchemeName("Зелёная");
        var twice = ThemeFeatureMap.ResolveCustomSchemeName(once);

        Assert.Equal(once, twice);
    }
}