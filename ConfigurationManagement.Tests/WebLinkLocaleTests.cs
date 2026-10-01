using Configuration_Management.Services;
using Xunit;

namespace ConfigurationManagement.Tests;

/// <summary>
/// Тесты нормализации web-ссылок при открытии по ссылке (issue #332):
/// завершающий сегмент локали вида /ru_RU/ отбрасывается из пути,
/// чтобы ссылка открывалась как веб-база, а не страница локали.
/// Шаблон применяется ТОЛЬКО к конечному сегменту пути (без query/fragment).
/// </summary>
public sealed class WebLinkLocaleTests
{
    // ======================= Позитив: сегмент локали в конце =======================

    [Theory]
    [InlineData("https://accounting.demo.1c.ru/accounting/ru_RU/", "https://accounting.demo.1c.ru/accounting/")]
    [InlineData("https://accounting.demo.1c.ru/accounting/en_US/", "https://accounting.demo.1c.ru/accounting/")]
    [InlineData("http://server/base/ru_RU", "http://server/base/")]
    public void StripWebLocaleSegment_EndsWithLocaleSegment_RemovesSegment(string input, string expected)
    {
        Assert.Equal(expected, OneCLauncher.StripWebLocaleSegment(input));
    }

    [Fact]
    public void StripWebLocaleSegment_WithQueryAfterLocale_KeepsQuery()
    {
        var result = OneCLauncher.StripWebLocaleSegment("https://host/base/ru_RU/?page=1&lang=ru_RU");
        Assert.Equal("https://host/base/?page=1&lang=ru_RU", result);
    }

    // ======================= Негатив: локали нет в конце пути =======================

    [Theory]
    [InlineData("https://host/ru_RU/base/")]
    [InlineData("https://host/ru_RU/en_US/extra/")]
    [InlineData("https://host/base/")]
    [InlineData("https://host/base")]
    public void StripWebLocaleSegment_LocaleInMiddleOrAbsent_LeavesUrlUnchanged(string input)
    {
        Assert.Equal(input, OneCLauncher.StripWebLocaleSegment(input));
    }

    // ======================= Негатив: query/fragment не считаются путём =======================

    [Theory]
    [InlineData("https://host/base?ru_RU")]
    [InlineData("https://host/base?lang=ru_RU")]
    [InlineData("https://host/base/#fragment_ru_RU")]
    public void StripWebLocaleSegment_LocaleOnlyInQueryOrFragment_LeavesUrlUnchanged(string input)
    {
        Assert.Equal(input, OneCLauncher.StripWebLocaleSegment(input));
    }

    // ======================= Граничные случаи =======================

    [Fact]
    public void StripWebLocaleSegment_CaseSensitive_UpperLangNotRemoved()
    {
        // Шаблон из issue чувствителен к регистру: /Ru_RU/ не распознаётся как локаль.
        const string url = "https://host/base/Ru_RU/";
        Assert.Equal(url, OneCLauncher.StripWebLocaleSegment(url));
    }

    [Fact]
    public void StripWebLocaleSegment_NullOrEmpty_ReturnsAsIs()
    {
        Assert.Null(OneCLauncher.StripWebLocaleSegment(null));
        Assert.Equal("", OneCLauncher.StripWebLocaleSegment(""));
        Assert.Equal("   ", OneCLauncher.StripWebLocaleSegment("   "));
    }
}