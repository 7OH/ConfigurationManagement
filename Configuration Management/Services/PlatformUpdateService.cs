using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Configuration_Management.Models;

namespace Configuration_Management.Services;

/// <summary>
/// Реализация <see cref="IPlatformUpdateService"/>: получение списка доступных версий
/// технологической платформы 1С со страницы <c>releases.1c.ru/project/Platform83</c>,
/// ленивая подгрузка файлов дистрибутива из ответа <c>version_files</c> и выбор файла
/// под текущую ОС/разрядность. Сетевые вызовы выполняются через
/// <see cref="IOneCUpdatesService.GetPageTextAsync"/> (готовая авторизация портала,
/// ручное следование редиректам), парсинг — чистым <see cref="OneCPlatformCatalogParser"/>.
/// Никакие исключения наружу не бросаются: итог всегда описывается статусом
/// <see cref="PortalFetchStatus"/> и ключом локализации «PlatformUpdate.Error.*».
/// </summary>
public sealed class PlatformUpdateService : IPlatformUpdateService
{
    /// <summary>Ключ локализации: требуется вход на портал 1С.</summary>
    public const string ErrorAuthRequired = "PlatformUpdate.Error.AuthRequired";

    /// <summary>Ключ локализации: каталог/версия не найдены (404).</summary>
    public const string ErrorNotFound = "PlatformUpdate.Error.NotFound";

    /// <summary>Ключ локализации: сетевая ошибка.</summary>
    public const string ErrorNetwork = "PlatformUpdate.Error.NetworkError";

    /// <summary>Ключ локализации: операция отменена.</summary>
    public const string ErrorCancelled = "PlatformUpdate.Error.Cancelled";

    /// <summary>Маркер страницы входа в тексте ответа (Spring Security CAS перенаправляет
    /// releases.1c.ru сюда при отсутствии сессии).</summary>
    private const string LoginHostMarker = "login.1c.ru";

    private readonly IOneCUpdatesService _updates;
    private readonly IAppLogger _logger;
    private readonly Func<string, CancellationToken, Task<string?>> _textProvider;

    /// <summary>
    /// Основной конструктор (для DI): текст страниц получает через
    /// <see cref="IOneCUpdatesService.GetPageTextAsync"/>.
    /// </summary>
    public PlatformUpdateService(IOneCUpdatesService updates, IAppLogger logger)
        : this(updates, logger, null)
    {
    }

    /// <summary>
    /// Конструктор с инжектируемым провайдером текста страницы (для тестов):
    /// принимает URL и токен отмены, возвращает текст ответа или null/исключение —
    /// маппинг ошибок выполняется по контракту, описанному в
    /// <see cref="GetAvailableReleasesAsync"/>.
    /// </summary>
    /// <param name="updates">Сервис портала 1С (используется как источник по умолчанию).</param>
    /// <param name="logger">Журнал приложения.</param>
    /// <param name="textProvider">Провайдер текста страницы; null — <c>GetPageTextAsync</c>.</param>
    internal PlatformUpdateService(
        IOneCUpdatesService updates,
        IAppLogger logger,
        Func<string, CancellationToken, Task<string?>>? textProvider)
    {
        _updates = updates ?? throw new ArgumentNullException(nameof(updates));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _textProvider = textProvider ?? ((url, ct) => _updates.GetPageTextAsync(url, ct));
    }

    /// <inheritdoc />
    public async Task<PlatformCatalogResult> GetAvailableReleasesAsync(CancellationToken ct = default)
    {
        var url = BuildCatalogUrl();
        var (status, text) = await FetchTextAsync(url, ct).ConfigureAwait(false);
        if (status != PortalFetchStatus.Ok)
            return Failure(status);

        var releases = OneCPlatformCatalogParser.ParseVersions(text!);
        if (releases.Count == 0)
        {
            // Страница получена, но ни одной версии не распознано — структура каталога
            // могла измениться либо пришёл неожиданный контент. Показываем сетевую ошибку.
            _logger.Warn($"[PlatformUpdate] Каталог получен, но версии не распознаны: {url}");
            return Failure(PortalFetchStatus.NetworkError);
        }

        return new PlatformCatalogResult { Status = PortalFetchStatus.Ok, Releases = releases };
    }

    /// <inheritdoc />
    public async Task<PlatformCatalogResult> LoadReleaseFilesAsync(
        PlatformRelease release, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(release);

        var url = BuildVersionFilesUrl(release);
        var (status, text) = await FetchTextAsync(url, ct).ConfigureAwait(false);
        if (status != PortalFetchStatus.Ok)
            return Failure(status);

        var files = OneCPlatformCatalogParser.ParseDistributionFiles(text!);
        release.VersionFilesUrl = url;
        release.Files.Clear();
        release.Files.AddRange(files);

        return new PlatformCatalogResult { Status = PortalFetchStatus.Ok, Release = release };
    }

    /// <inheritdoc />
    public PlatformReleaseFile? PickDistribution(IReadOnlyList<PlatformReleaseFile> files)
        => PickForPlatform(files, OperatingSystem.IsWindows());

    /// <summary>
    /// Внутренний чистый выбор файла дистрибутива для указанной платформы
    /// (выделен для тестирования обеих веток на любой ОС).
    /// </summary>
    /// <param name="files">Файлы дистрибутива релиза.</param>
    /// <param name="isWindows">True — выбор для Windows, false — для Linux.</param>
    /// <returns>Выбранный файл или null, если подходящего нет.</returns>
    internal static PlatformReleaseFile? PickForPlatform(
        IReadOnlyList<PlatformReleaseFile> files, bool isWindows)
    {
        if (files is null || files.Count == 0)
            return null;

        if (isWindows)
        {
            // Windows: zip-архив с setup.exe; x64 предпочтительнее x86.
            var zips = files.Where(f => f.Kind == PlatformDistributionKind.WindowsSetupZip).ToList();
            if (zips.Count == 0)
                return null;
            return zips.FirstOrDefault(Is64Bit) ?? zips.FirstOrDefault(Is32Bit) ?? zips[0];
        }

        // Linux: пакет .deb/.rpm (x64 предпочтительнее), при отсутствии — .tar.gz.
        var packages = files
            .Where(f => f.Kind is PlatformDistributionKind.LinuxDeb or PlatformDistributionKind.LinuxRpm)
            .ToList();
        return packages.FirstOrDefault(Is64Bit)
            ?? packages.FirstOrDefault()
            ?? files.FirstOrDefault(f => f.Kind == PlatformDistributionKind.LinuxTarGz);
    }

    /// <summary>True — файл собран под 64-битную архитектуру.</summary>
    private static bool Is64Bit(PlatformReleaseFile file)
        => string.Equals(file.Architecture, "x64", StringComparison.OrdinalIgnoreCase);

    /// <summary>True — файл собран под 32-битную архитектуру.</summary>
    private static bool Is32Bit(PlatformReleaseFile file)
        => string.Equals(file.Architecture, "x86", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Запрашивает текст страницы и маппит ошибки по контракту:
    /// null/пусто → <see cref="PortalFetchStatus.NetworkError"/>; текст со страницей входа
    /// (<c>login.1c.ru</c>) → <see cref="PortalFetchStatus.AuthRequired"/>; маркер
    /// «404 Not Found» → <see cref="PortalFetchStatus.NotFound"/>;
    /// <see cref="OperationCanceledException"/> → <see cref="PortalFetchStatus.Cancelled"/>;
    /// прочие исключения → <see cref="PortalFetchStatus.NetworkError"/>. Не бросает исключений.
    /// </summary>
    private async Task<(PortalFetchStatus Status, string? Text)> FetchTextAsync(
        string url, CancellationToken ct)
    {
        try
        {
            var text = await _textProvider(url, ct).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(text))
            {
                _logger.Warn($"[PlatformUpdate] Пустой ответ или HTTP-ошибка: {url}");
                return (PortalFetchStatus.NetworkError, null);
            }

            if (text.Contains(LoginHostMarker, StringComparison.OrdinalIgnoreCase))
                return (PortalFetchStatus.AuthRequired, null);
            if (LooksLikeNotFoundPage(text))
                return (PortalFetchStatus.NotFound, null);

            return (PortalFetchStatus.Ok, text);
        }
        catch (OperationCanceledException)
        {
            return (PortalFetchStatus.Cancelled, null);
        }
        catch (Exception ex)
        {
            _logger.Error($"[PlatformUpdate] Сетевая ошибка при обращении к каталогу: {url}", ex);
            return (PortalFetchStatus.NetworkError, null);
        }
    }

    /// <summary>Эвристический маркер страницы «не найдено»: «404» вместе с «Not Found»/
    /// «страница не найдена» либо только русская фраза.</summary>
    private static bool LooksLikeNotFoundPage(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;
        if (text.Contains("страница не найдена", StringComparison.OrdinalIgnoreCase))
            return true;
        return text.Contains("404", StringComparison.OrdinalIgnoreCase)
            && text.Contains("not found", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Адрес HTML-каталога версий платформы: <c>releases.1c.ru/project/Platform83</c>.</summary>
    private static string BuildCatalogUrl()
        => $"{OneCUpdatesService.ReleasesProjectBaseUrl}/{OneCPlatformCatalogParser.PlatformNick}";

    /// <summary>Абсолютный адрес страницы файлов релиза. Если у релиза ссылка не задана —
    /// строится по правилу <c>version_files?nick=…&ver=…</c>; относительная ссылка
    /// дополняется хостом портала.</summary>
    private static string BuildVersionFilesUrl(PlatformRelease release)
    {
        var url = (release.VersionFilesUrl ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(url))
        {
            url = $"{OneCUpdatesService.ReleasesBaseUrl}?nick={OneCPlatformCatalogParser.PlatformNick}" +
                  $"&ver={Uri.EscapeDataString(release.Version)}";
        }
        else if (url.StartsWith("/", StringComparison.Ordinal))
        {
            url = $"https://releases.1c.ru{url}";
        }

        return url;
    }

    /// <summary>Собирает результат ошибки с ключом локализации по статусу.</summary>
    private static PlatformCatalogResult Failure(PortalFetchStatus status)
    {
        var key = status switch
        {
            PortalFetchStatus.AuthRequired => ErrorAuthRequired,
            PortalFetchStatus.NotFound => ErrorNotFound,
            PortalFetchStatus.Cancelled => ErrorCancelled,
            _ => ErrorNetwork,
        };
        return new PlatformCatalogResult { Status = status, ErrorKey = key };
    }
}