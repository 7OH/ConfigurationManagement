using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Configuration_Management.Models;

namespace Configuration_Management.Services;

/// <summary>
/// Реализация <see cref="IOneCUpdatesService"/>: формирование web-адреса обновлений
/// по правилу 1С, проверка наличия новых релизов в каталоге и загрузка дистрибутива
/// с прогрессом. Паттерны сети (static <c>HttpClient</c>, User-Agent, таймаут,
/// устойчивый парсинг, отсутствие падения при ошибках) — по образцу
/// <see cref="GitHubReleaseService"/> и <see cref="UpdateService"/>.
/// </summary>
public class OneCUpdatesService : IOneCUpdatesService
{
    /// <summary>Базовый адрес web-ресурса обновлений 1С (сегмент <c>1cbsl</c> для типовых решений).</summary>
    public const string DefaultBaseUrl = "https://downloads.1c.ru/ipp/1cbsl/";

    private const int TimeoutSeconds = 15;

    private static readonly HttpClient HttpClient = CreateHttpClient();

    /// <summary>Шаблон ссылки на архив дистрибутива конфигурации на странице каталога.</summary>
    private static readonly Regex ArchiveLinkRegex =
        new(@"href\s*=\s*[""'](?<url>[^""']*(?:setup|1c[^""']*)\.zip)[""']",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <inheritdoc />
    public IReadOnlyList<OneCConfigType> BuiltInConfigTypes => BuiltInConfigTypesHolder.All;

    private static class BuiltInConfigTypesHolder
    {
        internal static readonly IReadOnlyList<OneCConfigType> All =
            Services.BuiltInConfigTypes.All;
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient(new HttpClientHandler
        {
            // Каталог 1С может выдавать перенаправления на CDN — разрешаем автоперенаправление.
            AllowAutoRedirect = true,
        })
        {
            Timeout = TimeSpan.FromSeconds(TimeoutSeconds),
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "ConfigurationManagement/0.3.8.11 (+https://github.com/sivatorov/ConfigurationManagement)");
        client.DefaultRequestHeaders.Accept.ParseAdd("text/html,application/xhtml+xml,*/*;q=0.8");
        return client;
    }

    /// <inheritdoc />
    public string BuildUpdateUrl(OneCConfigType? config, OneCConfigEdition? edition, string? urlOverride)
    {
        if (!string.IsNullOrWhiteSpace(urlOverride))
            return urlOverride.Trim();

        if (config is null)
            return string.Empty;

        // Если у редакции есть собственная переопределённая ссылка — используем её.
        if (edition is { HasUrlOverride: true })
            return edition.UrlOverride.Trim();

        var segments = new List<string> { "Configs", config.EffectiveUrlCode };
        if (edition is not null && !string.IsNullOrWhiteSpace(edition.Red))
            segments.Add(edition.Red);
        if (edition is not null && !string.IsNullOrWhiteSpace(edition.SubRed))
            segments.Add(edition.SubRed);

        var baseUrl = DefaultBaseUrl.TrimEnd('/') + "/";
        var path = string.Join("/", segments.Select(Uri.EscapeDataString));
        var url = baseUrl + path + "/";

        // Проверка валидности итогового абсолютного адреса.
        return Uri.TryCreate(url, UriKind.Absolute, out var uri)
            ? uri.ToString()
            : url;
    }

    /// <inheritdoc />
    public async Task<ConfigUpdateCheckResult> CheckForUpdatesAsync(
        string configName, string currentVersion, string url, CancellationToken ct = default)
    {
        var result = new ConfigUpdateCheckResult
        {
            ConfigName = configName ?? string.Empty,
            CurrentVersion = currentVersion ?? string.Empty,
            Url = url ?? string.Empty,
        };

        if (string.IsNullOrWhiteSpace(url))
        {
            result.Status = ConfigUpdateStatus.Failed;
            result.Error = "Updates.NoUrl";
            return result;
        }

        try
        {
            using var response = await HttpClient.GetAsync(url, ct).ConfigureAwait(false);

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                // Каталог не существует — недоступно / неверная ссылка.
                result.Status = ConfigUpdateStatus.Failed;
                result.Error = "Updates.NotFound";
                return result;
            }

            if (!response.IsSuccessStatusCode)
            {
                result.Status = ConfigUpdateStatus.Failed;
                result.Error = $"HTTP {(int)response.StatusCode}";
                return result;
            }

            var html = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            var latest = ParseLatestVersion(html);
            result.LatestVersion = latest;

            if (string.IsNullOrWhiteSpace(latest))
            {
                // Каталог доступен, но точную последнюю версию распарсить не удалось.
                result.Status = ConfigUpdateStatus.Unavailable;
                return result;
            }

            result.Status = IsNewer(latest, result.CurrentVersion)
                ? ConfigUpdateStatus.NewerAvailable
                : ConfigUpdateStatus.UpToDate;
            return result;
        }
        catch (OperationCanceledException)
        {
            result.Status = ConfigUpdateStatus.Failed;
            result.Error = "Updates.Cancelled";
            return result;
        }
        catch
        {
            // Ошибка сети/парсинга — не роняем UI, честно сообщаем.
            result.Status = ConfigUpdateStatus.Failed;
            result.Error = "Updates.NetworkError";
            return result;
        }
    }

    /// <summary>
    /// Устойчиво ищет в HTML-странице каталога ссылки на архивы дистрибутивов вида
    /// <c>*setup*.zip</c> и возвращает максимальную версию, извлечённую из имён файлов.
    /// При невозможности распарсить ни одну версию возвращает пустую строку.
    /// </summary>
    private static string ParseLatestVersion(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return string.Empty;

        Version? best = null;
        string bestText = string.Empty;

        foreach (Match match in ArchiveLinkRegex.Matches(html))
        {
            var fileName = match.Groups["url"].Value;
            var version = ExtractVersionFromFileName(fileName);
            if (string.IsNullOrWhiteSpace(version))
                continue;

            if (!TryParseVersion(version, out var parsed))
                continue;

            if (best is null || parsed > best)
            {
                best = parsed;
                bestText = version;
            }
        }

        return bestText;
    }

    /// <summary>Извлекает подстроку версии из имени файла дистрибутива (устойчиво к префиксам).</summary>
    private static string ExtractVersionFromFileName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return string.Empty;

        var start = -1;
        for (var i = 0; i < fileName.Length; i++)
        {
            if (char.IsDigit(fileName[i]))
            {
                start = i;
                break;
            }
        }

        if (start < 0)
            return string.Empty;

        var end = fileName.Length;
        for (var i = start; i < fileName.Length; i++)
        {
            var c = fileName[i];
            if (c is ' ' or '_' or '-' or '+' or '\\' or '/')
            {
                end = i;
                break;
            }
        }

        var candidate = fileName.Substring(start, end - start);
        // Версия обычно выглядит как «3.0.142.32» — отбрасываем хвост без точек.
        if (!candidate.Contains('.'))
            return string.Empty;
        return candidate;
    }

    /// <summary>
    /// Разбирает строку как 3–5-частную версию <see cref="Version"/>. При невозможности
    /// распарсить возвращает false. Хвостовые суффиксы после «+» обрезаются.
    /// </summary>
    internal static bool TryParseVersion(string? text, out Version version)
    {
        version = new Version(0, 0, 0, 0);
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var value = text.Trim();
        var plus = value.IndexOf('+');
        if (plus >= 0)
            value = value.Substring(0, plus);

        if (!Version.TryParse(value, out var parsed) || parsed is null)
            return false;

        // Version поддерживает до 4 частей; приводим к 4-частному виду для корректного сравнения.
        version = Normalize(parsed);
        return true;
    }

    private static Version Normalize(Version v)
    {
        var major = Math.Max(v.Major, 0);
        var minor = v.Minor < 0 ? 0 : v.Minor;
        var build = v.Build < 0 ? 0 : v.Build;
        var revision = v.Revision < 0 ? 0 : v.Revision;
        return new Version(major, minor, build, revision);
    }

    /// <summary>True, если <paramref name="latestVersion"/> новее <paramref name="currentVersion"/>.</summary>
    internal static bool IsNewer(string? latestVersion, string? currentVersion)
    {
        if (!TryParseVersion(latestVersion, out var latest))
            return false;
        // Пустая текущая версия считается «ниже» любой известной последней.
        if (!TryParseVersion(currentVersion, out var current))
            return true;
        return latest > current;
    }

    /// <inheritdoc />
    public async Task<string?> DownloadUpdateAsync(
        string url, string targetPath, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(url))
            return null;

        try
        {
            var dir = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            using var response = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
                return null;

            var totalBytes = response.Content.Headers.ContentLength ?? -1L;

            await using (var contentStream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
            await using (var fileStream = new FileStream(targetPath, FileMode.Create, FileAccess.Write,
                             FileShare.None, 81920, useAsync: true))
            {
                var buffer = new byte[81920];
                long readTotal = 0;
                int read;
                while ((read = await contentStream.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
                {
                    await fileStream.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                    readTotal += read;
                    if (totalBytes > 0 && progress is not null)
                        progress.Report(Math.Min(1.0, (double)readTotal / totalBytes));
                }
            }

            return File.Exists(targetPath) ? targetPath : null;
        }
        catch (OperationCanceledException)
        {
            TryDelete(targetPath);
            return null;
        }
        catch
        {
            TryDelete(targetPath);
            return null;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // Игнорируем ошибки удаления временного файла.
        }
    }
}