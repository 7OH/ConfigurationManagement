using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Configuration_Management.Models;
using Configuration_Management.Services;
using Xunit;

namespace ConfigurationManagement.Tests;

/// <summary>
/// Тесты сервиса каталога технологической платформы 1С
/// (<see cref="PlatformUpdateService"/>) на fake-провайдере текста страницы —
/// без сетевых запросов: маппинг ошибок (пусто/сеть/отмена/авторизация/404),
/// получение списка версий, ленивая подгрузка файлов релиза и выбор дистрибутива
/// под ОС/разрядность.
/// </summary>
public sealed class PlatformUpdateServiceTests
{
    /// <summary>HTML каталога с таблицей #versionsTable (фикстура, как у парсера).</summary>
    private const string VersionsTableHtml = """
        <html><body>
        <table id="versionsTable">
          <tr><td><a href="/version_files?nick=Platform83&ver=8.3.27.2214">8.3.27.2214</a></td></tr>
          <tr><td><a href="/version_files?nick=Platform83&ver=8.3.27.1688">8.3.27.1688</a></td></tr>
          <tr><td><a href="/version_files?nick=Platform83&ver=8.3.10">8.3.10</a></td></tr>
          <tr><td><a href="/version_files?nick=Platform83&ver=8.3.9">8.3.9</a></td></tr>
        </table>
        </body></html>
        """;

    /// <summary>HTML страницы входа портала (маркер login.1c.ru).</summary>
    private const string LoginPageHtml = """
        <html><body>
        <form action="https://login.1c.ru/portal/login" method="post">
          <input type="text" name="username" />
        </form>
        </body></html>
        """;

    /// <summary>Ответ version_files: JSON со ссылками на zip/deb/rpm.</summary>
    private const string VersionFilesJson = """
        [{"FileName":"8.3.27.2214_x64.zip","url":"https://releases.1c.ru/dist/8.3.27.2214_x64.zip","size":314572800},
         {"FileName":"8.3.27.2214_x86.zip","url":"https://releases.1c.ru/dist/8.3.27.2214_x86.zip","size":241172480},
         {"FileName":"8.3.27.2214_amd64.deb","url":"https://releases.1c.ru/dist/8.3.27.2214_amd64.deb","size":161061273},
         {"FileName":"8.3.27.2214_arm64.tar.gz","url":"https://releases.1c.ru/dist/8.3.27.2214_arm64.tar.gz","size":33554432}]
        """;

    [Fact]
    public async Task GetAvailableReleasesAsync_SuccessfulHtml_ReturnsOkWithSortedReleases()
    {
        var service = CreateService(_ => Task.FromResult<string?>(VersionsTableHtml));

        var result = await service.GetAvailableReleasesAsync();

        Assert.Equal(PortalFetchStatus.Ok, result.Status);
        Assert.Equal(string.Empty, result.ErrorKey);
        Assert.Equal(new[] { "8.3.27.2214", "8.3.27.1688", "8.3.10", "8.3.9" },
            result.Releases.Select(r => r.Version));
    }

    [Fact]
    public async Task GetAvailableReleasesAsync_UsesProjectUrlForPlatformNick()
    {
        string? requestedUrl = null;
        var service = CreateService(url =>
        {
            requestedUrl = url;
            return Task.FromResult<string?>(VersionsTableHtml);
        });

        await service.GetAvailableReleasesAsync();

        Assert.Equal($"https://releases.1c.ru/project/{OneCPlatformCatalogParser.PlatformNick}", requestedUrl);
    }

    [Fact]
    public async Task GetAvailableReleasesAsync_NullText_ReturnsNetworkError()
    {
        var service = CreateService(_ => Task.FromResult<string?>(null));

        var result = await service.GetAvailableReleasesAsync();

        Assert.Equal(PortalFetchStatus.NetworkError, result.Status);
        Assert.Equal(PlatformUpdateService.ErrorNetwork, result.ErrorKey);
    }

    [Fact]
    public async Task GetAvailableReleasesAsync_EmptyText_ReturnsNetworkError()
    {
        var service = CreateService(_ => Task.FromResult<string?>(string.Empty));

        var result = await service.GetAvailableReleasesAsync();

        Assert.Equal(PortalFetchStatus.NetworkError, result.Status);
    }

    [Fact]
    public async Task GetAvailableReleasesAsync_ProviderThrows_ReturnsNetworkError()
    {
        var service = CreateService(_ => throw new InvalidOperationException("boom"));

        var result = await service.GetAvailableReleasesAsync();

        Assert.Equal(PortalFetchStatus.NetworkError, result.Status);
        Assert.Equal(PlatformUpdateService.ErrorNetwork, result.ErrorKey);
    }

    [Fact]
    public async Task GetAvailableReleasesAsync_Cancelled_ReturnsCancelled()
    {
        var service = CreateService(_ => throw new OperationCanceledException());

        var result = await service.GetAvailableReleasesAsync();

        Assert.Equal(PortalFetchStatus.Cancelled, result.Status);
        Assert.Equal(PlatformUpdateService.ErrorCancelled, result.ErrorKey);
    }

    [Fact]
    public async Task GetAvailableReleasesAsync_LoginPageText_ReturnsAuthRequired()
    {
        var service = CreateService(_ => Task.FromResult<string?>(LoginPageHtml));

        var result = await service.GetAvailableReleasesAsync();

        Assert.Equal(PortalFetchStatus.AuthRequired, result.Status);
        Assert.Equal(PlatformUpdateService.ErrorAuthRequired, result.ErrorKey);
    }

    [Fact]
    public async Task GetAvailableReleasesAsync_NotFoundMarker_ReturnsNotFound()
    {
        var service = CreateService(_ => Task.FromResult<string?>("<html><title>404 Not Found</title></html>"));

        var result = await service.GetAvailableReleasesAsync();

        Assert.Equal(PortalFetchStatus.NotFound, result.Status);
        Assert.Equal(PlatformUpdateService.ErrorNotFound, result.ErrorKey);
    }

    [Fact]
    public async Task LoadReleaseFilesAsync_FillsFilesAndSavesAbsoluteVersionFilesUrl()
    {
        var release = new PlatformRelease { Version = "8.3.27.2214", VersionFilesUrl = "/version_files?nick=Platform83&ver=8.3.27.2214" };
        var service = CreateService(_ => Task.FromResult<string?>(VersionFilesJson));

        var result = await service.LoadReleaseFilesAsync(release);

        Assert.Equal(PortalFetchStatus.Ok, result.Status);
        Assert.Same(release, result.Release);
        Assert.Equal("https://releases.1c.ru/version_files?nick=Platform83&ver=8.3.27.2214", release.VersionFilesUrl);
        Assert.Equal(4, release.Files.Count);
        Assert.Contains(release.Files, f => f.FileName == "8.3.27.2214_x64.zip" && f.Kind == PlatformDistributionKind.WindowsSetupZip);
        Assert.Contains(release.Files, f => f.FileName == "8.3.27.2214_amd64.deb" && f.Kind == PlatformDistributionKind.LinuxDeb);
    }

    [Fact]
    public async Task LoadReleaseFilesAsync_EmptyVersionFilesUrl_BuildsFromVersion()
    {
        var release = new PlatformRelease { Version = "8.3.27.2214" };
        string? requestedUrl = null;
        var service = CreateService(url =>
        {
            requestedUrl = url;
            return Task.FromResult<string?>(VersionFilesJson);
        });

        var result = await service.LoadReleaseFilesAsync(release);

        Assert.Equal(PortalFetchStatus.Ok, result.Status);
        Assert.Equal("https://releases.1c.ru/version_files?nick=Platform83&ver=8.3.27.2214", requestedUrl);
    }

    // --- PickDistribution / PickForPlatform ---

    [Fact]
    public void PickForPlatform_Windows_PrefersX64ZipOverX86()
    {
        var files = new List<PlatformReleaseFile>
        {
            NewFile("setup_x86.zip", "x86", PlatformDistributionKind.WindowsSetupZip),
            NewFile("setup_x64.zip", "x64", PlatformDistributionKind.WindowsSetupZip),
        };

        var picked = PlatformUpdateService.PickForPlatform(files, isWindows: true);

        Assert.NotNull(picked);
        Assert.Equal("setup_x64.zip", picked.FileName);
    }

    [Fact]
    public void PickForPlatform_Windows_OnlyX86Zip_PicksX86()
    {
        var files = new List<PlatformReleaseFile>
        {
            NewFile("setup_x86.zip", "x86", PlatformDistributionKind.WindowsSetupZip),
        };

        var picked = PlatformUpdateService.PickForPlatform(files, isWindows: true);

        Assert.Equal("setup_x86.zip", picked!.FileName);
    }

    [Fact]
    public void PickForPlatform_Windows_NoZip_ReturnsNull()
    {
        var files = new List<PlatformReleaseFile>
        {
            NewFile("8.3.27.2214_amd64.deb", "x64", PlatformDistributionKind.LinuxDeb),
        };

        var picked = PlatformUpdateService.PickForPlatform(files, isWindows: true);

        Assert.Null(picked);
    }

    [Fact]
    public void PickForPlatform_Linux_PrefersX64DebOverX86Rpm()
    {
        var files = new List<PlatformReleaseFile>
        {
            NewFile("8.3.27.2214_i386.rpm", "x86", PlatformDistributionKind.LinuxRpm),
            NewFile("8.3.27.2214_amd64.deb", "x64", PlatformDistributionKind.LinuxDeb),
        };

        var picked = PlatformUpdateService.PickForPlatform(files, isWindows: false);

        Assert.Equal("8.3.27.2214_amd64.deb", picked!.FileName);
    }

    [Fact]
    public void PickForPlatform_Linux_NoX64Package_PicksAnyPackage()
    {
        var files = new List<PlatformReleaseFile>
        {
            NewFile("8.3.27.2214_i386.rpm", "x86", PlatformDistributionKind.LinuxRpm),
            NewFile("8.3.27.2214.tar.gz", null, PlatformDistributionKind.LinuxTarGz),
        };

        var picked = PlatformUpdateService.PickForPlatform(files, isWindows: false);

        Assert.Equal("8.3.27.2214_i386.rpm", picked!.FileName);
    }

    [Fact]
    public void PickForPlatform_Linux_OnlyTarGz_PicksTarGz()
    {
        var files = new List<PlatformReleaseFile>
        {
            NewFile("8.3.27.2214_arm64.tar.gz", null, PlatformDistributionKind.LinuxTarGz),
        };

        var picked = PlatformUpdateService.PickForPlatform(files, isWindows: false);

        Assert.Equal("8.3.27.2214_arm64.tar.gz", picked!.FileName);
    }

    [Fact]
    public void PickForPlatform_EmptyList_ReturnsNull()
    {
        Assert.Null(PlatformUpdateService.PickForPlatform(new List<PlatformReleaseFile>(), isWindows: true));
        Assert.Null(PlatformUpdateService.PickForPlatform(new List<PlatformReleaseFile>(), isWindows: false));
        Assert.Null(PlatformUpdateService.PickForPlatform(null!, isWindows: true));
    }

    /// <summary>Создаёт сервис с инжектируемым провайдером текста страницы.</summary>
    private static PlatformUpdateService CreateService(Func<string, Task<string?>> provider)
    {
        return new PlatformUpdateService(new StubUpdates(), new StubLogger(), (url, _) => provider(url));
    }

    /// <summary>Создаёт файл дистрибутива для тестов выбора.</summary>
    private static PlatformReleaseFile NewFile(string fileName, string? architecture, PlatformDistributionKind kind)
        => new()
        {
            FileName = fileName,
            Url = $"https://releases.1c.ru/dist/{fileName}",
            Architecture = architecture,
            Kind = kind,
        };

    /// <summary>Заглушка IOneCUpdatesService: сетевые методы не используются (fake-провайдер).</summary>
    private sealed class StubUpdates : IOneCUpdatesService
    {
        public IReadOnlyList<OneCConfigType> BuiltInConfigTypes => Array.Empty<OneCConfigType>();

        public string BuildUpdateUrl(OneCConfigType? config, OneCConfigEdition? edition, string? urlOverride, string? urlSegment = null)
            => string.Empty;

        public Task<ConfigUpdateCheckResult> CheckForUpdatesAsync(
            string configName, string currentVersion, string url, CancellationToken ct = default)
            => Task.FromResult(new ConfigUpdateCheckResult());

        public Task<string?> DownloadUpdateAsync(
            string url, string targetPath, IProgress<double>? progress = null, CancellationToken ct = default)
            => Task.FromResult<string?>(null);

        public Task<string?> DownloadDistributionAsync(
            string url, string targetPath, IProgress<double>? progress = null, CancellationToken ct = default)
            => Task.FromResult<string?>(null);

        public Task<string?> GetPageTextAsync(string url, CancellationToken ct = default)
            => Task.FromResult<string?>(null);
    }

    /// <summary>Заглушка журнала приложения.</summary>
    private sealed class StubLogger : IAppLogger
    {
        public void Info(string message)
        {
        }

        public void Warn(string message)
        {
        }

        public void Error(string message, Exception? exception = null)
        {
        }
    }
}