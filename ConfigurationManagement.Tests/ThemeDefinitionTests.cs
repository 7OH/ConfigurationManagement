using System.Linq;
using Configuration_Management.Models;
using Configuration_Management.Themes;
using Xunit;

namespace ConfigurationManagement.Tests;

/// <summary>
/// Тесты полноценных тем (Задача 1): встроенные определения из
/// <see cref="ThemeCatalog"/> — идентификаторы, имена, базовый вариант,
/// целостность цветовых схем и акцентные цвета.
/// </summary>
public sealed class ThemeDefinitionTests
{
    [Fact]
    public void BuiltIn_HasExpectedIdsNamesAndBaseVariant()
    {
        var themes = ThemeCatalog.BuiltIn;

        Assert.Equal(4, themes.Count);

        // Светлая.
        Assert.Equal("light", themes[0].Id);
        Assert.Equal("Светлая", themes[0].Name);
        Assert.False(themes[0].IsDark);

        // Тёмная.
        Assert.Equal("dark", themes[1].Id);
        Assert.Equal("Тёмная", themes[1].Name);
        Assert.True(themes[1].IsDark);

        // Синяя.
        Assert.Equal("blue", themes[2].Id);
        Assert.Equal("Синяя", themes[2].Name);
        Assert.False(themes[2].IsDark);

        // Зелёная.
        Assert.Equal("green", themes[3].Id);
        Assert.Equal("Зелёная", themes[3].Name);
        Assert.False(themes[3].IsDark);
    }

    [Fact]
    public void BuiltIn_HasUniqueIds()
    {
        var ids = ThemeCatalog.BuiltIn.Select(t => t.Id).ToList();

        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    [Fact]
    public void BuiltIn_ThemesAreNormalizableAndComplete()
    {
        foreach (var theme in ThemeCatalog.BuiltIn)
        {
            // Нормализация схемы не должна падать и сохраняет обе палитры полными.
            theme.Scheme.Normalize();
            Assert.NotEmpty(theme.Scheme.LightColors);
            Assert.NotEmpty(theme.Scheme.DarkColors);

            // Все редактируемые ключи присутствуют в обеих палитрах.
            foreach (var (key, _) in ColorScheme.Definitions)
            {
                Assert.Contains(key, theme.Scheme.LightColors);
                Assert.Contains(key, theme.Scheme.DarkColors);
            }
        }
    }

    [Fact]
    public void BuiltIn_AccentColorsAreValidHex()
    {
        foreach (var theme in ThemeCatalog.BuiltIn)
        {
            Assert.False(string.IsNullOrWhiteSpace(theme.AccentColor));
            Assert.Matches("^#[0-9A-Fa-f]{6}$", theme.AccentColor);
        }
    }

    [Fact]
    public void BuiltIn_DifferInWindowButtonStyleAndIconSet()
    {
        // Полноценные темы отличаются не только палитрой: при выборе темы меняется
        // стиль кнопок управления окном и набор иконок, а не только цвет (issue #376).
        var themes = ThemeCatalog.BuiltIn;

        Assert.Equal(WindowButtonStyle.Fluent, themes[0].ButtonStyle); // Светлая
        Assert.Equal(IconSet.Fluent, themes[0].Icons);
        Assert.Equal(WindowButtonStyle.Fluent, themes[1].ButtonStyle); // Тёмная
        Assert.Equal(IconSet.Fluent, themes[1].Icons);
        Assert.Equal(WindowButtonStyle.Classic, themes[2].ButtonStyle); // Синяя
        Assert.Equal(IconSet.Outline, themes[2].Icons);
        Assert.Equal(WindowButtonStyle.Minimal, themes[3].ButtonStyle); // Зелёная
        Assert.Equal(IconSet.Outline, themes[3].Icons);

        // В каталоге есть и Fluent, и другие стили/наборы — выбор темы даёт видимый эффект.
        Assert.True(themes.Select(t => t.ButtonStyle).Distinct().Count() > 1);
        Assert.True(themes.Select(t => t.Icons).Distinct().Count() > 1);
    }
}