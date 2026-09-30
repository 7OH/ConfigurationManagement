using System.Collections.Generic;
using System.Linq;
using Configuration_Management.Models;
using Configuration_Management.Services;
using Xunit;

namespace ConfigurationManagement.Tests;

/// <summary>
/// Тесты чистого парсера каталога технологической платформы 1С
/// (<see cref="OneCPlatformCatalogParser"/>) на HTML/JSON-фикстурах — без сетевых запросов:
/// список версий со страницы <c>project/Platform83</c> (все строки #versionsTable, fallback,
/// дедупликация, нормализация) и файлы дистрибутива из ответа <c>version_files</c>.
/// </summary>
public sealed class OneCPlatformCatalogParserTests
{
    /// <summary>HTML каталога с таблицей #versionsTable: 6 строк с version_files-ссылками,
    /// из них одна — дубликат «8.3.27.1688», плюс «мусорные» строки без ссылок.</summary>
    private const string VersionsTableHtml = """
        <html><body>
        <h1>Технологическая платформа 8.3</h1>
        <table id="versionsTable">
          <tr><th>Версия</th><th>Дата</th></tr>
          <tr><td><a href="/version_files?nick=Platform83&ver=8.3.27.2214">8.3.27.2214</a></td><td>01.10.2026</td></tr>
          <tr><td><a href="/version_files?nick=Platform83&ver=8.3.27.1688">8.3.27.1688</a></td><td>15.09.2026</td></tr>
          <tr><td>Неизвестная строка без ссылки</td></tr>
          <tr><td><a href="/other/page">не version_files</a></td></tr>
          <tr><td><a href="/version_files?nick=Platform83&ver=8.3.26.1890">8.3.26.1890</a></td></tr>
          <tr><td><a href="/version_files?nick=Platform83&ver=8.3.10">8.3.10</a></td></tr>
          <tr><td><a href="/version_files?nick=Platform83&ver=8.3.9">8.3.9</a></td></tr>
          <tr><td><a href="/version_files?nick=Platform83&ver=8.3.27.1688">8.3.27.1688</a></td><td>дубликат</td></tr>
        </table>
        </body></html>
        """;

    [Fact]
    public void ParseVersions_StandardTable_ReturnsAllVersionsSortedDescending()
    {
        var releases = OneCPlatformCatalogParser.ParseVersions(VersionsTableHtml);

        Assert.Equal(5, releases.Count);
        Assert.Equal(new[] { "8.3.27.2214", "8.3.27.1688", "8.3.26.1890", "8.3.10", "8.3.9" },
            releases.Select(r => r.Version));
    }

    [Fact]
    public void ParseVersions_StandardTable_FillsVersionFilesUrlFromHref()
    {
        var releases = OneCPlatformCatalogParser.ParseVersions(VersionsTableHtml);

        var newest = releases[0];
        Assert.Equal("8.3.27.2214", newest.Version);
        Assert.Equal("/version_files?nick=Platform83&ver=8.3.27.2214", newest.VersionFilesUrl);
    }

    [Fact]
    public void ParseVersions_Duplicates_AreDeduplicatedKeepingFirstHref()
    {
        var releases = OneCPlatformCatalogParser.ParseVersions(VersionsTableHtml);

        // «8.3.27.1688» встречается дважды — в списке одна запись с первой ссылкой.
        var dup = releases.Single(r => r.Version == "8.3.27.1688");
        Assert.Equal("/version_files?nick=Platform83&ver=8.3.27.1688", dup.VersionFilesUrl);
    }

    [Fact]
    public void ParseVersions_NoVersionsTable_FallsBackToLinksAcrossWholeHtml()
    {
        var html = """
            <html><body>
            <p>Список обновлений:</p>
            <a href="/version_files?nick=Platform83&ver=8.3.27.2214">8.3.27.2214</a>
            <a href="/version_files?nick=Platform83&ver=8.3.26.1890">8.3.26.1890</a>
            </body></html>
            """;

        var releases = OneCPlatformCatalogParser.ParseVersions(html);

        Assert.Equal(2, releases.Count);
        Assert.Equal(new[] { "8.3.27.2214", "8.3.26.1890" }, releases.Select(r => r.Version));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("<html><broken")]
    public void ParseVersions_EmptyOrBrokenHtml_ReturnsEmptyList(string? html)
    {
        var releases = OneCPlatformCatalogParser.ParseVersions(html!);

        Assert.Empty(releases);
    }

    [Fact]
    public void ParseVersions_WhitespaceAndHtmlEntities_AreNormalized()
    {
        var html = """
            <html><body>
            <table id="versionsTable">
              <tr><td><a href="/version_files?nick=Platform83&ver=8.3.27.2214">8.3.27.
                2214 </a></td></tr>
              <tr><td><a href="/version_files?nick=Platform83&ver=8.3.26.1890">8.3.26.&nbsp;1890</a></td></tr>
            </table>
            </body></html>
            """;

        var releases = OneCPlatformCatalogParser.ParseVersions(html);

        Assert.Equal(new[] { "8.3.27.2214", "8.3.26.1890" }, releases.Select(r => r.Version));
    }

    /// <summary>JSON-ответ version_files: файлы всех типов + неизвестное расширение (.msi),
    /// размеры в полях size/filesize.</summary>
    private const string DistributionJson = """
        {
          "success": true,
          "items": [
            { "name": "8.3.27.2214_x64.zip", "url": "/version_files/get/8.3.27.2214_x64.zip", "size": 640234356 },
            { "name": "8.3.27.2214.zip", "url": "/version_files/get/8.3.27.2214.zip", "filesize": 33554432 },
            { "name": "8.3.27.2214_amd64.deb", "url": "/version_files/get/8.3.27.2214_amd64.deb", "size": 220200960 },
            { "name": "8.3.27.2214.x86_64.rpm", "url": "/version_files/get/8.3.27.2214.x86_64.rpm", "filesize": 176160768 },
            { "name": "8.3.27.2214.tar.gz", "url": "/version_files/get/8.3.27.2214.tar.gz", "size": 123456789 },
            { "name": "8.3.27.2214_i386.deb", "url": "/version_files/get/8.3.27.2214_i386.deb", "size": 1234 },
            { "name": "8.3.27.2214.msi", "url": "/version_files/get/8.3.27.2214.msi", "size": 987654321 }
          ]
        }
        """;

    [Fact]
    public void ParseDistributionFiles_ClassifiesKindsAndArchitecture()
    {
        var files = OneCPlatformCatalogParser.ParseDistributionFiles(DistributionJson);

        // .msi (неизвестное расширение) пропускается.
        Assert.Equal(6, files.Count);

        var x64Zip = files.Single(f => f.FileName == "8.3.27.2214_x64.zip");
        Assert.Equal(PlatformDistributionKind.WindowsSetupZip, x64Zip.Kind);
        Assert.Equal("x64", x64Zip.Architecture);

        var plainZip = files.Single(f => f.FileName == "8.3.27.2214.zip");
        Assert.Equal(PlatformDistributionKind.WindowsSetupZip, plainZip.Kind);
        Assert.Null(plainZip.Architecture);

        var deb = files.Single(f => f.FileName == "8.3.27.2214_amd64.deb");
        Assert.Equal(PlatformDistributionKind.LinuxDeb, deb.Kind);
        Assert.Equal("x64", deb.Architecture);

        var rpm = files.Single(f => f.FileName == "8.3.27.2214.x86_64.rpm");
        Assert.Equal(PlatformDistributionKind.LinuxRpm, rpm.Kind);
        Assert.Equal("x64", rpm.Architecture);

        var tarGz = files.Single(f => f.FileName == "8.3.27.2214.tar.gz");
        Assert.Equal(PlatformDistributionKind.LinuxTarGz, tarGz.Kind);
        Assert.Null(tarGz.Architecture);

        var i386 = files.Single(f => f.FileName == "8.3.27.2214_i386.deb");
        Assert.Equal(PlatformDistributionKind.LinuxDeb, i386.Kind);
        Assert.Equal("x86", i386.Architecture);
    }

    [Fact]
    public void ParseDistributionFiles_ReadsSizesFromJsonSizeAndFilesizeFields()
    {
        var files = OneCPlatformCatalogParser.ParseDistributionFiles(DistributionJson);

        Assert.Equal(640234356L, files.Single(f => f.FileName == "8.3.27.2214_x64.zip").SizeBytes);
        Assert.Equal(33554432L, files.Single(f => f.FileName == "8.3.27.2214.zip").SizeBytes);
        Assert.Equal(220200960L, files.Single(f => f.FileName == "8.3.27.2214_amd64.deb").SizeBytes);
        Assert.Equal(176160768L, files.Single(f => f.FileName == "8.3.27.2214.x86_64.rpm").SizeBytes);
        Assert.Equal(123456789L, files.Single(f => f.FileName == "8.3.27.2214.tar.gz").SizeBytes);
    }

    [Fact]
    public void ParseDistributionFiles_LinksWithQueryPart_KeepsUrlAndExtractsFileName()
    {
        var html = """
            <html><body>
            <a href="https://releases.1c.ru/version_files/get/8.3.27.2214_x64.zip?token=xyz123&exp=1">x64</a>
            <a href="/version_files/get/8.3.27.2214_32.zip#fragment">32</a>
            </body></html>
            """;

        var files = OneCPlatformCatalogParser.ParseDistributionFiles(html);

        Assert.Equal(2, files.Count);
        var withQuery = files.Single(f => f.FileName == "8.3.27.2214_x64.zip");
        Assert.Equal("https://releases.1c.ru/version_files/get/8.3.27.2214_x64.zip?token=xyz123&exp=1", withQuery.Url);
        Assert.Equal("x64", withQuery.Architecture);

        var withFragment = files.Single(f => f.FileName == "8.3.27.2214_32.zip");
        Assert.Equal("/version_files/get/8.3.27.2214_32.zip#fragment", withFragment.Url);
        Assert.Equal("x86", withFragment.Architecture);
    }

    [Fact]
    public void ParseDistributionFiles_HtmlWithoutKnownExtensions_ReturnsEmpty()
    {
        var html = "<html><body><a href=\"/version_files/get/file.cf\">cf</a></body></html>";

        var files = OneCPlatformCatalogParser.ParseDistributionFiles(html);

        Assert.Empty(files);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{broken json")]
    public void ParseDistributionFiles_EmptyOrBrokenBody_ReturnsEmptyList(string? body)
    {
        var files = OneCPlatformCatalogParser.ParseDistributionFiles(body!);

        Assert.Empty(files);
    }

    [Fact]
    public void CompareVersions_NumericSegments_ComparesCorrectly()
    {
        Assert.True(OneCPlatformCatalogParser.CompareVersions("8.3.27.2214", "8.3.27.1688") > 0);
        Assert.True(OneCPlatformCatalogParser.CompareVersions("8.3.27.1688", "8.3.27.2214") < 0);
        Assert.True(OneCPlatformCatalogParser.CompareVersions("8.3.10", "8.3.9") > 0);
        Assert.True(OneCPlatformCatalogParser.CompareVersions("8.3.9", "8.3.10") < 0);
        Assert.Equal(0, OneCPlatformCatalogParser.CompareVersions("8.3.27.1688", "8.3.27.1688"));
    }

    [Fact]
    public void PlatformNick_IsPlatform83()
    {
        Assert.Equal("Platform83", OneCPlatformCatalogParser.PlatformNick);
    }
}