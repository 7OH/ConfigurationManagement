using System.Threading.Tasks;
using Configuration_Management.Services;
using Xunit;

namespace ConfigurationManagement.Tests;

/// <summary>
/// Тесты выбора файла релиза при одиночном скачивании (issue #352.1): парсер nick/ver,
/// разбор страницы файлов версии («Дистрибутив обновления»/«Полный дистрибутив») и
/// чистая логика диалога повтора цепочки (issue #352.3, ChainRetryCountdown).
/// </summary>
public sealed class UpdateFileChoiceTests
{
    private const string VersionFilesHtml = """
        <html><body>
          <h1>Trade 11.5.26.118</h1>
          <table>
            <tr><td><a href="/version_file?nick=Trade110&ver=11.5.26.118&path=Trade%5c11_5_26_118%5cTrade_11_5_26_118_updsetup.cf">Дистрибутив обновления</a></td></tr>
            <tr><td><a href="/version_file?nick=Trade110&ver=11.5.26.118&path=Trade%5c11_5_26_118%5cTrade_11_5_26_118.rar">Полный дистрибутив</a></td></tr>
            <tr><td><a href="/version_file?nick=Trade110&ver=11.5.26.118&path=Trade%5c11_5_26_118%5creadme.txt">readme.txt</a></td></tr>
          </table>
        </body></html>
        """;

    // ======================= парсер nick/ver (issue #352) =======================

    [Fact]
    public void ExtractNickAndVersion_FromAdditionalFileLink_ParsesQuery()
    {
        var (nick, ver) = OneCUpdatesService.ExtractNickAndVersion(
            "https://releases.1c.ru/additional_file?nick=Trade110&ver=11.5.26.118&path=Trade%5cExtrafiles%5cRasshirenieGISMTsRPT.cf");

        Assert.Equal("Trade110", nick);
        Assert.Equal("11.5.26.118", ver);
    }

    [Fact]
    public void ExtractNickAndVersion_MissingParameters_ReturnsEmpty()
    {
        var (nick, ver) = OneCUpdatesService.ExtractNickAndVersion(
            "https://releases.1c.ru/project/Trade110");

        Assert.Equal(string.Empty, nick);
        Assert.Equal(string.Empty, ver);
    }

    // ======================= разбор страницы файлов версии =======================

    [Fact]
    public void ParseReleaseFileLinks_BothCaptions_PrioritizesUpdateDistribution()
    {
        var links = OneCUpdatesService.ParseReleaseFileLinks(
            VersionFilesHtml, "https://releases.1c.ru/version_files?nick=Trade110&ver=11.5.26.118");

        Assert.Equal(2, links.Count); // readme.txt не дистрибутив — отфильтрован.
        Assert.Equal(UpdateFileChoice.UpdateDistributionCaptionKey, links[0].CaptionKey);
        Assert.Contains("updsetup", links[0].Url, System.StringComparison.OrdinalIgnoreCase);
        Assert.Equal(UpdateFileChoice.FullDistributionCaptionKey, links[1].CaptionKey);
    }

    [Fact]
    public void ParseReleaseFileLinks_OnlyUpdateDistribution_ClassifiedByFileName()
    {
        const string html = """
            <html><body>
              <a href="/version_file?nick=Trade110&ver=11.5.26.118&path=Trade%5c11_5_26_118%5cTrade_11_5_26_118_updsetup.zip">Trade_11_5_26_118_updsetup.zip</a>
            </body></html>
            """;

        var links = OneCUpdatesService.ParseReleaseFileLinks(
            html, "https://releases.1c.ru/version_files?nick=Trade110&ver=11.5.26.118");

        var link = Assert.Single(links);
        Assert.Equal(UpdateFileChoice.UpdateDistributionCaptionKey, link.CaptionKey);
        Assert.EndsWith("Trade_11_5_26_118_updsetup.zip", link.FileName, System.StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ParseReleaseFileLinks_NothingRecognized_ReturnsEmpty()
    {
        const string html = """
            <html><body><a href="/version_file?nick=X&ver=1&path=a.txt">примечания</a></body></html>
            """;

        var links = OneCUpdatesService.ParseReleaseFileLinks(html, "https://releases.1c.ru");

        Assert.Empty(links);
    }

    [Fact]
    public void ParseReleaseFileLinks_RelativeHref_MadeAbsolute()
    {
        var links = OneCUpdatesService.ParseReleaseFileLinks(
            VersionFilesHtml, "https://releases.1c.ru/version_files?nick=Trade110&ver=11.5.26.118");

        Assert.All(links, l => Assert.StartsWith("https://releases.1c.ru/", l.Url, System.StringComparison.OrdinalIgnoreCase));
    }

    // ======================= имя файла из адреса =======================

    [Fact]
    public void ExtractFileNameFromUrl_PathParameter_ReturnsFileName()
    {
        var name = OneCUpdatesService.ExtractFileNameFromUrl(
            "https://releases.1c.ru/transfer_file?nick=Trade110&path=Trade%5cExtrafiles%5cRasshirenieGISMTsRPT.cf");

        Assert.Equal("RasshirenieGISMTsRPT.cf", name);
    }

    [Fact]
    public void ExtractFileNameFromUrl_LastPathSegment_ReturnsFileName()
    {
        var name = OneCUpdatesService.ExtractFileNameFromUrl(
            "https://releases.1c.ru/files/setuptc64_8_3_27_2325.rar");

        Assert.Equal("setuptc64_8_3_27_2325.rar", name);
    }

    [Fact]
    public void ExtractFileNameFromUrl_NoFileName_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, OneCUpdatesService.ExtractFileNameFromUrl(
            "https://releases.1c.ru/version_files?nick=X&ver=1"));
        Assert.Equal(string.Empty, OneCUpdatesService.ExtractFileNameFromUrl(null));
    }

    // ======================= отсев «чужих» файлов обновления (issue #352) =======================

    [Fact]
    public void ParseReleaseFileLinks_ForeignUpdateSuffix_FilteredOut()
    {
        // На странице файлов версии УТ лежит обновление для другой конфигурации
        // (Trade_11_5_25_80_updsetup_accounting3.zip). Оно не должно попадать в кандидаты
        // «Дистрибутива обновления» — иначе цепочка скачает файл с лишним суффиксом.
        const string html = """
            <html><body>
              <a href="/version_file?nick=Trade&ver=11.5.25.80&path=Trade%5c11_5_25_80%5cTrade_11_5_25_80_updsetup.zip">Дистрибутив обновления</a>
              <a href="/additional_file?nick=Trade&ver=11.5.25.80&path=Trade%5c11_5_25_80%5cTrade_11_5_25_80_updsetup_accounting3.zip">Дистрибутив обновления для Бухгалтерии предприятия</a>
            </body></html>
            """;

        var links = OneCUpdatesService.ParseReleaseFileLinks(
            html, "https://releases.1c.ru/version_files?nick=Trade&ver=11.5.25.80");

        var link = Assert.Single(links);
        Assert.Equal(UpdateFileChoice.UpdateDistributionCaptionKey, link.CaptionKey);
        Assert.EndsWith("Trade_11_5_25_80_updsetup.zip", link.FileName, System.StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ParseReleaseFileLinks_ForeignUpdateSuffix_NotClassifiedAsFullDistribution()
    {
        // Файл обновления для другой конфигурации отсеивается ПОЛНОСТЬЮ — даже как
        // «Полный дистрибутив» по расширению .zip (иначе он попал бы в диалог выбора).
        const string html = """
            <html><body>
              <a href="/additional_file?nick=Trade&ver=11.5.25.80&path=Trade%5c11_5_25_80%5cTrade_11_5_25_80_updsetup_accounting3.zip">Дистрибутив обновления для Бухгалтерии предприятия</a>
            </body></html>
            """;

        var links = OneCUpdatesService.ParseReleaseFileLinks(
            html, "https://releases.1c.ru/version_files?nick=Trade&ver=11.5.25.80");

        Assert.Empty(links);
    }

    // ======================= отсев «переходных» обновлений (issue #380) =======================

    [Fact]
    public void ParseReleaseFileLinks_TransitionUpdateDistribution_FilteredOut()
    {
        // На странице файлов версии лежит «Дистрибутив обновления для перехода с версии
        // ПРОФ» (AccountingCorp_3_0_208_14_updstpp.zip). Он не должен попадать в кандидаты
        // «Дистрибутива обновления» — иначе цепочка скачает переходный файл вместо updsetup.
        const string html = """
            <html><body>
              <a href="/version_file?nick=AccountingCorp30&ver=3.0.208.14&path=AccountingCorp%5c3_0_208_14%5cAccountingCorp_3_0_208_14_updsetup.zip">Дистрибутив обновления</a>
              <a href="/version_file?nick=AccountingCorp30&ver=3.0.208.14&path=AccountingCorp%5c3_0_208_14%5cAccountingCorp_3_0_208_14_updstpp.zip">Дистрибутив обновления для перехода с версии ПРОФ</a>
            </body></html>
            """;

        var links = OneCUpdatesService.ParseReleaseFileLinks(
            html, "https://releases.1c.ru/version_files?nick=AccountingCorp30&ver=3.0.208.14");

        var link = Assert.Single(links);
        Assert.Equal(UpdateFileChoice.UpdateDistributionCaptionKey, link.CaptionKey);
        Assert.EndsWith("AccountingCorp_3_0_208_14_updsetup.zip", link.FileName, System.StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ParseReleaseFileLinks_OnlyTransitionUpdateDistribution_ReturnsEmpty()
    {
        const string html = """
            <html><body>
              <a href="/version_file?nick=AccountingCorp30&ver=3.0.208.14&path=AccountingCorp%5c3_0_208_14%5cAccountingCorp_3_0_208_14_updstpp.zip">Дистрибутив обновления для перехода с версии ПРОФ</a>
            </body></html>
            """;

        var links = OneCUpdatesService.ParseReleaseFileLinks(
            html, "https://releases.1c.ru/version_files?nick=AccountingCorp30&ver=3.0.208.14");

        Assert.Empty(links);
    }

    [Fact]
    public void ParseReleaseFileLinks_TransitionFileName_NeutralCaption_FilteredOut()
    {
        // Файл с маркером updstpp отсеивается даже при нейтральной подписи «Дистрибутив
        // обновления» — фильтр работает и по имени файла (issue #380).
        const string html = """
            <html><body>
              <a href="/version_file?nick=AccountingCorp30&ver=3.0.208.14&path=AccountingCorp%5c3_0_208_14%5cAccountingCorp_3_0_208_14_updstpp.zip">Дистрибутив обновления</a>
            </body></html>
            """;

        var links = OneCUpdatesService.ParseReleaseFileLinks(
            html, "https://releases.1c.ru/version_files?nick=AccountingCorp30&ver=3.0.208.14");

        Assert.Empty(links);
    }

    // ======================= диалог повтора (issue #352.3) =======================

    [Fact]
    public void ChainRetryCountdown_ButtonText_ShowsCountdown()
    {
        Assert.Equal("Да (57)", ChainRetryCountdown.ButtonText("Да", 57));
        Assert.Equal("Да", ChainRetryCountdown.ButtonText("Да", 0));
        Assert.Equal("Да", ChainRetryCountdown.ButtonText("Да", -3));
    }

    [Fact]
    public void ChainRetryCountdown_IsFinished_AtZero()
    {
        Assert.False(ChainRetryCountdown.IsFinished(1));
        Assert.True(ChainRetryCountdown.IsFinished(0));
        Assert.True(ChainRetryCountdown.IsFinished(-1));
        Assert.Equal(60, ChainRetryCountdown.DefaultSeconds);
    }

    [Fact]
    public void ChainRetryCountdown_BuildMessage_FormatsFailedAndTotal()
    {
        var message = ChainRetryCountdown.BuildMessage("Цепочка скачалась с ошибками: {0} из {1}", 2, 5);
        Assert.Equal("Цепочка скачалась с ошибками: 2 из 5", message);
    }
}
