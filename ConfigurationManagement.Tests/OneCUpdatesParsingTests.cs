using System;
using System.Collections.Generic;
using System.Linq;
using Configuration_Management.Services;
using Xunit;

namespace ConfigurationManagement.Tests;

/// <summary>
/// Тесты устойчивого парсинга версий из HTML-каталога releases.1c.ru (issue #371):
/// короткие страницы УПП/Розницы (len≈11760) не содержат таблицы #versionsTable,
/// ссылок version_files и атрибутов data-ver — версия извлекается из версионных
/// токенов текста с отсевом дат и «годоподобных» токенов.
/// </summary>
public sealed class OneCUpdatesParsingTests
{
    [Fact]
    public void ParseLatestVersionFromProjectHtml_ShortPageWithoutTable_ReturnsMaxVersionToken()
    {
        // Короткая страница каталога (как у УПП/Розницы, issue #371): нет таблицы,
        // нет ссылок version_files, нет data-ver. Версии — только в тексте, рядом даты.
        const string html =
            "<html><head><title>Обновления</title></head><body>" +
            "<h1>Каталог обновлений</h1>" +
            "<p>Последняя версия: 8.3.26.1656</p>" +
            "<p>Предыдущая: 8.5.4.442</p>" +
            "<p>Дата публикации: 2026.10.12</p>" +
            "<p>Обновлено: 10.12.2026</p>" +
            "<p>Год: 2025</p>" +
            "</body></html>";

        var latest = OneCUpdatesService.ParseLatestVersionFromProjectHtml(html);

        // 8.5.4.442 > 8.3.26.1656; даты/годы отсеяны.
        Assert.Equal("8.5.4.442", latest);
    }

    [Fact]
    public void ParseLatestVersionFromProjectHtml_ShortPageWithSingleToken_ReturnsIt()
    {
        const string html = "<html><body><p>Версия: 8.3.26.1656</p></body></html>";

        var latest = OneCUpdatesService.ParseLatestVersionFromProjectHtml(html);

        Assert.Equal("8.3.26.1656", latest);
    }

    [Fact]
    public void ParseLatestVersionFromProjectHtml_OnlyDates_ReturnsEmpty()
    {
        // Только даты и годы — версии нет, парсер не должен выдать мусор.
        const string html = "<html><body><p>2026.10.12</p><p>10.12.2026</p><p>2025</p></body></html>";

        var latest = OneCUpdatesService.ParseLatestVersionFromProjectHtml(html);

        Assert.Equal(string.Empty, latest);
    }

    [Fact]
    public void ParseLatestVersionFromProjectHtml_EmptyOrNull_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, OneCUpdatesService.ParseLatestVersionFromProjectHtml(null));
        Assert.Equal(string.Empty, OneCUpdatesService.ParseLatestVersionFromProjectHtml(string.Empty));
        Assert.Equal(string.Empty, OneCUpdatesService.ParseLatestVersionFromProjectHtml("   "));
    }

    [Fact]
    public void ExtractVersionTokens_ReturnsVersionTokensInOrder()
    {
        const string html = "8.3.26.1656 и 8.5.4.442";

        var tokens = OneCUpdatesService.ExtractVersionTokens(html).ToList();

        Assert.Equal(new[] { "8.3.26.1656", "8.5.4.442" }, tokens);
    }

    [Fact]
    public void ExtractVersionTokens_FiltersDatesAndYears()
    {
        // Первый или последний сегмент ≥ 1900 — дата/год, отсекается.
        const string html = "8.3.26.1656 2026.10.12 10.12.2026 2025 8.5.4.442";

        var tokens = OneCUpdatesService.ExtractVersionTokens(html).ToList();

        Assert.Equal(new[] { "8.3.26.1656", "8.5.4.442" }, tokens);
    }

    [Fact]
    public void ExtractVersionTokens_DeduplicatesRepeatedTokens()
    {
        const string html = "8.3.26.1656 ... 8.3.26.1656 ... 8.3.26.1656";

        var tokens = OneCUpdatesService.ExtractVersionTokens(html).ToList();

        Assert.Single(tokens);
        Assert.Equal("8.3.26.1656", tokens[0]);
    }

    [Fact]
    public void ExtractVersionTokens_EmptyOrNull_ReturnsEmpty()
    {
        Assert.Empty(OneCUpdatesService.ExtractVersionTokens(null));
        Assert.Empty(OneCUpdatesService.ExtractVersionTokens(string.Empty));
        Assert.Empty(OneCUpdatesService.ExtractVersionTokens("   "));
    }

    [Fact]
    public void ExtractVersionTokens_IgnoresTwoPartTokens()
    {
        // Двухчастные токены (например 8.3) не являются полными версиями релиза.
        const string html = "8.3 8.3.26.1656 1.0.1";

        var tokens = OneCUpdatesService.ExtractVersionTokens(html).ToList();

        Assert.Equal(new[] { "8.3.26.1656", "1.0.1" }, tokens);
    }

    [Fact]
    public void ExtractVersionTokens_FiltersForeignFirstSegment()
    {
        // issue #371: на короткой странице Розницы/УПП встречается посторонний токен «4.2.0»
        // (версия другого продукта). Версии конфигураций 1С начинаются с 1–3, платформы — с 8;
        // «7.1.0»/«9.0.1.2» — чужие первые сегменты, тоже отсекаются.
        const string html = "4.2.0 8.3.26.1656 3.0.14.168 7.1.0 9.0.1.2";

        var tokens = OneCUpdatesService.ExtractVersionTokens(html).ToList();

        Assert.Equal(new[] { "8.3.26.1656", "3.0.14.168" }, tokens);
    }

    [Fact]
    public void ParseLatestVersionFromProjectHtml_WithForeignToken_ReturnsMaxConfigVersion()
    {
        // issue #371: страница Розницы содержит посторонний «4.2.0» (вероятно, версию
        // платформы/другого продукта) и реальную версию конфигурации 3.0.14.168.
        // Посторонний токен отсекается фильтром — парсер возвращает версию конфигурации.
        const string html =
            "<html><body>" +
            "<h1>Розница</h1>" +
            "<p>Версия: 4.2.0</p>" +
            "<p>Доступная версия: 3.0.14.168</p>" +
            "<p>Дата: 2026.10.12</p>" +
            "</body></html>";

        var latest = OneCUpdatesService.ParseLatestVersionFromProjectHtml(html);

        Assert.Equal("3.0.14.168", latest);
    }

    [Fact]
    public void ParseLatestVersionFromProjectHtml_WithFamilyHint_ReturnsMaxInFamily()
    {
        // issue #371: на короткой странице Розницы есть посторонний «2.2.10» (другое семейство)
        // и реальная версия 3.0.14.168. Семейство текущей версии 3.0.x — максимум среди 3.x.
        const string html =
            "<html><body>" +
            "<h1>Розница</h1>" +
            "<p>Версия: 2.2.10</p>" +
            "<p>Доступная версия: 3.0.14.168</p>" +
            "</body></html>";

        var latest = OneCUpdatesService.ParseLatestVersionFromProjectHtml(html, "3.0.14.100");

        Assert.Equal("3.0.14.168", latest);
    }

    [Fact]
    public void ParseLatestVersionFromProjectHtml_WithFamilyHint_NoMatch_ReturnsEmpty()
    {
        // issue #371: на короткой странице только посторонний «2.2.10» (семейство 2),
        // а текущая версия 3.0.x — версия каталога не определена (честный Unavailable,
        // а не показ чужой версии).
        const string html = "<html><body><p>Версия: 2.2.10</p></body></html>";

        var latest = OneCUpdatesService.ParseLatestVersionFromProjectHtml(html, "3.0.14.100");

        Assert.Equal(string.Empty, latest);
    }

    [Fact]
    public void ParseLatestVersionFromProjectHtml_WithFamilyHint_PlatformTokenSkipped()
    {
        // issue #371: на странице конфигурации есть версия платформы 8.x и версия
        // конфигурации 1.3.x. Семейство 1.3 — максимум среди 1.x, платформа отсекается.
        const string html =
            "<html><body>" +
            "<p>Платформа: 8.3.26.1656</p>" +
            "<p>Доступная версия: 1.3.285.2</p>" +
            "</body></html>";

        var latest = OneCUpdatesService.ParseLatestVersionFromProjectHtml(html, "1.3.285.1");

        Assert.Equal("1.3.285.2", latest);
    }

    [Fact]
    public void ParseLatestVersionFromProjectHtml_WithoutFamilyHint_ReturnsGlobalMax()
    {
        // Без семейства (прежнее поведение) — максимум среди всех кандидатов.
        const string html =
            "<html><body>" +
            "<p>Версия: 2.2.10</p>" +
            "<p>Доступная версия: 3.0.14.168</p>" +
            "</body></html>";

        var latest = OneCUpdatesService.ParseLatestVersionFromProjectHtml(html);

        Assert.Equal("3.0.14.168", latest);
    }

    [Fact]
    public void LooksLikeForeignVersion_DetectsPlatformAndForeign()
    {
        // «Подозрительные» версии на странице конфигурации: платформа 8.x и посторонние 4.x.
        Assert.True(OneCUpdatesService.LooksLikeForeignVersion("8.3.27.2214"));
        Assert.True(OneCUpdatesService.LooksLikeForeignVersion("4.2.0"));
        Assert.True(OneCUpdatesService.LooksLikeForeignVersion("11.4.5.6"));

        // Версии конфигураций 1С (1–3) подозрительными не являются.
        Assert.False(OneCUpdatesService.LooksLikeForeignVersion("3.0.14.168"));
        Assert.False(OneCUpdatesService.LooksLikeForeignVersion("1.3.285.2"));
        Assert.False(OneCUpdatesService.LooksLikeForeignVersion("2.3.12.41"));

        // Мусор/пусто — не версия вовсе.
        Assert.False(OneCUpdatesService.LooksLikeForeignVersion(null));
        Assert.False(OneCUpdatesService.LooksLikeForeignVersion(string.Empty));
        Assert.False(OneCUpdatesService.LooksLikeForeignVersion("abc"));
    }
}