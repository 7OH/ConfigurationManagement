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
}