using Configuration_Management.Models;
using Xunit;

namespace ConfigurationManagement.Tests;

/// <summary>
/// Тесты автогенерации цветовой схемы из одного базового цвета (issue #271)
/// и новой настройки синхронизации «Сохранять после правки» (issue #269).
/// </summary>
public sealed class ColorSchemeGeneratorTests
{
    [Fact]
    public void GenerateFromColor_BuildsCompleteLightAndDarkPalettes()
    {
        var scheme = ColorScheme.GenerateFromColor("#2D6CDF");

        Assert.NotNull(scheme);
        Assert.NotEmpty(scheme.LightColors);
        Assert.NotEmpty(scheme.DarkColors);

        // Все редактируемые ключи должны присутствовать в обеих палитрах.
        foreach (var (key, _) in ColorScheme.Definitions)
        {
            Assert.Contains(key, scheme.LightColors);
            Assert.Contains(key, scheme.DarkColors);
        }

        // Акцент обеих палитр — валидный HEX-цвет.
        Assert.Matches("^#[0-9A-Fa-f]{6}$", scheme.LightColors["AccentColor"]);
        Assert.Matches("^#[0-9A-Fa-f]{6}$", scheme.DarkColors["AccentColor"]);

        // Светлая и тёмная палитры различаются по акценту (разная светлость).
        Assert.NotEqual(scheme.LightColors["AccentColor"], scheme.DarkColors["AccentColor"]);
    }

    [Fact]
    public void GenerateFromColor_HandlesEdgeBaseColorsWithoutThrowing()
    {
        // Чёрный/белый имеют нулевую насыщенность — генерация не должна падать.
        var black = ColorScheme.GenerateFromColor("#000000");
        var white = ColorScheme.GenerateFromColor("#FFFFFF");
        var invalid = ColorScheme.GenerateFromColor("not-a-color");

        Assert.NotEmpty(black.LightColors);
        Assert.NotEmpty(white.LightColors);
        Assert.NotEmpty(invalid.LightColors);
    }

    [Fact]
    public void AppSettings_IbasesSaveAfterEdit_DefaultsToTrue()
    {
        // «Сохранять после правки» (issue #269) включено по умолчанию,
        // чтобы не менять прежнее поведение (выгрузка сразу после правки).
        var settings = new AppSettings();
        Assert.True(settings.IbasesSaveAfterEdit);
    }
}