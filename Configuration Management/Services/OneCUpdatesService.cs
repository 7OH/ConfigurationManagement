using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
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
    /// <summary>Базовый адрес web-ресурса обновлений 1С (сегмент <c>1cbsl</c> для типовых решений).
    /// Используется как устаревший механизм формирования адреса, когда ник конфигурации не задан.</summary>
    public const string DefaultBaseUrl = "https://downloads.1c.ru/ipp/1cbsl/";

    /// <summary>Базовый адрес ресурса обновлений 1С в формате <c>version_files?nick=…&ver=…</c>.</summary>
    public const string ReleasesBaseUrl = "https://releases.1c.ru/version_files";

    /// <summary>Базовый адрес HTML-списка версий конфигурации по нику: <c>project/<nick></c>.</summary>
    public const string ReleasesProjectBaseUrl = "https://releases.1c.ru/project";

    private const int TimeoutSeconds = 15;

    /// <summary>Максимальное число переходов при ручном следовании редиректам (защита от зацикливания).</summary>
    private const int MaxRedirects = 10;

    /// <summary>
    /// Адрес формы входа на портал 1С (сервис «1С:Обновление программ»). Ресурс
    /// releases.1c.ru при отсутствии сессии перенаправляет сюда (Spring Security CAS):
    /// сначала нужно GET'ом получить страницу с формой и скрытым токеном <c>execution</c>,
    /// затем POST'ом отправить логин/пароль вместе с этим токеном. После успешного входа
    /// сервер выставляет сессионные cookie, которые сохраняются в <see cref="CookieContainer"/>.
    /// </summary>
    private const string PortalLoginUrl = "https://login.1c.ru/login";

    private static readonly HttpClient HttpClient = CreateHttpClient();

    private readonly IInfobaseRepository _repository;
    private readonly IAppLogger _logger;

    /// <summary>True — попытка программного входа на portal.1c.ru уже выполнялась (не более одного
    /// раза за сессию службы; сессионные cookie хранятся в <see cref="CookieContainer"/> клиента).</summary>
    private bool _portalLoginAttempted;

    /// <summary>
    /// Создаёт экземпляр службы. <paramref name="repository"/> (singleton) используется для
    /// чтения настроек логина/пароля авторизации на сайте 1С на каждый сетевой запрос;
    /// <paramref name="logger"/> — для диагностики сетевых ошибок проверки обновлений.
    /// </summary>
    public OneCUpdatesService(IInfobaseRepository repository, IAppLogger logger)
    {
        _repository = repository;
        _logger = logger;
    }

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
            // Перенаправления обрабатываем вручную (см. SendWithAuthAsync): автоперенаправление
            // .NET снимает заголовок Authorization при переходе на другой хост (CDN), из-за чего
            // Basic Auth теряется. Поэтому AllowAutoRedirect=false, а редиректы следуем сами,
            // заново добавляя заголовок на каждом шаге.
            AllowAutoRedirect = false,
            // Хранилище session-cookie для гибридной авторизации (вход на portal.1c.ru):
            // после успешного входа cookie автоматически добавляются к последующим запросам.
            CookieContainer = new CookieContainer(),
        })
        {
            Timeout = TimeSpan.FromSeconds(TimeoutSeconds),
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "ConfigurationManagement/0.3.9.3 (+https://github.com/sivatorov/ConfigurationManagement)");
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

        // Ресурс releases.1c.ru/project/<nick> — HTML-список версий; последняя (самая новая) версия
        // находится в первой строке таблицы #versionsTable. Версия определяется при проверке.
        if (!string.IsNullOrWhiteSpace(config.Nick))
        {
            var nickUrl = $"{ReleasesProjectBaseUrl}/{Uri.EscapeDataString(config.Nick.Trim())}";
            return Uri.TryCreate(nickUrl, UriKind.Absolute, out var nickUri)
                ? nickUri.ToString()
                : nickUrl;
        }

        // Ник не задан — корректный URL построить невозможно (старый сегментный путь
        // downloads.1c.ru/ipp/.../Configs/... более не работает и даёт 404). Возвращаем пустую
        // строку, чтобы проверка честно завершилась со статусом Failed.
        return string.Empty;
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
            _logger.Warn($"[Updates] Пустой URL (config='{configName}') — проверка не выполнялась.");
            result.Status = ConfigUpdateStatus.Failed;
            result.Error = "Updates.NoUrl";
            return result;
        }

        _logger.Info($"[Updates] Проверка: config='{configName}', url={url}");

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            using var response = await SendWithAuthAsync(request, HttpCompletionOption.ResponseContentRead, ct).ConfigureAwait(false);

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                // Каталог не существует — недоступно / неверная ссылка.
                _logger.Warn($"[Updates] Каталог не найден (404): {url}");
                result.Status = ConfigUpdateStatus.Failed;
                result.Error = "Updates.NotFound";
                return result;
            }

            if (!response.IsSuccessStatusCode)
            {
                // Диагностика: фиксируем реальный код ответа сервера (401/403 — нет доступа,
                // 5xx — проблемы на стороне 1С) и конечный URI после возможных редиректов.
                _logger.Warn($"[Updates] HTTP {(int)response.StatusCode} для '{url}' (requestUri={request.RequestUri})");
                result.Status = ConfigUpdateStatus.Failed;
                result.Error = $"HTTP {(int)response.StatusCode}";
                return result;
            }

            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            // Формат ответа определяется по URL:
            //   - project/<nick>            → HTML-список версий (#versionsTable), парсим из него;
            //   - version_files?nick=&ver=  → JSON/HTML со списком файлов релиза (прежний парсер);
            //   - прочее                    → устаревший HTML-каталог (ссылки на архивы).
            var isProject = url.Contains("/project/", StringComparison.OrdinalIgnoreCase);
            var isVersionFiles = url.Contains("version_files", StringComparison.OrdinalIgnoreCase);
            var latest = isProject
                ? ParseLatestVersionFromProjectHtml(body)
                : isVersionFiles
                    ? ParseLatestVersionFromJson(body)
                    : ParseLatestVersion(body);
            result.LatestVersion = latest;

            if (string.IsNullOrWhiteSpace(latest))
            {
                // Каталог доступен, но точную последнюю версию распарсить не удалось.
                _logger.Warn($"[Updates] Каталог доступен, но версия не распарсена (len={body.Length}): {url}");
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
        catch (Exception ex)
        {
            // Диагностика: фиксируем точный тип/сообщение исключения, чтобы отличить таймаут,
            // ошибку построения URI при редиректе, DNS/TLS и т.п. UI не роняем — возвращаем Failed.
            _logger.Error($"[Updates] Ошибка сети/парсинга для '{url}': {ex.GetType().Name}: {ex.Message}", ex);
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

    /// <summary>Регулярное выражение для ссылки на страницу файлов релиза вида
    /// <c>/version_files?nick=…&ver=…</c> (текст ссылки — номер версии).</summary>
    private static readonly Regex VersionFilesLinkRegex =
        new(@"href\s*=\s*[""'][^""']*version_files[^""']*ver\s*=[^""']*[""'][^>]*>\s*(?<ver>[^<]+?)</a>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);

    /// <summary>
    /// Извлекает последнюю (самую новую) версию из HTML-страницы <c>releases.1c.ru/project/<nick></c>.
    /// Ищет таблицу <c>id="versionsTable"</c> и в её первой строке <c><tr></c> первый элемент
    /// <c><a href="/version_files?nick=…&ver=…">ВЕРСИЯ</a></c>. Устойчив к пробелам и
    /// переносам строк. Если таблица не найдена — как запасной вариант ищет первую ссылку
    /// <c>version_files?...&ver=</c> во всём HTML. Возвращает найденную версию или пустую строку.
    /// </summary>
    private static string ParseLatestVersionFromProjectHtml(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return string.Empty;

        // 1) Основной путь: таблица #versionsTable, первая строка <tr>, первая ссылка version_files.
        var tableMatch = Regex.Match(html,
            @"<table[^>]*id\s*=\s*[""']versionsTable[""'][^>]*>(?<table>.*?)</table>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (tableMatch.Success)
        {
            var table = tableMatch.Groups["table"].Value;
            var firstRow = Regex.Match(table, @"<tr[^>]*>(?<row>.*?)</tr>",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (firstRow.Success)
            {
                var row = firstRow.Groups["row"].Value;
                var link = VersionFilesLinkRegex.Match(row);
                if (link.Success)
                {
                    var ver = WebUtility.HtmlDecode(link.Groups["ver"].Value.Trim());
                    if (!string.IsNullOrWhiteSpace(ver))
                        return ver;
                }
            }
        }

        // 2) Запасной путь: регресс к поиску первой ссылки version_files во всём HTML.
        var fallback = VersionFilesLinkRegex.Match(html);
        if (fallback.Success)
        {
            var ver = WebUtility.HtmlDecode(fallback.Groups["ver"].Value.Trim());
            if (!string.IsNullOrWhiteSpace(ver))
                return ver;
        }

        return string.Empty;
    }

    /// <summary>Регулярное выражение для извлечения 3–4-частных номеров версий из текста (JSON и пр.).</summary>
    private static readonly Regex VersionTokenRegex =
        new(@"\b(?<v>\d{1,4}(\.\d{1,4}){1,3})\b", RegexOptions.Compiled);

    /// <summary>
    /// Извлекает из JSON-ответа ресурса <c>releases.1c.ru/version_files</c> все кандидаты версий
    /// и возвращает максимальную. Точная схема ответа неизвестна, поэтому поиск ведётся по числовым
    /// токенам вида <c>3.0.206.19</c> (устойчиво к структуре JSON и изменению полей). При невозможности
    /// распарсить ни одну версию возвращает пустую строку.
    /// </summary>
    private static string ParseLatestVersionFromJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return string.Empty;

        Version? best = null;
        string bestText = string.Empty;
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (Match match in VersionTokenRegex.Matches(json))
        {
            var candidate = match.Groups["v"].Value;
            if (!seen.Add(candidate))
                continue;
            if (!TryParseVersion(candidate, out var parsed))
                continue;

            if (best is null || parsed > best)
            {
                best = parsed;
                bestText = candidate;
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

            // Если URL указывает на страницу version_files — он возвращает JSON со списком файлов
            // релиза, а не сам архив. Получаем список, выбираем дистрибутив (setup*.zip → *.zip →
            // 1cv8.cf) и скачиваем уже прямую ссылку. Если URL — прямая ссылка на файл — качаем как есть.
            var isVersionFiles = url.Contains("version_files", StringComparison.OrdinalIgnoreCase);
            if (isVersionFiles)
            {
                var listing = await GetTextAsync(url, ct).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(listing))
                {
                    _logger.Warn($"[Updates] Не удалось получить список файлов релиза: {url}");
                    return null;
                }

                var direct = SelectDistributionUrl(listing);
                if (string.IsNullOrWhiteSpace(direct))
                {
                    _logger.Warn($"[Updates] В списке файлов релиза не найден дистрибутив: {url}");
                    return null;
                }

                url = ResolveUrl(url, direct);
            }

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            using var response = await SendWithAuthAsync(request, HttpCompletionOption.ResponseHeadersRead, ct)
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

    /// <summary>Регулярное выражение для поиска прямых ссылок на дистрибутивы
    /// (<c>*.zip</c>, <c>*.cf</c>) в ответе списка файлов релиза.</summary>
    private static readonly Regex DistributionUrlRegex = new(
        @"(?<url>(?:https?://|/)[^""'\s<>]*?\.(?:zip|cf)(?:[?#][^""'\s<>]*)?)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Выбирает прямую ссылку на дистрибутив из ответа (JSON/HTML) списка файлов релиза
    /// <c>version_files</c>. Приоритет: <c>setup*.zip</c> → полный <c>*.zip</c> → <c>1cv8.cf</c>.
    /// Поиск ведётся регулярными выражениями, устойчивыми к неизвестной структуре ответа.
    /// При отсутствии подходящих ссылок возвращает null.
    /// </summary>
    private static string? SelectDistributionUrl(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return null;

        string? setupZip = null;
        string? fullZip = null;
        string? cf = null;

        foreach (Match m in DistributionUrlRegex.Matches(body))
        {
            var raw = m.Groups["url"].Value.Trim().Trim('"', '\'', '\\');
            if (string.IsNullOrWhiteSpace(raw))
                continue;

            var lower = raw.ToLowerInvariant();
            var isZip = lower.EndsWith(".zip") || lower.Contains(".zip?") || lower.Contains(".zip#");
            var isCf = lower.EndsWith(".cf") || lower.Contains(".cf?");
            if (isZip && lower.Contains("setup"))
                setupZip ??= raw;
            else if (isZip)
                fullZip ??= raw;
            else if (isCf)
                cf ??= raw;
        }

        return setupZip ?? fullZip ?? cf;
    }

    /// <summary>Преобразует относительную ссылку из списка файлов релиза в абсолютную
    /// относительно <paramref name="baseUrl"/>. Абсолютные ссылки возвращаются без изменений.</summary>
    private static string ResolveUrl(string baseUrl, string direct)
    {
        if (direct.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            direct.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return direct;

        try
        {
            return new Uri(new Uri(baseUrl), direct).ToString();
        }
        catch
        {
            return direct;
        }
    }

    /// <summary>Выполняет GET и возвращает тело ответа как строку; при сетевой ошибке или
    /// не-успешном статусе возвращает пустую строку.</summary>
    private async Task<string> GetTextAsync(string url, CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            using var response = await SendWithAuthAsync(request, HttpCompletionOption.ResponseContentRead, ct)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return string.Empty;
            return await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        }
        catch
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// Отправляет запрос с HTTP Basic Auth и вручную следует перенаправлениям (до
    /// <see cref="MaxRedirects"/> шагов), заново добавляя заголовок Authorization на каждом
    /// переходе. Необходимо, потому что автоперенаправление <c>HttpClientHandler</c> снимает
    /// заголовок Authorization при переходе на другой хост (например, CDN дистрибутивов 1С),
    /// из-за чего авторизованный запрос после редиректа выполнялся бы без учётных данных.
    /// </summary>
    private async Task<HttpResponseMessage> SendWithAuthAsync(
        HttpRequestMessage request,
        HttpCompletionOption completionOption = HttpCompletionOption.ResponseContentRead,
        CancellationToken ct = default)
    {
        var current = request;
        for (var i = 0; i <= MaxRedirects; i++)
        {
            AddBasicAuth(current);

            var response = await HttpClient.SendAsync(current, completionOption, ct).ConfigureAwait(false);

            var status = (int)response.StatusCode;

            // Гибридная авторизация. Ресурс releases.1c.ru при отсутствии сессии НЕ выдаёт 401,
            // а перенаправляет на login.1c.ru (Spring Security CAS). Поэтому вход запускаем при:
            //   - HTTP 401/403 (возможен Basic-вариант), либо
            //   - редиректе на login.1c.ru (нужна cookie-сессия).
            // Один раз за сессию службы; при успехе повторяем исходный запрос с сохранёнными cookie.
            var needsLogin =
                (status is 401 or 403) ||
                (response.Headers.Location?.Host.Contains("login.1c.ru", StringComparison.OrdinalIgnoreCase) == true);

            if (needsLogin && !_portalLoginAttempted)
            {
                _portalLoginAttempted = true;
                var loggedIn = await TryLoginPortalAsync(ct).ConfigureAwait(false);
                if (loggedIn)
                {
                    response.Dispose();
                    var rebuilt = new HttpRequestMessage(current.Method, current.RequestUri!);
                    current.Dispose();
                    current = rebuilt;
                    continue;
                }
                // Вход не удался — продолжаем обычную обработку редиректа/ответа ниже.
            }

            if (status is < 300 or >= 400 || response.Headers.Location is null)
                return response;

            // Перенаправление: освобождаем ответ и строим следующий запрос по Location.
            var location = response.Headers.Location;
            response.Dispose();

            var target = location.IsAbsoluteUri
                ? location
                : new Uri(current.RequestUri!, location);
            var next = new HttpRequestMessage(current.Method, target);

            // Переносим только заголовок Authorization (Basic Auth); прочие приватные заголовки
            // для нового хоста берутся из дефолтов клиента (User-Agent, Accept).
            if (current.Headers.Authorization is not null)
                next.Headers.Authorization = current.Headers.Authorization;

            current = next;
        }

        throw new InvalidOperationException("Слишком много перенаправлений при проверке обновлений.");
    }

    /// <summary>
    /// Добавляет HTTP Basic Auth заголовок (<c>Authorization: Basic base64(логин:пароль)</c>)
    /// на основе настроек <see cref="AppSettings.UpdatesLogin"/>/<see cref="AppSettings.UpdatesPassword"/>.
    /// Если логин не задан — запрос выполняется без авторизации (обратная совместимость).
    /// Настройки читаются на каждый запрос, поэтому смена учётных данных не требует перезапуска.
    /// Заголовок Authorization и пароль не логируются.
    /// </summary>
    private void AddBasicAuth(HttpRequestMessage request)
    {
        var settings = _repository.LoadSettings();
        var login = settings.UpdatesLogin ?? string.Empty;
        if (string.IsNullOrEmpty(login))
            return;

        var password = settings.UpdatesPassword ?? string.Empty;
        var token = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{login}:{password}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", token);
    }

    /// <summary>
    /// Программный вход на portal.1c.ru (гибридная авторизация). Точные адрес формы входа и имена
    /// полей неизвестны наверняка, поэтому перебираются несколько типовых комбинаций; поток подлежит
    /// корректировке после реального теста. Сессионные cookie сохраняются в <see cref="CookieContainer"/>
    /// общего <see cref="HttpClient"/>, поэтому последующие запросы проходят авторизацию автоматически.
    /// Возвращает true, если хотя бы один вариант завершился успешно (2xx или редирект вне страницы входа).
    /// </summary>
    private async Task<bool> TryLoginPortalAsync(CancellationToken ct)
    {
        var settings = _repository.LoadSettings();
        var login = settings.UpdatesLogin ?? string.Empty;
        var password = settings.UpdatesPassword ?? string.Empty;
        if (string.IsNullOrEmpty(login))
        {
            _logger.Warn("[Updates] Для входа на portal.1c.ru не задан логин.");
            return false;
        }

        try
        {
            // Шаг 1: GET формы входа — получаем HTML и скрытый токен Spring Security CAS «execution».
            _logger.Info($"[Updates] Запрашиваю форму входа: {PortalLoginUrl}");
            using (var formRequest = new HttpRequestMessage(HttpMethod.Get, PortalLoginUrl))
            using (var formResponse =
                   await HttpClient.SendAsync(formRequest, HttpCompletionOption.ResponseContentRead, ct).ConfigureAwait(false))
            {
                var html = await formResponse.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                var execution = ExtractFormExecution(html);
                if (string.IsNullOrWhiteSpace(execution))
                {
                    _logger.Warn("[Updates] Не удалось извлечь токен 'execution' из формы входа.");
                    return false;
                }

                // Шаг 2: POST /login с логином/паролем и токеном. Поля соответствуют реальной
                // форме login.1c.ru (username, password, execution, _eventId=submit, rememberMe,
                // anotherComputer, geolocation, inviteCode, inviteType).
                var form = new Dictionary<string, string>
                {
                    ["username"] = login,
                    ["password"] = password,
                    ["execution"] = execution,
                    ["_eventId"] = "submit",
                    ["rememberMe"] = "on",
                    ["anotherComputer"] = string.Empty,
                    ["geolocation"] = string.Empty,
                    ["inviteCode"] = string.Empty,
                    ["inviteType"] = string.Empty,
                };

                using var postRequest = new HttpRequestMessage(HttpMethod.Post, PortalLoginUrl);
                postRequest.Content = new FormUrlEncodedContent(form);
                postRequest.Content.Headers.ContentType =
                    new MediaTypeHeaderValue("application/x-www-form-urlencoded");

                using var postResponse =
                    await HttpClient.SendAsync(postRequest, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);

                var postStatus = (int)postResponse.StatusCode;
                var location = postResponse.Headers.Location?.ToString() ?? string.Empty;

                // Успех: 2xx, либо редирект вне страницы входа. При неудачном логине форма обычно
                // возвращается снова (200/302 обратно на login) и сессионных cookie нет.
                var success = postResponse.IsSuccessStatusCode ||
                              (postStatus is >= 300 and < 400 && !location.Contains("login", StringComparison.OrdinalIgnoreCase));

                if (success)
                {
                    _logger.Info($"[Updates] Вход на portal.1c.ru выполнен (status={postStatus}, location='{location}').");
                    return true;
                }

                _logger.Warn($"[Updates] Вход на portal.1c.ru не подтверждён (status={postStatus}, location='{location}').");
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.Warn($"[Updates] Ошибка входа на portal.1c.ru: {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    /// <summary>Извлекает значение скрытого поля <c>execution</c> из HTML-формы входа
    /// Spring Security CAS. При отсутствии поля возвращает пустую строку.</summary>
    private static string ExtractFormExecution(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return string.Empty;

        var m = Regex.Match(html,
            @"name=[""']execution[""'][^>]*value=[""'](?<value>[^""']*)[""']",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
        return m.Success ? m.Groups["value"].Value : string.Empty;
    }
}