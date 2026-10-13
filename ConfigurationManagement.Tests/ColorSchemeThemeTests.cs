using Configuration_Management.Models;
using Xunit;

namespace ConfigurationManagement.Tests;

/// <summary>
/// Тесты расширений <see cref="ColorScheme"/> для полноценных тем (Задача 1):
/// акцентный цвет, встроенные схемы «Синяя»/«Зелёная», клонирование
/// и чтение старых JSON-файлов без акцента.
/// </summary>
public sealed class ColorSchemeThemeTests
{
    [Fact]
    public void CreateLight_HasDefaultAccentColor()
    {
        var scheme = ColorScheme.CreateLight();

        Assert.Equal("Светлая", scheme.Name);
        Assert.Equal("#FDBF00", scheme.AccentColor);
        Assert.NotEmpty(scheme.LightColors);
        Assert.NotEmpty(scheme.DarkColors);
    }

    [Fact]
    public void CreateDark_HasDefaultAccentColor()
    {
        var scheme = ColorScheme.CreateDark();

        Assert.Equal("Тёмная", scheme.Name);
        Assert.Equal("#FFB300", scheme.AccentColor);
        Assert.NotEmpty(scheme.LightColors);
        Assert.NotEmpty(scheme.DarkColors);
    }

    [Fact]
    public void CreateBlue_BuildsSchemeWithAccentInBothPalettes()
    {
        var scheme = ColorScheme.CreateBlue();

        Assert.Equal("Синяя", scheme.Name);
        Assert.Equal("#2563EB", scheme.AccentColor);
        Assert.Equal("#2563EB", scheme.LightColors["AccentColor"]);
        Assert.Equal("#2563EB", scheme.DarkColors["AccentColor"]);
        Assert.Equal("#2563EB", scheme.LightColors["SidebarSelectedColor"]);

        // Производные акценты (hover/pressed) — валидные HEX-цвета.
        Assert.Matches("^#[0-9A-Fa-f]{6}$", scheme.LightColors["AccentHoverColor"]);
        Assert.Matches("^#[0-9A-Fa-f]{6}$", scheme.LightColors["AccentPressedColor"]);
        Assert.NotEmpty(scheme.DarkColors);
    }

    [Fact]
    public void CreateGreen_BuildsSchemeWithAccentInBothPalettes()
    {
        var scheme = ColorScheme.CreateGreen();

        Assert.Equal("Зелёная", scheme.Name);
        Assert.Equal("#16A34A", scheme.AccentColor);
        Assert.Equal("#16A34A", scheme.LightColors["AccentColor"]);
        Assert.Equal("#16A34A", scheme.DarkColors["AccentColor"]);
        Assert.NotEmpty(scheme.LightColors);
        Assert.NotEmpty(scheme.DarkColors);
    }

    [Fact]
    public void Clone_CopiesAccentColorAndPalettesIndependently()
    {
        var original = ColorScheme.CreateBlue();
        var clone = original.Clone();

        Assert.Equal(original.AccentColor, clone.AccentColor);
        Assert.Equal(original.LightColors["AccentColor"], clone.LightColors["AccentColor"]);

        // Изменение копии не влияет на оригинал.
        clone.AccentColor = "#FF0000";
        clone.LightColors["AccentColor"] = "#FF0000";
        Assert.NotEqual("#FF0000", original.AccentColor);
        Assert.NotEqual("#FF0000", original.LightColors["AccentColor"]);
    }

    [Fact]
    public void FromJson_ReadsLegacyFileWithoutAccentColor()
    {
        // Старый формат: имя и (частичная) палитра без поля AccentColor.
        const string legacyJson = """
        {
          "Name": "Старая схема",
          "LightColors": {
            "AccentColor": "#112233",
            "ContentBackgroundColor": "#FFFFFF"
          }
        }
        """;

        var scheme = ColorScheme.FromJson(legacyJson);

        Assert.NotNull(scheme);
        Assert.Equal("Старая схема", scheme!.Name);
        // Normalize() заполнил акцент из активной (светлой) палитры.
        Assert.Equal("#112233", scheme.AccentColor);
        // Отсутствующие ключи дополнены значениями по умолчанию.
        Assert.Equal("#FFFFFF", scheme.LightColors["ContentBackgroundColor"]);
        Assert.NotEmpty(scheme.DarkColors);
    }

    [Fact]
    public void ToJson_IncludesAccentColor_AndOmitsNullOne()
    {
        var scheme = ColorScheme.CreateGreen();
        var json = scheme.ToJson();

        Assert.Contains("\"AccentColor\": \"#16A34A\"", json);

        // Пустой акцент не пишется (JsonIgnoreCondition.WhenWritingNull).
        var noAccent = new ColorScheme { Name = "Без акцента" };
        Assert.DoesNotContain("\"AccentColor\"", noAccent.ToJson());
    }
}