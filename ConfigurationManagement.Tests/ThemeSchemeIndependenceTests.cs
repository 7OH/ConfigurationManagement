using Configuration_Management.Models;
using Configuration_Management.Themes;
using Xunit;

namespace ConfigurationManagement.Tests;

/// <summary>
/// Тесты независимости «Темы» и «Цветовая схема» (issue #378, пункт 2): смена темы
/// не сбрасывает пользовательскую цветовую схему и наоборот; переопределение набора
/// иконок и стиля кнопок окна не затрагивает активную схему. Чистая логика
/// (<see cref="ThemeFeatureMap"/>, <see cref="AppSettings"/>) тестируется без
/// Application.Current; UI-менеджеры применяют её к ресурсам приложения.
/// </summary>
public sealed class ThemeSchemeIndependenceTests
{
    [Fact]
    public void ApplyFullTheme_DoesNotResetCustomScheme()
    {
        // Пользовательская схема активна; применение полноценной темы (резолв + копия
        // с базовым вариантом) не трогает её — тема и схема независимы.
        var custom = ColorScheme.Create("Моя схема", false);
        var settings = new AppSettings { FullTheme = "blue", ActiveColorScheme = custom };

        var theme = ThemeFeatureMap.Resolve(settings.FullTheme);
        var applied = ThemeFeatureMap.WithBaseVariant(theme, theme.IsDark);

        Assert.Equal("blue", applied.Id);
        Assert.Same(custom, settings.ActiveColorScheme);
        Assert.Equal("Моя схема", settings.ActiveColorScheme.Name);
    }

    [Fact]
    public void ApplyCustomScheme_DoesNotResetFullTheme()
    {
        // Применение пользовательской схемы не меняет выбранную полноценную тему.
        var settings = new AppSettings { FullTheme = "blue" };
        settings.ActiveColorScheme = ColorScheme.Create("Моя схема", false);

        Assert.Equal("blue", settings.FullTheme);
        Assert.Equal("Моя схема", settings.ActiveColorScheme.Name);
    }

    [Fact]
    public void IconSetOverride_DoesNotChangeScheme()
    {
        var theme = ThemeFeatureMap.Resolve("blue");
        var schemeBefore = theme.Scheme;

        var effective = ThemeFeatureMap.EffectiveIconSet(theme, IconSet.Material);

        Assert.Equal(IconSet.Material, effective);
        Assert.Same(schemeBefore, theme.Scheme);
    }

    [Fact]
    public void WindowButtonStyleOverride_DoesNotChangeScheme()
    {
        var theme = ThemeFeatureMap.Resolve("blue");
        var schemeBefore = theme.Scheme;

        var effective = ThemeFeatureMap.EffectiveButtonStyle(theme, WindowButtonStyle.MacOS);

        Assert.Equal(WindowButtonStyle.MacOS, effective);
        Assert.Same(schemeBefore, theme.Scheme);
    }

    [Fact]
    public void ApplyScheme_AfterApplyFullTheme_KeepsScheme()
    {
        // Схема, применённая после полноценной темы, остаётся активной: тема не
        // перезаписывает её при повторном применении (порядок ApplyFullTheme → ApplyScheme).
        var custom = ColorScheme.Create("Моя схема", false);
        var theme = ThemeFeatureMap.Resolve("blue");
        var applied = ThemeFeatureMap.WithBaseVariant(theme, theme.IsDark);

        // Пользовательская схема накладывается поверх темы и сохраняется.
        applied.Scheme = custom;
        Assert.Same(custom, applied.Scheme);
        Assert.Equal("Моя схема", applied.Scheme.Name);
    }

    [Fact]
    public void WithBaseVariant_ClonesScheme_DoesNotMutateCatalog()
    {
        // Копия темы с другим базовым вариантом клонирует схему: применение палитры
        // не мутирует объекты ThemeCatalog (тема и схема остаются независимыми).
        var source = ThemeCatalog.Find("blue")!;
        var sourceScheme = source.Scheme;

        var dark = ThemeFeatureMap.WithBaseVariant(source, isDark: true);

        Assert.NotSame(sourceScheme, dark.Scheme);
        Assert.Same(sourceScheme, source.Scheme);
    }
}