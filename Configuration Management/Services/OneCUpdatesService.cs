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

    /// <summary>Таймаут одной попытки многопоточной загрузки (дольше основного — CDN дистрибутивов).</summary>
    private static readonly TimeSpan ParallelAttemptTimeout = TimeSpan.FromMinutes(20);

    /// <summary>Максимальное число переходов при ручном следовании редиректам (защита от зацикливания).</summary>
    private const int MaxRedirects = 10;

    /// <summary>User-Agent запросов к порталу и CDN дистрибутивов (единый для всех клиентов службы).
    /// Версия подставляется из сборки, чтобы сервер не считал клиент устаревшим (issue #334).</summary>
    private static readonly string UserAgent =
        $"ConfigurationManagement/{VersionInfo.Display()} (+https://github.com/sivatorov/ConfigurationManagement)";

    /// <summary>
    /// Адрес формы входа на портал 1С (сервис «1С:Обновление программ»). Ресурс
    /// releases.1c.ru при отсутствии сессии перенаправляет сюда (Spring Security CAS):
    /// сначала нужно GET'ом получить страницу с формой и скрытым токеном <c>execution</c>,
    /// затем POST'ом отправить логин/пароль вместе с этим токеном. После успешного входа
    /// сервер выставляет сессионные cookie, которые сохраняются в <see cref="CookieContainer"/>.
    /// </summary>
    private const string PortalLoginUrl = "https://login.1c.ru/login";

    /// <summary>Хранилище session-cookie гибридной авторизации портала 1С: общее для основного
    /// клиента и клиента многопоточной загрузки (после входа cookie попадают в оба).</summary>
    private readonly CookieContainer _cookieContainer = new();

    /// <summary>Версия HTTP для запросов к форме входа portal.1c.ru: часть Spring Security CAS
    /// некорректно обрабатывает HTTP/2 (ответ 401 вместо формы/редиректа), поэтому вход
    /// выполняется принудительно по HTTP/1.1 (issue #334).</summary>
    private static readonly Version LoginHttpVersion = HttpVersion.Version11;

    /// <summary>Максимальное число попыток программного входа на portal.1c.ru за сессию службы
    /// (защита от анти-брутфорс блокировки портала; счётчик сбрасывается при смене учётной
    /// записи, при успешном входе и автоматически через <see cref="LoginLimitCooldown"/>,
    /// см. <see cref="CanAttemptPortalLogin"/>).</summary>
    private const int MaxPortalLoginAttempts = 3;

    /// <summary>Период автосброса лимита попыток входа на portal.1c.ru: после исчерпания
    /// лимита (<see cref="MaxPortalLoginAttempts"/>) новая попытка входа разрешается не ранее
    /// чем через этот интервал (анти-брутфорс портала; issue #334/#330/#323).</summary>
    private static readonly TimeSpan LoginLimitCooldown = TimeSpan.FromMinutes(10);

    /// <summary>HTTP-обработчик, инжектируемый в тестах (fake вместо реальной сети); null — реальный стек.</summary>
    private readonly HttpMessageHandler? _handlerOverride;

    private readonly HttpClient _httpClient;

    private readonly IInfobaseRepository _repository;
    private readonly IItsAccountsStore? _itsAccounts;
    private readonly IAppLogger _logger;

    /// <summary>Число выполненных попыток входа на portal.1c.ru (не более
    /// <see cref="MaxPortalLoginAttempts"/> за сессию службы; сессионные cookie хранятся
    /// в <see cref="CookieContainer"/> клиента). В отличие от прежнего «одноразового» флага
    /// позволяет повторять вход для каждого нового окна/операции (issue #330/#323: одна ошибка
    /// входа не должна «отравлять» всю сессию).</summary>
    private int _portalLoginAttempts;

    /// <summary>Сигнатура учётной записи последней попытки входа (без пароля): при её смене
    /// счётчик <see cref="_portalLoginAttempts"/> сбрасывается.</summary>
    private string? _lastAttemptAccountSignature;

    /// <summary>Результат последней попытки входа на portal.1c.ru (для различения
    /// AuthRequired / AuthFailed в результатах проверок).</summary>
    private PortalLoginResult _lastLoginResult = PortalLoginResult.NoCredentials;

    /// <summary>Момент исчерпания лимита попыток входа (для автосброса по
    /// <see cref="LoginLimitCooldown"/>); default — лимит не исчерпан.</summary>
    private DateTime _limitReachedAt;

    /// <summary>Источник текущего времени для автосброса лимита попыток входа
    /// (в проде — <see cref="DateTime.UtcNow"/>; в тестах подменяется фиктивными часами,
    /// чтобы проверить повторную попытку после <see cref="LoginLimitCooldown"/>).</summary>
    internal Func<DateTime> UtcNowProvider = () => DateTime.UtcNow;

    /// <summary>
    /// Создаёт экземпляр службы. <paramref name="repository"/> (singleton) используется для
    /// чтения настроек (выбор учётной записи ИТС) на каждый сетевой запрос;
    /// <paramref name="logger"/> — для диагностики сетевых ошибок проверки обновлений.
    /// Учётные данные берутся из справочника <see cref="IItsAccountsStore"/> (issue #333):
    /// выбранная в настройках запись или «Основная». Без хранилища (конструктор без
    /// параметра) используются только устаревшие поля настроек
    /// <see cref="AppSettings.UpdatesLogin"/>/<see cref="AppSettings.UpdatesPassword"/> —
    /// обратная совместимость для прямых созданий в тестах.
    /// </summary>
    public OneCUpdatesService(IInfobaseRepository repository, IAppLogger logger)
        : this(repository, logger, handler: null, itsAccounts: null)
    {
    }

    /// <summary>
    /// Основной конструктор для DI: помимо репозитория и журнала внедряет хранилище
    /// учётных записей ИТС <see cref="IItsAccountsStore"/> (issue #333). Без него креды
    /// из справочника <c>its_accounts.json</c> не доходили бы до <see cref="GetCredentials"/>
    /// (вход на portal.1c.ru сообщал «не задан логин» — issue #334).
    /// </summary>
    public OneCUpdatesService(IInfobaseRepository repository, IAppLogger logger, IItsAccountsStore itsAccounts)
        : this(repository, logger, handler: null, itsAccounts: itsAccounts)
    {
    }

    /// <summary>
    /// Конструктор с инжектируемым HTTP-обработчиком (для тестов): весь сетевой стек
    /// службы — основной клиент и клиент многопоточной загрузки — работает через
    /// fake-обработчик, реальная сеть не используется.
    /// </summary>
    internal OneCUpdatesService(
        IInfobaseRepository repository,
        IAppLogger logger,
        HttpMessageHandler? handler,
        IItsAccountsStore? itsAccounts = null)
    {
        _repository = repository;
        _itsAccounts = itsAccounts;
        _logger = logger;
        _handlerOverride = handler;
        _httpClient = CreateHttpClient(handler);
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

    private HttpClient CreateHttpClient(HttpMessageHandler? handler)
    {
        var client = new HttpClient(handler ?? new HttpClientHandler
        {
            // Перенаправления обрабатываем вручную (см. SendWithAuthAsync): автоперенаправление
            // .NET снимает заголовок Authorization при переходе на другой хост (CDN), из-за чего
            // Basic Auth теряется. Поэтому AllowAutoRedirect=false, а редиректы следуем сами,
            // заново добавляя заголовок на каждом шаге.
            AllowAutoRedirect = false,
            // Хранилище session-cookie для гибридной авторизации (вход на portal.1c.ru):
            // после успешного входа cookie автоматически добавляются к последующим запросам.
            CookieContainer = _cookieContainer,
        })
        {
            Timeout = TimeSpan.FromSeconds(TimeoutSeconds),
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        client.DefaultRequestHeaders.Accept.ParseAdd("text/html,application/xhtml+xml,*/*;q=0.8");
        return client;
    }

    /// <inheritdoc />
    public string BuildUpdateUrl(OneCConfigType? config, OneCConfigEdition? edition, string? urlOverride,
        string? urlSegment = null)
    {
        if (!string.IsNullOrWhiteSpace(urlOverride))
            return urlOverride.Trim();

        if (config is null)
            return string.Empty;

        // Если у редакции есть собственная переопределённая ссылка — используем её.
        if (edition is { HasUrlOverride: true })
            return edition.UrlOverride.Trim();

        // Персональный сегмент (ник) базы приоритетнее ника типовой конфигурации (issue #322):
        // пользователь видит и правит ключевой кусочек адреса после releases.1c.ru/project/
        // в окне «Связать с конфигурацией», не меняя общую карточку конфигурации.
        var nick = !string.IsNullOrWhiteSpace(urlSegment) ? urlSegment.Trim() : config.Nick;
        return BuildNickUrl(nick);
    }

    /// <summary>
    /// Строит URL каталога релизов ресурса <c>releases.1c.ru/project/<nick></c> — HTML-список
    /// версий; последняя (самая новая) версия находится в первой строке таблицы #versionsTable.
    /// Если ник не задан — корректный URL построить невозможно (старый сегментный путь
    /// <c>downloads.1c.ru/ipp/.../Configs/...</c> более не работает и даёт 404): возвращается
    /// пустая строка, чтобы проверка честно завершилась со статусом Failed.
    /// </summary>
    internal static string BuildNickUrl(string? nick)
    {
        if (string.IsNullOrWhiteSpace(nick))
            return string.Empty;

        // Экранируем явно и возвращаем экранированную строку: Uri.ToString() «разворачивает»
        // %XX-последовательности обратно в читаемые символы, что ломало бы URL с кириллицей
        // или пробелами в нике. Латиница/цифры (AccountingCorp30) EscapeDataString не меняет.
        var nickUrl = $"{ReleasesProjectBaseUrl}/{Uri.EscapeDataString(nick.Trim())}";
        return Uri.TryCreate(nickUrl, UriKind.Absolute, out _) ? nickUrl : string.Empty;
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

            // Пользователь мог попасть на страницу входа portal.1c.ru/login: ресурс releases.1c.ru
            // при отсутствии сессии перенаправляет туда, а программный вход не удался (нет логина
            // в настройках или неверные учётные данные). Показываем понятную ошибку авторизации,
            // а не «каталог доступен, но версия не распарсена».
            var authFailed = _lastLoginResult is PortalLoginResult.AuthFailed or PortalLoginResult.RedirectFailed;
            var isLoginRedirect =
                response.Headers.Location?.Host.Contains("login.1c.ru", StringComparison.OrdinalIgnoreCase) == true ||
                response.RequestMessage?.RequestUri?.Host.Contains("login.1c.ru", StringComparison.OrdinalIgnoreCase) == true;
            if (isLoginRedirect)
            {
                _logger.Warn($"[Updates] Требуется вход на portal.1c.ru (запрос ушёл на {response.RequestMessage?.RequestUri}) для '{url}'");
                result.Status = ConfigUpdateStatus.Failed;
                // Лимит попыток входа исчерпан — отдельная понятная ошибка с советом
                // (issue #334/#330/#323); вход предпринимался и не подтверждён сервером (401) —
                // «не подтверждён» (issue #334); иначе — «требуется вход».
                result.Error = AuthErrorKey(authFailed);
                return result;
            }

            if (!response.IsSuccessStatusCode)
            {
                // Диагностика: фиксируем реальный код ответа сервера (401/403 — нет доступа,
                // 5xx — проблемы на стороне 1С) и конечный URI после возможных редиректов.
                var code = (int)response.StatusCode;
                _logger.Warn($"[Updates] HTTP {code} для '{url}' (requestUri={request.RequestUri})");
                result.Status = ConfigUpdateStatus.Failed;
                // 401/403 и редиректы 3xx (в т.ч. 302 без Location от CAS releases.1c.ru) —
                // понятная ошибка авторизации (неверный/пустой логин-пароль сайта 1С, issue #323);
                // лимит попыток исчерпан — отдельный ключ; если вход предпринимался и не
                // подтверждён — «вход не подтверждён (401)»; остальные коды — техническая диагностика.
                result.Error = code is 401 or 403 or (>= 300 and < 400)
                    ? AuthErrorKey(authFailed)
                    : $"HTTP {code}";
                return result;
            }

            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            // Страница входа может прийти с HTTP 200: CAS возвращает форму (поля execution/lt)
            // вместо редиректа, когда сессия не установлена (фантомный успех, issue #330).
            // Распознаём по содержимому и показываем понятную ошибку авторизации, а не
            // «каталог доступен, но версия не распарсена» (issue #323/#334).
            if (LooksLikeLoginForm(body))
            {
                _logger.Warn($"[Updates] Получена страница входа вместо содержимого каталога ({url}).");
                result.Status = ConfigUpdateStatus.Failed;
                result.Error = AuthErrorKey(authFailed);
                return result;
            }

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
    internal static string ParseLatestVersion(string html)
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
    internal static string ParseLatestVersionFromProjectHtml(string html)
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

    /// <summary>
    /// Извлекает подстроку версии из имени файла дистрибутива (устойчиво к префиксам).
    /// Пропускает цифровые группы без точек («1c», «setup_2…», префиксы) и возвращает
    /// первую группу вида «3.0.13.7» (суффиксы «.zip»/«.rar» отбрасываются).
    /// </summary>
    private static string ExtractVersionFromFileName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return string.Empty;

        var i = 0;
        while (i < fileName.Length)
        {
            while (i < fileName.Length && !char.IsDigit(fileName[i]))
                i++;
            if (i >= fileName.Length)
                break;

            var start = i;
            var end = fileName.Length;
            for (var j = start; j < fileName.Length; j++)
            {
                var c = fileName[j];
                if (c is ' ' or '_' or '-' or '+' or '\\' or '/')
                {
                    end = j;
                    break;
                }
            }

            var candidate = fileName.Substring(start, end - start);
            // Отбрасываем хвостовые суффиксы вида «.zip»/«.rar»/«setup» и прочие нечисловые хвосты:
            // версия обычно выглядит как «3.0.142.32», и суффикс не должен мешать парсингу.
            var lastDot = candidate.LastIndexOf('.');
            if (lastDot > 0 && lastDot < candidate.Length - 1)
            {
                var tail = candidate.Substring(lastDot + 1);
                if (tail.Length > 0 && !char.IsDigit(tail[0]))
                    candidate = candidate.Substring(0, lastDot);
            }
            if (candidate.Contains('.'))
                return candidate;

            i = end;
        }

        return string.Empty;
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

    /// <inheritdoc />
    public async Task<string?> DownloadDistributionAsync(
        string url, string targetPath, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(targetPath))
            return null;

        try
        {
            var dir = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);

                // Проверка свободного места на этом этапе не блокирует загрузку — только
                // предупреждение в журнал; блокирующий вопрос пользователю — этап 0.3.9.214.
                LogFreeSpaceWarning(dir, expectedSizeBytes: 0);
            }

            _logger.Info($"[Platform] Загрузка дистрибутива: {url} -> {targetPath}");

            // 1) Многопоточная загрузка: отдельный HttpClient с авторизацией портала
            //    (Basic Auth + общий CookieContainer) и AllowAutoRedirect=true для CDN;
            //    клиент создаётся только на время операции и освобождается после неё.
            using (var parallelClient = CreateParallelClient())
            {
                var parallelResult = await ParallelDownloader.TryDownloadAsync(
                        parallelClient, url, targetPath,
                        percent => progress?.Report(Math.Clamp(percent / 100.0, 0.0, 1.0)),
                        expectedSize: 0, ct)
                    .ConfigureAwait(false);

                if (parallelResult is not null)
                {
                    _logger.Info($"[Platform] Дистрибутив загружен многопоточно: {targetPath}");
                    // Метка докачки для разового временного файла не нужна — очищаем.
                    TryDelete(targetPath + ".etag");
                    return parallelResult;
                }
            }

            // 2) Fallback: существующий однопоточный путь с авторизацией
            //    (SendWithAuthAsync + ReadAsStream) — тот же прогресс и удаление файла
            //    при ошибке/отмене.
            _logger.Info("[Platform] Многопоточная загрузка недоступна (мал файл/нет Range/сбой) — однопоточная.");
            return await DownloadUpdateAsync(url, targetPath, progress, ct).ConfigureAwait(false);
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

    /// <summary>
    /// Создаёт HTTP-клиент для многопоточной загрузки дистрибутива: автоперенаправление
    /// включено (CDN отдаёт финальный адрес редиректом), cookie-контейнер общий с основным
    /// клиентом (сессия портала), Basic Auth — как в <see cref="AddBasicAuth"/> (учётные
    /// данные читаются из настроек на каждый вызов; заголовок Authorization и пароль не
    /// логируются), User-Agent/Accept — как у основного клиента, таймаут дольше (крупные
    /// файлы). Клиент используется только в пределах одной операции загрузки и освобождается.
    /// </summary>
    private HttpClient CreateParallelClient()
    {
        var handler = _handlerOverride ?? new HttpClientHandler
        {
            // Для CDN дистрибутивов 1С автоследование безопаснее: сервер редиректит на
            // хранилище файлов, учётные данные передаются заголовком DefaultRequestHeaders
            // в пределах этой операции (клиент создаётся локально и освобождается).
            AllowAutoRedirect = true,
            CookieContainer = _cookieContainer,
        };

        var client = new HttpClient(handler)
        {
            Timeout = ParallelAttemptTimeout,
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        client.DefaultRequestHeaders.Accept.ParseAdd("text/html,application/xhtml+xml,*/*;q=0.8");

        var (login, password) = GetCredentials();
        if (!string.IsNullOrEmpty(login))
        {
            var token = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{login}:{password}"));
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", token);
        }

        return client;
    }

    /// <summary>
    /// Выбор стратегии загрузки дистрибутива: многопоточность имеет смысл, когда размер
    /// файла известен и даёт более одного сегмента по <c>MinSegmentBytes</c> (1 МБ), т.е.
    /// файл не меньше 2 МБ. Переиспользует <see cref="ParallelDownloader.CanParallelize"/>.
    /// </summary>
    internal static bool ChooseDownloadStrategy(long totalBytes)
        => ParallelDownloader.CanParallelize(totalBytes, ParallelDownloader.DefaultMaxParallelism);

    /// <summary>
    /// Формирует имя итогового файла дистрибутива платформы: префикс версии + имя файла
    /// (символы, недопустимые в имени файла, заменяются на '_'). Пустые части пропускаются.
    /// </summary>
    /// <param name="version">Версия платформы, например «8.3.27.2214».</param>
    /// <param name="fileName">Имя файла из каталога, например «8.3.27.2214_x64.zip».</param>
    internal static string BuildTargetFileName(string version, string fileName)
    {
        var versionPart = SanitizeFileName(version);
        var namePart = SanitizeFileName(fileName);
        if (string.IsNullOrWhiteSpace(versionPart))
            return namePart;
        if (string.IsNullOrWhiteSpace(namePart))
            return versionPart;
        return $"{versionPart}_{namePart}";
    }

    /// <summary>Заменяет символы, недопустимые в имени файла, на '_' (пустая строка — пустая).</summary>
    private static string SanitizeFileName(string value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(value.Length);
        foreach (var c in value)
            sb.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
        return sb.ToString().Trim();
    }

    /// <summary>
    /// Журналирует предупреждение о малом свободном месте перед загрузкой (не блокирует).
    /// Порог: свободно меньше <c>размер файла + 1 ГБ</c> (минимум 1 ГБ при неизвестном размере).
    /// </summary>
    private void LogFreeSpaceWarning(string targetDir, long expectedSizeBytes)
    {
        const long gb = 1024L * 1024 * 1024;
        try
        {
            var info = DiskFreeSpaceHelper.TryGetInfo(targetDir, DiskFreeSpaceHelper.DefaultDriveResolver);
            if (info is null)
                return;

            var requiredBytes = Math.Max(expectedSizeBytes, 0) + gb;
            var requiredGb = (int)Math.Clamp((requiredBytes + gb - 1) / gb, 1, int.MaxValue);
            if (DiskFreeSpaceHelper.IsWarning(info.FreeBytes, requiredGb))
            {
                _logger.Warn(
                    $"[Platform] Мало свободного места на диске {DiskFreeSpaceHelper.ResolveDriveName(targetDir)}: " +
                    $"свободно {DiskFreeSpaceHelper.FormatBytes(info.FreeBytes)}, " +
                    $"требуется не менее {DiskFreeSpaceHelper.FormatBytes(requiredBytes)} ({targetDir}).");
            }
        }
        catch
        {
            // Проверка места никогда не мешает загрузке.
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

    /// <summary>Сравнивает два URI без учёта регистра (для распознавания циклических
    /// редиректов на тот же адрес, issue #323).</summary>
    private static bool SameUri(Uri? a, Uri? b)
    {
        if (a is null || b is null)
            return false;
        return string.Equals(a.AbsoluteUri, b.AbsoluteUri, StringComparison.OrdinalIgnoreCase);
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

    /// <inheritdoc />
    public async Task<string?> GetPageTextAsync(string url, CancellationToken ct = default)
    {
        var page = await FetchPageCoreAsync(url, ct).ConfigureAwait(false);
        if (page.Status != PortalFetchStatus.Ok)
        {
            _logger.Warn($"[Updates] Не удалось получить страницу ({page.Status}): {url}");
            return null;
        }

        return page.Text;
    }

    /// <inheritdoc />
    public async Task<PortalPageResult> FetchPageAsync(string url, CancellationToken ct = default)
        => await FetchPageCoreAsync(url, ct).ConfigureAwait(false);

    /// <summary>Выполняет авторизованный GET страницы портала и возвращает текст ответа вместе
    /// со статусом: Ok — тело получено; AuthRequired — требуется вход (редирект/страница входа,
    /// а вход не выполнялся либо не настроен); AuthFailed — вход предпринимался, но сервер не
    /// подтвердил учётные данные (401/цепочка на странице входа, issue #334/#330/#323);
    /// NetworkError — сеть/HTTP/пусто; Cancelled — отмена. Исключения наружу не бросаются.</summary>
    private async Task<PortalPageResult> FetchPageCoreAsync(string url, CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            using var response = await SendWithAuthAsync(request, HttpCompletionOption.ResponseContentRead, ct)
                .ConfigureAwait(false);

            var authFailed = _lastLoginResult is PortalLoginResult.AuthFailed or PortalLoginResult.RedirectFailed;
            // Форма входа изменилась радикально (OAuth/JS-челлендж) или не получена — отдельный
            // статус с понятным сообщением (issue #323/#330/#334).
            var formUnavailable = _lastLoginResult == PortalLoginResult.FormUnavailable;

            // Ответ со страницей входа либо редирект на неё — требуется авторизация.
            // Если лимит попыток входа исчерпан — отдельный статус (issue #334/#330/#323);
            // если форма входа недоступна — FormUnavailable; если вход предпринимался и не
            // подтверждён сервером — это именно AuthFailed.
            var isLoginRedirect =
                response.Headers.Location?.Host.Contains("login.1c.ru", StringComparison.OrdinalIgnoreCase) == true ||
                response.RequestMessage?.RequestUri?.Host.Contains("login.1c.ru", StringComparison.OrdinalIgnoreCase) == true;
            if (isLoginRedirect)
                return Page(IsPortalLoginLimitReached() ? PortalFetchStatus.LoginLimitReached
                    : formUnavailable ? PortalFetchStatus.FormUnavailable
                    : authFailed ? PortalFetchStatus.AuthFailed
                    : PortalFetchStatus.AuthRequired);

            if (!response.IsSuccessStatusCode)
            {
                var code = (int)response.StatusCode;
                if (code is 401 or 403 or (>= 300 and < 400))
                    return Page(IsPortalLoginLimitReached() ? PortalFetchStatus.LoginLimitReached
                        : formUnavailable ? PortalFetchStatus.FormUnavailable
                        : authFailed ? PortalFetchStatus.AuthFailed
                        : PortalFetchStatus.AuthRequired);
                return Page(PortalFetchStatus.NetworkError);
            }

            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(body))
                return Page(PortalFetchStatus.NetworkError);

            // Страница входа может прийти с HTTP 200 (CAS отдаёт форму без сессии,
            // issue #330): распознаём по содержимому вместо парсинга версий.
            if (LooksLikeLoginForm(body))
            {
                _logger.Warn($"[Updates] Получена страница входа вместо содержимого ({url}) — авторизация не выполнена.");
                return Page(IsPortalLoginLimitReached() ? PortalFetchStatus.LoginLimitReached
                    : formUnavailable ? PortalFetchStatus.FormUnavailable
                    : authFailed ? PortalFetchStatus.AuthFailed
                    : PortalFetchStatus.AuthRequired);
            }

            return Page(PortalFetchStatus.Ok, body);
        }
        catch (OperationCanceledException)
        {
            return Page(PortalFetchStatus.Cancelled);
        }
        catch
        {
            return Page(PortalFetchStatus.NetworkError);
        }
    }

    /// <summary>Выполняет GET и возвращает тело ответа как строку; при сетевой ошибке или
    /// не-успешном статусе возвращает пустую строку.</summary>
    private async Task<string> GetTextAsync(string url, CancellationToken ct)
    {
        var page = await FetchPageCoreAsync(url, ct).ConfigureAwait(false);
        return page.Status == PortalFetchStatus.Ok ? page.Text ?? string.Empty : string.Empty;
    }

    private static PortalPageResult Page(PortalFetchStatus status, string? text = null)
        => new() { Status = status, Text = text };

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

        // Вход на portal.1c.ru выполняется НЕ более одного раза за вызов (issue #330/#334):
        // «фантомный успех» или рассинхронизация сессии не должны тратить весь лимит попыток
        // внутри одного запроса — при повторном 302→login возвращаем ответ как есть, а
        // CheckForUpdatesAsync/FetchPageCoreAsync распознают страницу входа и вернут
        // AuthRequired/AuthFailed/LoginLimitReached.
        var loginTried = false;
        for (var i = 0; i <= MaxRedirects; i++)
        {
            AddBasicAuth(current);

            var response = await _httpClient.SendAsync(current, completionOption, ct).ConfigureAwait(false);

            var status = (int)response.StatusCode;

            // Гибридная авторизация. Ресурс releases.1c.ru при отсутствии сессии НЕ выдаёт 401,
            // а перенаправляет на login.1c.ru (Spring Security CAS). Поэтому вход запускаем при:
            //   - HTTP 401/403 (возможен Basic-вариант), либо
            //   - редиректе на login.1c.ru (нужна cookie-сессия).
            // Попытки ограничены MaxPortalLoginAttempts на сессию (сброс при смене учётной записи
            // и при успешном входе), при успехе повторяем исходный запрос с сохранёнными cookie.
            var needsLogin =
                (status is 401 or 403) ||
                (response.Headers.Location?.Host.Contains("login.1c.ru", StringComparison.OrdinalIgnoreCase) == true);

            if (needsLogin && !loginTried && CanAttemptPortalLogin())
            {
                loginTried = true;
                // Диагностика (issue #323/#330/#334): причина запуска входа и его результат —
                // по журналу должно быть видно, на каком звене CAS-цепочка рвётся.
                var reason = status is 401 or 403 ? $"http-{status}" : "redirect-login";
                var locText = response.Headers.Location?.ToString() ?? "<нет>";
                _logger.Info($"[Updates] Вход запущен: reason={reason}, url='{current.RequestUri}', location='{locText}'");
                // Передаём полный URL редиректа (login.1c.ru/login?service=...): форма входа
                // получит service= исходного каталога, и CAS после входа вернёт верный адрес.
                var loginResult = await TryLoginPortalAsync(response.Headers.Location?.ToString(), ct).ConfigureAwait(false);
                _logger.Info($"[Updates] Вход запущен: результат={loginResult}, повтор исходного запроса={(loginResult == PortalLoginResult.Success)}");
                if (loginResult == PortalLoginResult.Success)
                {
                    response.Dispose();
                    var rebuilt = new HttpRequestMessage(current.Method, current.RequestUri!);
                    current.Dispose();
                    current = rebuilt;
                    continue;
                }
                // Вход не удался — продолжаем обычную обработку редиректа/ответа ниже.
            }

            // Диагностика (issue #323): каждый редирект логируем с номером шага и Location,
            // чтобы по журналу был виден реальный путь CAS-авторизации portal.1c.ru.
            if (status is >= 300 and < 400)
            {
                var locText = response.Headers.Location?.ToString() ?? "<нет Location>";
                _logger.Info($"[Updates] Редирект {status} (шаг {i}): '{locText}' для '{current.RequestUri}'");
            }

            // releases.1c.ru при отсутствии сессии может вернуть 302 БЕЗ заголовка Location
            // либо 302 на тот же адрес (циклический редирект, issue #323). Это не сбой
            // протокола, а требование авторизации (CAS): пробуем войти и повторить исходный
            // запрос с сохранёнными cookie (попытки ограничены счётчиком сессии).
            var selfRedirect = status is >= 300 and < 400 &&
                               (response.Headers.Location is null ||
                                SameUri(current.RequestUri, response.Headers.Location));
            if (selfRedirect && !loginTried && CanAttemptPortalLogin())
            {
                loginTried = true;
                // Диагностика (issue #323/#330/#334): циклический/пустой редирект — тот же
                // признак требования авторизации, что и прямой редирект на login.1c.ru.
                _logger.Info($"[Updates] Вход запущен: reason={(response.Headers.Location is null ? "redirect-no-location" : "self-redirect")}, url='{current.RequestUri}'");
                // Location отсутствует (302 без заголовка) — форма входа по базовому адресу.
                var loginResult = await TryLoginPortalAsync(null, ct).ConfigureAwait(false);
                _logger.Info($"[Updates] Вход запущен: результат={loginResult}, повтор исходного запроса={(loginResult == PortalLoginResult.Success)}");
                if (loginResult == PortalLoginResult.Success)
                {
                    response.Dispose();
                    var rebuilt = new HttpRequestMessage(current.Method, current.RequestUri!);
                    current.Dispose();
                    current = rebuilt;
                    continue;
                }
                _logger.Warn($"[Updates] HTTP {status} без полезного Location для '{current.RequestUri}' — вход на portal.1c.ru не выполнен (проверьте учётные данные ИТС).");
            }

            // Диагностика «фантомного успеха» (issue #323/#330/#334): вход в рамках этой операции
            // уже выполнялся и завершился «успехом» (сессионная cookie появилась в контейнере),
            // но повтор исходного запроса СНОВА дал 302 на login.1c.ru — сервер не принял cookie.
            // Второй вход не запускаем (loginTried=true): лимит попыток не тратится впустую
            // (лог issue #330: три подряд «Вход выполнен (status=200)» → лимит → AuthRequired).
            if (loginTried &&
                _lastLoginResult == PortalLoginResult.Success &&
                response.Headers.Location?.Host.Contains("login.1c.ru", StringComparison.OrdinalIgnoreCase) == true)
            {
                _logger.Warn("[Updates] retryAfterLoginStill302=true: после «успешного» входа повтор " +
                             "исходного запроса снова дал 302 на login.1c.ru (фантомный успех); второй " +
                             "вход в рамках операции не выполняется.");
            }

            // На страницу входа portal.1c.ru редирект НЕ следуем: если сессии нет, а программный
            // вход не удался, GET формы входа вернёт HTML без версий, и проверка ложно завершится
            // статусом Unavailable («каталог доступен, точная версия не определена»). Возвращаем
            // редирект как есть, а CheckForUpdatesAsync распознает login.1c.ru и покажет ошибку
            // авторизации (issue #323).
            if (response.Headers.Location?.Host.Contains("login.1c.ru", StringComparison.OrdinalIgnoreCase) == true)
                return response;

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
    /// на основе учётной записи ИТС (issue #333): выбранной в настройках (<see cref="AppSettings.ItsAccountId"/>)
    /// или «Основной» из справочника <see cref="IItsAccountsStore"/>. Если логин не задан — запрос
    /// выполняется без авторизации (обратная совместимость). Учётные данные читаются на каждый
    /// запрос, поэтому смена записи в справочнике не требует перезапуска.
    /// Заголовок Authorization и пароль не логируются.
    /// </summary>
    private void AddBasicAuth(HttpRequestMessage request)
    {
        var (login, password) = GetCredentials();
        if (string.IsNullOrEmpty(login))
            return;

        var token = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{login}:{password}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", token);
    }

    /// <summary>
    /// Возвращает логин/пароль для авторизации на сайте 1С: учётная запись из справочника
    /// <see cref="IItsAccountsStore"/> (выбранная в настройках <see cref="AppSettings.ItsAccountId"/>
    /// или «Основная»), при её отсутствии — устаревшие поля
    /// <see cref="AppSettings.UpdatesLogin"/>/<see cref="AppSettings.UpdatesPassword"/> (миграция
    /// ещё не выполнена либо справочник пуст). Пароль никогда не логируется.
    /// </summary>
    private (string Login, string Password) GetCredentials()
    {
        var settings = _repository.LoadSettings();
        var account = _itsAccounts?.Resolve(settings.ItsAccountId);
        if (account is not null && !string.IsNullOrWhiteSpace(account.Login))
            return (account.Login ?? string.Empty, account.Password ?? string.Empty);

        return (settings.UpdatesLogin ?? string.Empty, settings.UpdatesPassword ?? string.Empty);
    }

    /// <summary>
    /// Программный вход на portal.1c.ru (гибридная авторизация, Spring Security CAS).
    /// Поток (issue #323/#334): GET формы входа (по URL редиректа сервера, если он известен —
    /// так форма получает <c>service=</c> исходного каталога), извлечение скрытого токена
    /// <c>execution</c>, POST логина и доведение до конца цепочки редиректов после входа
    /// (обычно 302 на <c>releases.1c.ru/public/security_check?ticket=ST-…</c> — сессионная
    /// cookie TGC/JSESSIONID выставляется именно при обращении по этому адресу). Cookie
    /// сохраняются в общем <see cref="CookieContainer"/>, поэтому последующие запросы
    /// проходят авторизацию автоматически.
    /// Возвращает true, если вход завершился успешно (финальный ответ вне страницы входа).
    /// </summary>
    /// <param name="loginUrl">Полный URL редиректа с сервера (<c>login.1c.ru/login?service=…</c>)
    /// или null — тогда используется базовый <see cref="PortalLoginUrl"/>.</param>
    private async Task<PortalLoginResult> TryLoginPortalAsync(string? loginUrl, CancellationToken ct)
    {
        // Ранний выход: сессионная cookie портала уже установлена (предыдущий успешный вход
        // в этой сессии службы) — повторный вход не требуется, каталог отдаст контент сразу
        // (issue #330/#334). Заодно сбрасываем счётчик попыток как при любом успехе.
        if (HasPortalSessionCookie())
        {
            _portalLoginAttempts = 0;
            _lastLoginResult = PortalLoginResult.Success;
            return PortalLoginResult.Success;
        }

        var (login, password) = GetCredentials();
        if (string.IsNullOrEmpty(login))
        {
            _logger.Warn("[Updates] Для входа на portal.1c.ru не задан логин.");
            _lastLoginResult = PortalLoginResult.NoCredentials;
            return PortalLoginResult.NoCredentials;
        }

        try
        {
            var formUrl = ResolveLoginFormUrl(loginUrl);
            _logger.Info($"[Updates] Вход на portal.1c.ru: учётная запись '{ResolveAccountName()}', " +
                         $"credsPresent={!string.IsNullOrEmpty(login)}, форма: {formUrl}");

            // Шаг 1: GET формы входа — получаем HTML и все скрытые поля Spring Security CAS
            // (execution, lt, CSRF и пр.). Запрос идёт по HTTP/1.1: часть CAS-серверов некорректно
            // обрабатывает HTTP/2 (ответ 401), см. LoginHttpVersion.
            using (var formRequest = new HttpRequestMessage(HttpMethod.Get, formUrl) { Version = LoginHttpVersion })
            using (var formResponse =
                   await _httpClient.SendAsync(formRequest, HttpCompletionOption.ResponseContentRead, ct).ConfigureAwait(false))
            {
                var formStatus = (int)formResponse.StatusCode;
                var html = await formResponse.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                var fields = ExtractFormFields(html);
                var hasExecution = fields.TryGetValue("execution", out var execution) && !string.IsNullOrWhiteSpace(execution);
                var hasLt = fields.ContainsKey("lt");
                var formAction = ExtractFormAction(html);
                var fieldNames = fields.Count == 0
                    ? "<нет>"
                    : string.Join(",", fields.Keys.OrderBy(k => k, StringComparer.Ordinal));
                _logger.Info($"[Updates] Вход: GET формы status={formStatus}, execution={(hasExecution ? "есть" : "нет")}, " +
                             $"lt={(hasLt ? "есть" : "нет")}, action='{formAction ?? "<нет>"}', поля: {fieldNames}");

                if (!hasExecution)
                {
                    LogAnonymizedAuthFailure("[Updates] Не удалось извлечь токен 'execution' из формы входа.", html, fields);
                    // Распознавание «протокол изменился» (issue #323/#330/#334): если в форме
                    // нет классических токенов CAS (execution/lt), но есть признаки OAuth/JS-
                    // челленджа — программный вход невозможен в принципе; пользователю нужен
                    // браузер, а не очередная попытка POST.
                    if (LooksLikeOAuthOrChallenge(html))
                    {
                        _logger.Warn("[Updates] Форма входа изменилась (признаки OAuth/JS-челленджа): " +
                                     "автоматический вход временно недоступен, откройте login.1c.ru в браузере.");
                    }
                    _lastLoginResult = PortalLoginResult.FormUnavailable;
                    return PortalLoginResult.FormUnavailable;
                }

                // Шаг 2: POST на АТРИБУТ action формы (issue #323/#330/#334, третья итерация):
                // ранее POST всегда шёл на URL GET-формы, а Spring Security CAS часто указывает
                // отдельный action («/login/cas?service=…») — запрос уходил не туда (401/404).
                // Если action отсутствует/пуст — POST остаётся на адресе формы (прежнее поведение).
                // Набор полей строится ДИНАМИЧЕСКИ из фактической формы (execution, lt, CSRF
                // и пр.) + обязательные username/password/_eventId=submit — жёсткий список
                // 0.3.9.297 отклоняется сервером 401 при изменении формы входа (issue #334).
                var postUrl = ResolveFormPostUrl(formUrl, formAction);
                if (!string.Equals(postUrl, formUrl, StringComparison.Ordinal))
                {
                    _logger.Info($"[Updates] Вход: POST на action формы '{postUrl}' (GET-форма: {formUrl})");
                }
                var form = new Dictionary<string, string>(fields, StringComparer.Ordinal)
                {
                    ["username"] = login,
                    ["password"] = password,
                    ["_eventId"] = "submit",
                };

                using var postRequest = new HttpRequestMessage(HttpMethod.Post, postUrl) { Version = LoginHttpVersion };
                postRequest.Content = new FormUrlEncodedContent(form);
                postRequest.Content.Headers.ContentType =
                    new MediaTypeHeaderValue("application/x-www-form-urlencoded");
                // Часть CAS-развёртываний проверяет Origin/Referer при POST формы.
                postRequest.Headers.Referrer = new Uri(formUrl);
                postRequest.Headers.TryAddWithoutValidation("Origin", new Uri(formUrl).GetLeftPart(UriPartial.Authority));

                using var postResponse =
                    await _httpClient.SendAsync(postRequest, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);

                var postStatus = (int)postResponse.StatusCode;
                var postLocation = postResponse.Headers.Location?.ToString() ?? "<нет>";
                // Cookie из ответа POST явно добавляем в общее хранилище: страховка для
                // кастомных транспортов/тестов с fake-обработчиками (в проде HttpClientHandler
                // уже обрабатывает Set-Cookie; повторное добавление той же cookie безопасно).
                // После этого sessionCookie корректно отражает факт установки сессии.
                ApplySetCookieToContainer(postResponse, postRequest.RequestUri ?? new Uri(postUrl));
                _logger.Info($"[Updates] Вход: POST status={postStatus}, location='{postLocation}', " +
                             $"sessionCookie={HasPortalSessionCookie()}");
                _logger.Info($"[Updates] Вход: POST Set-Cookie: {DescribeSetCookies(postResponse)}");

                // Шаг 3: доводим CAS-цепочку до конца. После успешного входа сервер отвечает
                // 302 на releases.1c.ru/public/security_check?ticket=ST-…; сессионная cookie
                // устанавливается при обращении по этому адресу. Без этого шага повторный
                // запрос каталога снова уходил бы в 302 (issue #323/#334).
                if (postStatus is >= 300 and < 400 && postResponse.Headers.Location is not null)
                {
                    var completed = await FollowLoginRedirectsAsync(
                        postResponse.Headers.Location, ct).ConfigureAwait(false);
                    _logger.Info($"[Updates] Вход: FollowLoginRedirectsAsync={completed}, " +
                                 $"sessionCookie={HasPortalSessionCookie()}");
                    if (completed)
                    {
                        _logger.Info("[Updates] Вход на portal.1c.ru выполнен (цепочка редиректов пройдена).");
                        // Успешный вход сбрасывает счётчик попыток (issue #334/#330/#323):
                        // сессия установлена, следующие операции могут входить заново.
                        _portalLoginAttempts = 0;
                        _lastLoginResult = PortalLoginResult.Success;
                        LogPortalCookieInventory();
                        return PortalLoginResult.Success;
                    }

                    _logger.Warn("[Updates] Вход на portal.1c.ru не подтверждён: цепочка редиректов завершилась на странице входа.");
                    _lastLoginResult = PortalLoginResult.RedirectFailed;
                    return PortalLoginResult.RedirectFailed;
                }

                // Успех без редиректа: 2xx. ВАЖНО: при неверном логине CAS может вернуть 200
                // с телом формы входа (поля execution/lt, сообщение об ошибке) и БЕЗ сессионной
                // cookie — такой ответ НЕ является успехом («фантомный успех», issue #330):
                // проверяем содержимое тела, наличие Set-Cookie и сессионной cookie, а не
                // только код ответа.
                if (postResponse.IsSuccessStatusCode)
                {
                    var postBody = await ReadBodyQuietlyAsync(postResponse, ct).ConfigureAwait(false);
                    // Расширенная диагностика ветки 2xx (issue #323/#330/#334): contentType,
                    // длина тела, превью (без секретов) и Set-Cookie только именами/флагами.
                    LogPost2xxDiagnostics(postStatus, postResponse, postBody, fields, login, password);

                    if (LooksLikeLoginForm(postBody))
                    {
                        LogAnonymizedAuthFailure(
                            $"[Updates] Вход на portal.1c.ru не подтверждён (status={postStatus}: в теле форма входа).",
                            postBody, fields);
                        _lastLoginResult = PortalLoginResult.AuthFailed;
                        return PortalLoginResult.AuthFailed;
                    }

                    // Следование JS/meta-refresh-редиректу в теле 2xx (issue #323/#330/#334):
                    // CAS-цепочка часто доводится до releases.1c.ru/public/security_check?ticket=…
                    // именно JS-редиректом, и сессионная cookie выставляется на этом звене.
                    var bodyRedirect = ExtractBodyRedirectUrl(postBody);
                    if (bodyRedirect is not null)
                    {
                        _logger.Info($"[Updates] Вход: в теле 2xx найден JS/meta-refresh редирект на '{bodyRedirect}' — следуем.");
                        var target = ResolveBodyRedirectTarget(postUrl, bodyRedirect);
                        if (target is not null)
                        {
                            var jsCompleted = await FollowLoginRedirectsAsync(target, ct).ConfigureAwait(false);
                            _logger.Info($"[Updates] Вход: FollowLoginRedirectsAsync(js)={jsCompleted}, " +
                                         $"sessionCookie={HasPortalSessionCookie()}");
                            if (jsCompleted)
                            {
                                _logger.Info("[Updates] Вход на portal.1c.ru выполнен (JS/meta-refresh цепочка пройдена).");
                                _portalLoginAttempts = 0;
                                _lastLoginResult = PortalLoginResult.Success;
                                LogPortalCookieInventory();
                                return PortalLoginResult.Success;
                            }
                        }
                    }

                    // «Фантомный успех» (issue #330): сервер вернул 200, но сессионная cookie
                    // НЕ установлена и в ответе нет ни одного Set-Cookie — вход фактически не
                    // выполнен. Такой ответ успехом больше НЕ считается (раньше 2xx + тело без
                    // формы входа проходило как Success, и следующий запрос каталога снова давал
                    // 302 → повторный вход → исчерпание лимита за одну операцию, лог 7OH).
                    var hasSession = HasPortalSessionCookie();
                    var hasSetCookie = HasSetCookieHeader(postResponse);
                    if (!hasSession && !hasSetCookie)
                    {
                        _logger.Warn("[Updates] Вход на portal.1c.ru не подтверждён: сервер вернул 200 " +
                                     "без установки сессии и без Set-Cookie (фантомный успех) — вход " +
                                     "не засчитан, повтор исходного запроса и лимит попыток не тратятся.");
                        _lastLoginResult = PortalLoginResult.AuthFailed;
                        return PortalLoginResult.AuthFailed;
                    }

                    _logger.Info($"[Updates] Вход на portal.1c.ru выполнен (status={postStatus}), " +
                                 $"sessionCookie={hasSession}, setCookie={hasSetCookie}.");
                    _portalLoginAttempts = 0;
                    _lastLoginResult = PortalLoginResult.Success;
                    LogPortalCookieInventory();
                    return PortalLoginResult.Success;
                }

                // 401 либо 200 с формой ошибки — читаем тело и логируем анонимизированные
                // признаки (без пароля/логина/значений токенов), чтобы отличить «неверный
                // пароль» от «изменилась форма» от «требуется капча» (issue #334).
                var body = postStatus is 200 or 401
                    ? await ReadBodyQuietlyAsync(postResponse, ct).ConfigureAwait(false)
                    : string.Empty;
                LogAnonymizedAuthFailure($"[Updates] Вход на portal.1c.ru не подтверждён (status={postStatus}).", body, fields);
                _logger.Info($"[Updates] Вход: итог=AuthFailed, status={postStatus}");
                _lastLoginResult = PortalLoginResult.AuthFailed;
                return PortalLoginResult.AuthFailed;
            }
        }
        catch (Exception ex)
        {
            _logger.Warn($"[Updates] Ошибка входа на portal.1c.ru: {ex.GetType().Name}: {ex.Message}");
            _lastLoginResult = PortalLoginResult.FormUnavailable;
            return PortalLoginResult.FormUnavailable;
        }
    }

    /// <summary>Результат программного входа на portal.1c.ru (гибридная авторизация CAS).</summary>
    public enum PortalLoginResult
    {
        /// <summary>Вход выполнен успешно (цепочка редиректов пройдена либо ответ 2xx).</summary>
        Success,

        /// <summary>Учётные данные не приняты сервером (HTTP 401 либо форма ошибки после POST).</summary>
        AuthFailed,

        /// <summary>Учётные данные не заданы (логин пуст).</summary>
        NoCredentials,

        /// <summary>Форма входа недоступна: не получен HTML или не извлечён токен execution.</summary>
        FormUnavailable,

        /// <summary>Цепочка редиректов после входа не завершилась (страница входа / слишком много переходов).</summary>
        RedirectFailed,
    }

    /// <summary>
    /// True — допустима ещё одна попытка программного входа на portal.1c.ru. Лимит —
    /// <see cref="MaxPortalLoginAttempts"/> попыток за сессию службы; счётчик сбрасывается при
    /// смене учётной записи (логин/выбранная запись ИТС), при УСПЕШНОМ входе
    /// (см. <see cref="TryLoginPortalAsync"/>) и автоматически через <see cref="LoginLimitCooldown"/>
    /// после исчерпания (issue #330/#323/#334). При отсутствии учётных данных вход
    /// не «тратит» попытки: каждая операция быстро вернёт NoCredentials и понятное предупреждение.
    /// </summary>
    private bool CanAttemptPortalLogin()
    {
        var (login, _) = GetCredentials();
        if (string.IsNullOrEmpty(login))
        {
            _logger.Warn("[Updates] Для входа на portal.1c.ru не задан логин.");
            return false;
        }

        var signature = ComputeAccountSignature();
        if (!string.Equals(_lastAttemptAccountSignature, signature, StringComparison.Ordinal))
        {
            _lastAttemptAccountSignature = signature;
            _portalLoginAttempts = 0;
            _limitReachedAt = default;
        }

        if (_portalLoginAttempts >= MaxPortalLoginAttempts)
        {
            // Автосброс лимита по таймеру: через LoginLimitCooldown после исчерпания
            // лимита новая попытка входа разрешается автоматически (issue #334/#330/#323).
            if (_limitReachedAt == default)
                _limitReachedAt = UtcNowProvider();

            if (UtcNowProvider() - _limitReachedAt >= LoginLimitCooldown)
            {
                _portalLoginAttempts = 0;
                _limitReachedAt = default;
                _logger.Info($"[Updates] Лимит попыток входа на portal.1c.ru автоматически сброшен (прошло более {(int)LoginLimitCooldown.TotalMinutes} мин).");
            }
            else
            {
                var remaining = (int)(LoginLimitCooldown - (UtcNowProvider() - _limitReachedAt)).TotalMinutes;
                _logger.Warn("[Updates] Исчерпан лимит попыток входа на portal.1c.ru (" +
                             $"{MaxPortalLoginAttempts}) за сессию (повторная попытка через ~{remaining} мин). " +
                             "Проверьте учётные данные ИТС в «Настройки → Учётные данные ИТС»; " +
                             "при неверном пароле портал может временно блокировать аккаунт.");
                return false;
            }
        }

        _portalLoginAttempts++;
        return true;
    }

    /// <summary>True — лимит попыток входа на portal.1c.ru исчерпан (до автосброса по
    /// <see cref="LoginLimitCooldown"/> либо явного <see cref="ResetPortalLoginAttempts"/>).
    /// Используется для показа отдельной ошибки «лимит исчерпан» в результатах проверок
    /// вместо вводящего в заблуждение AuthRequired/AuthFailed (issue #334/#330/#323).
    /// Лимит «исчерпан» только если попытка входа была фактически ЗАБЛОКИРОВАНА
    /// (<see cref="_limitReachedAt"/> взведён): если же счётчик достиг максимума штатными
    /// неудачными попытками (попытка №Max была разрешена и не подтверждена сервером),
    /// результат остаётся AuthFailed — статус «лимит» наступает со следующей
    /// заблокированной попытки.</summary>
    internal bool IsPortalLoginLimitReached()
    {
        if (_limitReachedAt == default)
            return false;

        // Если время автосброса уже наступило — лимит считается снятым.
        return UtcNowProvider() - _limitReachedAt < LoginLimitCooldown;
    }

    /// <summary>Принудительно сбрасывает счётчик попыток входа на portal.1c.ru — например,
    /// после явного действия пользователя (смена учётных данных ИТС в настройках).
    /// Сбрасывается только локальный счётчик; анти-брутфорс-защита самого портала
    /// (временная блокировка аккаунта при многократных неверных входах) не отменяется.</summary>
    public void ResetPortalLoginAttempts()
    {
        _portalLoginAttempts = 0;
        _limitReachedAt = default;
    }

    /// <summary>Сигнатура учётной записи для сброса счётчика попыток входа: выбранная запись
    /// справочника (или устаревшие поля настроек) + логин. Пароль в сигнатуру НЕ входит.</summary>
    private string ComputeAccountSignature()
    {
        var settings = _repository.LoadSettings();
        var account = _itsAccounts?.Resolve(settings.ItsAccountId);
        var login = account is not null ? account.Login : settings.UpdatesLogin;
        return $"{settings.ItsAccountId ?? string.Empty}|{login ?? string.Empty}";
    }

    /// <summary>Читает тело ответа без исключений (для анонимизированной диагностики 401).</summary>
    private static async Task<string> ReadBodyQuietlyAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            return await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        }
        catch
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// Явно добавляет cookie из заголовков <c>Set-Cookie</c> ответа в общее хранилище
    /// (issue #323/#330/#334): в проде эту работу выполняет <c>HttpClientHandler</c>, но для
    /// кастомных транспортов и тестов с fake-обработчиками обработка дублируется здесь, чтобы
    /// <see cref="HasPortalSessionCookie"/> корректно отражал факт установки сессии. Повторное
    /// добавление той же cookie в контейнер безопасно (заменяет предыдущую). Некорректные
    /// заголовки игнорируются — вход не роняется.
    /// </summary>
    private void ApplySetCookieToContainer(HttpResponseMessage response, Uri requestUri)
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out var values))
            return;

        foreach (var header in values)
        {
            try
            {
                var cookie = ParseSetCookie(header, requestUri);
                if (cookie is null)
                    continue;

                var host = cookie.Domain.StartsWith(".", StringComparison.Ordinal)
                    ? cookie.Domain.TrimStart('.')
                    : cookie.Domain;
                _cookieContainer.Add(new Uri($"https://{host}/"), cookie);
            }
            catch
            {
                // Некорректный Set-Cookie не должен ронять вход.
            }
        }
    }

    /// <summary>
    /// Разбирает один заголовок <c>Set-Cookie</c> в <see cref="Cookie"/>: имя/значение и
    /// атрибуты Path/Domain/Expires/HttpOnly/Secure. Возвращает null при отсутствии пары
    /// name=value или пустом имени. Значения cookie в журнал не выводятся
    /// (issue #323/#330/#334).
    /// </summary>
    internal static Cookie? ParseSetCookie(string header, Uri fallbackUri)
    {
        if (string.IsNullOrWhiteSpace(header))
            return null;

        var parts = header.Split(';');
        var first = parts[0];
        var eq = first.IndexOf('=');
        if (eq <= 0)
            return null;

        var name = first.Substring(0, eq).Trim();
        var value = first.Substring(eq + 1).Trim();
        if (name.Length == 0)
            return null;

        var cookie = new Cookie(name, value);
        for (var i = 1; i < parts.Length; i++)
        {
            var p = parts[i].Trim();
            if (p.Length == 0)
                continue;

            var eq2 = p.IndexOf('=');
            var attrName = eq2 > 0 ? p.Substring(0, eq2).Trim() : p;
            var attrValue = eq2 > 0 ? p.Substring(eq2 + 1).Trim() : string.Empty;

            if (string.Equals(attrName, "path", StringComparison.OrdinalIgnoreCase) && attrValue.Length > 0)
                cookie.Path = attrValue;
            else if (string.Equals(attrName, "domain", StringComparison.OrdinalIgnoreCase) && attrValue.Length > 0)
                cookie.Domain = attrValue;
            else if (string.Equals(attrName, "expires", StringComparison.OrdinalIgnoreCase) && attrValue.Length > 0
                     && DateTime.TryParse(attrValue, CultureInfo.InvariantCulture, DateTimeStyles.None, out var expires))
                cookie.Expires = expires;
            else if (string.Equals(attrName, "httponly", StringComparison.OrdinalIgnoreCase))
                cookie.HttpOnly = true;
            else if (string.Equals(attrName, "secure", StringComparison.OrdinalIgnoreCase))
                cookie.Secure = true;
        }

        if (cookie.Domain.Length == 0)
            cookie.Domain = fallbackUri.Host;

        return cookie;
    }

    /// <summary>True — в заголовках ответа есть хотя бы один <c>Set-Cookie</c>.</summary>
    private static bool HasSetCookieHeader(HttpResponseMessage response)
        => response.Headers.TryGetValues("Set-Cookie", out _);

    /// <summary>Имена и флаги cookie из всех заголовков <c>Set-Cookie</c> ответа POST входа
    /// (БЕЗ значений) — диагностика «фантомного успеха» (issue #323/#330/#334).</summary>
    internal static string DescribeSetCookies(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out var values))
            return "<нет>";
        return string.Join(" | ", values.Select(DescribeSetCookieHeader));
    }

    /// <summary>Превращает один заголовок <c>Set-Cookie</c> в строку «имя; атрибуты» БЕЗ
    /// значения: <c>JSESSIONID; HttpOnly; Secure; Path=/; Domain=login.1c.ru</c>.</summary>
    internal static string DescribeSetCookieHeader(string header)
    {
        if (string.IsNullOrWhiteSpace(header))
            return "<пустой>";

        var parts = header.Split(';');
        var name = parts[0].Split('=')[0].Trim();
        var flags = new List<string>();
        for (var i = 1; i < parts.Length; i++)
        {
            var p = parts[i].Trim();
            if (p.Length == 0)
                continue;

            var eq = p.IndexOf('=');
            var attrName = eq > 0 ? p.Substring(0, eq).Trim() : p;
            var attrValue = eq > 0 ? p.Substring(eq + 1).Trim() : string.Empty;
            if (string.Equals(attrName, "httponly", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(attrName, "secure", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(attrName, "path", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(attrName, "domain", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(attrName, "samesite", StringComparison.OrdinalIgnoreCase))
            {
                flags.Add(attrValue.Length > 0 ? $"{attrName}={attrValue}" : attrName);
            }
        }

        return name.Length == 0 ? "<безымянная>" : flags.Count == 0 ? name : $"{name}; {string.Join("; ", flags)}";
    }

    /// <summary>
    /// Выбирает адрес формы входа: полный URL редиректа сервера (<c>login.1c.ru/login?service=…</c>),
    /// если он валиден и ведёт на login.1c.ru, иначе базовый <see cref="PortalLoginUrl"/>.
    /// </summary>
    private static string ResolveLoginFormUrl(string? loginUrl)
    {
        if (!string.IsNullOrWhiteSpace(loginUrl) &&
            Uri.TryCreate(loginUrl, UriKind.Absolute, out var uri) &&
            uri.Host.Contains("login.1c.ru", StringComparison.OrdinalIgnoreCase))
        {
            return uri.AbsoluteUri;
        }

        return PortalLoginUrl;
    }

    /// <summary>
    /// Следует за цепочкой редиректов после POST входа (GET по Location), пока не будет получен
    /// финальный ответ вне страницы входа. Каждый шаг журналируется (единая диагностика issue #323).
    /// Cookie из ответов накапливаются в общем <see cref="CookieContainer"/>.
    /// Возвращает true, если цепочка завершилась успешно (вход подтверждён).
    /// </summary>
    private async Task<bool> FollowLoginRedirectsAsync(Uri location, CancellationToken ct)
    {
        var current = location;
        for (var i = 0; i < MaxRedirects; i++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, current);
            using var response =
                await _httpClient.SendAsync(request, HttpCompletionOption.ResponseContentRead, ct).ConfigureAwait(false);
            var status = (int)response.StatusCode;
            var next = response.Headers.Location;

            if (status is >= 300 and < 400 && next is not null)
            {
                _logger.Info($"[Updates] Редирект входа (шаг {i + 1}): {status} '{next}' для '{current}'");
                current = next.IsAbsoluteUri ? next : new Uri(current, next);
                continue;
            }

            // Конец цепочки: успех — финальный ответ вне страницы входа И тело НЕ содержит
            // форму входа (CAS может вернуть 200 с формой вместо целевого контента —
            // «фантомный успех», issue #330).
            var finalHost = response.RequestMessage?.RequestUri?.Host ?? current.Host;
            if (status < 400 && !finalHost.Contains("login.1c.ru", StringComparison.OrdinalIgnoreCase))
            {
                var body = await ReadBodyQuietlyAsync(response, ct).ConfigureAwait(false);
                if (!LooksLikeLoginForm(body))
                    return true;

                _logger.Warn("[Updates] Цепочка входа завершилась 200 со страницей входа (вход не подтверждён).");
                return false;
            }

            _logger.Warn($"[Updates] Цепочка входа завершилась на странице входа (status={status}, host='{finalHost}').");
            return false;
        }

        _logger.Warn("[Updates] Слишком много перенаправлений после входа на portal.1c.ru.");
        return false;
    }

    /// <summary>Отображаемое имя учётной записи для журнала входа (без пароля):
    /// имя записи справочника, иначе логин, иначе «не задана».</summary>
    private string ResolveAccountName()
    {
        var settings = _repository.LoadSettings();
        var account = _itsAccounts?.Resolve(settings.ItsAccountId);
        if (account is not null)
        {
            return string.IsNullOrWhiteSpace(account.Name)
                ? (account.Login ?? string.Empty)
                : account.Name!;
        }

        return string.IsNullOrWhiteSpace(settings.UpdatesLogin)
            ? "не задана"
            : settings.UpdatesLogin!;
    }

    /// <summary>
    /// Извлекает поля формы входа (<c><input type="hidden"></c> и отмеченные
    /// <c>checkbox</c>) из HTML: имя → значение. Устойчиво к порядку атрибутов и кавычкам
    /// ('…' / "…"). Возвращает все скрытые поля, чтобы POST входа собирался динамически
    /// (execution, lt, CSRF и пр.) — жёсткий список полей отклоняется сервером 401 при
    /// изменении формы (issue #334). Пустой/битый HTML — пустой словарь.
    /// </summary>
    internal static Dictionary<string, string> ExtractFormFields(string html)
    {
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(html))
            return fields;

        foreach (Match tag in InputTagRegex.Matches(html))
        {
            var type = GetAttribute(tag.Value, "type") ?? string.Empty;
            var name = GetAttribute(tag.Value, "name");
            if (string.IsNullOrWhiteSpace(name))
                continue;

            var isHidden = string.Equals(type, "hidden", StringComparison.OrdinalIgnoreCase);
            var isCheckedCheckbox = string.Equals(type, "checkbox", StringComparison.OrdinalIgnoreCase)
                                    && Regex.IsMatch(tag.Value, @"\bchecked\b", RegexOptions.IgnoreCase);
            if (!isHidden && !isCheckedCheckbox)
                continue;

            fields[name!] = GetAttribute(tag.Value, "value") ?? string.Empty;
        }

        return fields;
    }

    /// <summary>Регулярное выражение тега <c><input …></c> (включая самозакрывающиеся).</summary>
    private static readonly Regex InputTagRegex =
        new(@"<input\b[^>]*/?>", RegexOptions.IgnoreCase | RegexOptions.Singleline);

    /// <summary>Значение атрибута тега (кавычки '…' / "…" или без кавычек) либо null.</summary>
    private static string? GetAttribute(string tag, string attributeName)
    {
        var pattern = $@"\b{Regex.Escape(attributeName)}\s*=\s*(?:""(?<v>[^""]*)""|'(?<v>[^']*)'|(?<v>[^\s>]+))";
        var match = Regex.Match(tag, pattern, RegexOptions.IgnoreCase | RegexOptions.Singleline);
        return match.Success ? match.Groups["v"].Value : null;
    }

    /// <summary>
    /// Извлекает атрибут <c>action</c> формы входа (issue #323/#330/#334, третья итерация):
    /// адрес, на который отправляется POST. Spring Security CAS часто указывает action,
    /// отличный от URL GET-формы (<c>/login/cas?service=…</c>) — POST «на адрес GET» уходил
    /// не туда (401/404). Возвращает null, если action отсутствует/пуст/равен "#".
    /// </summary>
    internal static string? ExtractFormAction(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return null;

        foreach (Match tag in FormTagRegex.Matches(html))
        {
            var action = GetAttribute(tag.Value, "action");
            if (string.IsNullOrWhiteSpace(action) || action == "#")
                continue;
            return action;
        }

        return null;
    }

    /// <summary>Регулярное выражение открывающего тега <c><form …></c>.</summary>
    private static readonly Regex FormTagRegex =
        new(@"<form\b[^>]*>", RegexOptions.IgnoreCase | RegexOptions.Singleline);

    /// <summary>
    /// Адрес POST формы входа: атрибут <c>action</c> формы, резолвленный относительно адреса
    /// GET-формы, либо сам адрес GET-формы, если action отсутствует/пуст/"#" (прежнее поведение).
    /// Referer/Origin при этом остаются на адресе GET-формы — часть CAS-развёртываний проверяет
    /// их при POST (см. TryLoginPortalAsync).
    /// </summary>
    private static string ResolveFormPostUrl(string formUrl, string? action)
    {
        if (string.IsNullOrWhiteSpace(action))
            return formUrl;
        if (Uri.TryCreate(action, UriKind.Absolute, out var abs))
            return abs.AbsoluteUri;
        if (Uri.TryCreate(new Uri(formUrl), action, out var rel))
            return rel.AbsoluteUri;
        return formUrl;
    }

    /// <summary>
    /// Признаки того, что форма входа изменилась радикально — OAuth/JS-челлендж вместо
    /// классической CAS-формы с <c>execution</c>/<c>lt</c> (issue #323/#330/#334): маркеры
    /// <c>oauth</c>/<c>client_id</c>/<c>challenge</c>/<c>csrf</c>. Программный POST классической
    /// формы в таком случае невозможен — нужен браузер (или импорт cookie, будущая итерация).
    /// </summary>
    internal static bool LooksLikeOAuthOrChallenge(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return false;
        return ContainsAny(body, "oauth", "client_id", "challenge", "csrf");
    }

    /// <summary>
    /// Логирует анонимизированные признаки неудачной авторизации: размер тела, маркеры
    /// ошибки, имена полей формы. Значения (пароль, логин, execution/lt-токены) НЕ выводятся.
    /// </summary>
    private void LogAnonymizedAuthFailure(string message, string body, IReadOnlyDictionary<string, string> fields)
    {
        var len = string.IsNullOrEmpty(body) ? 0 : body.Length;
        var markers = DetectAuthFailureMarkers(body);
        var fieldNames = fields.Count == 0
            ? string.Empty
            : string.Join(", ", fields.Keys.OrderBy(k => k, StringComparer.Ordinal));
        _logger.Warn($"{message} (body_len={len}" +
                     $"{(markers.Length > 0 ? $", признаки: {markers}" : string.Empty)}" +
                     $"{(fieldNames.Length > 0 ? $", поля формы: {fieldNames}" : string.Empty)}).");
    }

    /// <summary>
    /// Логирует расширенную диагностику ветки 2xx POST входа (issue #323/#330/#334):
    /// contentType, длину тела, превью первых ~300 символов (БЕЗ секретов — значения полей
    /// формы, логин и пароль удаляются) и Set-Cookie только именами/флагами.
    /// </summary>
    private void LogPost2xxDiagnostics(
        int status,
        HttpResponseMessage response,
        string body,
        IReadOnlyDictionary<string, string> fields,
        string login,
        string password)
    {
        var contentType = response.Content?.Headers.ContentType?.ToString() ?? "<нет>";
        var bodyLength = string.IsNullOrEmpty(body) ? 0 : body.Length;
        var preview = SanitizeBodyPreview(body, fields, login, password);
        _logger.Info($"[Updates] Вход: POST 2xx диагностика status={status}, contentType='{contentType}', " +
                     $"bodyLength={bodyLength}, bodyPreview='{preview}'");
        _logger.Info($"[Updates] Вход: POST Set-Cookie: {DescribeSetCookies(response)}");
    }

    /// <summary>Превью тела для журнала (первые ~300 символов) с удалением секретов:
    /// значений полей формы (execution/lt/csrf), логина и пароля, значений атрибутов
    /// <c>value</c> у input-тегов и пар name=значение чувствительных полей. Управляющие
    /// символы заменяются пробелами — превью остаётся одной строкой.</summary>
    private static string SanitizeBodyPreview(
        string body, IReadOnlyDictionary<string, string> fields, string login, string password)
    {
        if (string.IsNullOrEmpty(body))
            return string.Empty;

        var text = body.Length > 300 ? body.Substring(0, 300) : body;

        // Известные секреты: значения полей формы, логин и пароль.
        var secrets = new List<string>();
        foreach (var value in fields.Values)
        {
            if (!string.IsNullOrWhiteSpace(value) && value.Length >= 3)
                secrets.Add(value);
        }

        if (!string.IsNullOrWhiteSpace(login) && login.Length >= 3)
            secrets.Add(login);
        if (!string.IsNullOrWhiteSpace(password) && password.Length >= 3)
            secrets.Add(password);

        foreach (var secret in secrets.Distinct(StringComparer.Ordinal))
            text = text.Replace(secret, "<...>", StringComparison.Ordinal);

        // Значения атрибутов value любых input скрываются целиком (токены в теле POST-ответа
        // могут отличаться от полей GET-формы).
        text = Regex.Replace(text, @"\bvalue\s*=\s*(?:""[^""]*""|'[^']*')",
            "value=\"<...>\"", RegexOptions.IgnoreCase);

        // Пары name=значение в form-urlencoded контексте для чувствительных полей.
        text = Regex.Replace(text,
            @"\b(execution|lt|csrf|_csrf|password|username|j_password)\s*=\s*[^&\s""'<>]+",
            "$1=<...>", RegexOptions.IgnoreCase);

        return Regex.Replace(text, @"[\r\n\t]+", " ");
    }

    /// <summary>Определяет по тексту тела ответа вероятную причину отклонения входа
    /// (без вывода самого текста): неверный логин/пароль, капча, наличие полей lt/execution/csrf.</summary>
    private static string DetectAuthFailureMarkers(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return string.Empty;

        var found = new List<string>();
        if (ContainsAny(body, "Неверный логин", "неверные учётные данные", "incorrect",
                "invalid username", "invalid credentials", "bad credentials", "authentication failed"))
            found.Add("неверный логин/пароль");
        if (ContainsAny(body, "captcha", "капч", "recaptcha"))
            found.Add("капча");
        if (body.Contains("name=\"lt\"", StringComparison.OrdinalIgnoreCase) ||
            body.Contains("name='lt'", StringComparison.OrdinalIgnoreCase))
            found.Add("поле lt");
        if (body.Contains("execution", StringComparison.OrdinalIgnoreCase))
            found.Add("execution");
        if (body.Contains("csrf", StringComparison.OrdinalIgnoreCase))
            found.Add("csrf");
        // Маркеры изменённой формы (OAuth/JS-челлендж, issue #323/#330/#334): по ним
        // распознаётся «протокол изменился» — автоматический вход невозможен.
        if (body.Contains("oauth", StringComparison.OrdinalIgnoreCase))
            found.Add("oauth");
        if (body.Contains("client_id", StringComparison.OrdinalIgnoreCase))
            found.Add("client_id");
        if (body.Contains("challenge", StringComparison.OrdinalIgnoreCase))
            found.Add("challenge");
        return string.Join(", ", found);
    }

    /// <summary>True — текст содержит хотя бы одну из подстрок (без учёта регистра).</summary>
    private static bool ContainsAny(string text, params string[] candidates)
    {
        foreach (var candidate in candidates)
        {
            if (text.Contains(candidate, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Определяет, является ли тело ответа страницей входа на portal.1c.ru (гибридная
    /// авторизация CAS): наличие полей формы <c>execution</c>/<c>lt</c> (из
    /// <see cref="ExtractFormFields"/>), текстового поля <c>username</c>, маркеров ошибки
    /// авторизации (<see cref="DetectAuthFailureMarkers"/>) либо прямого упоминания
    /// <c>login.1c.ru</c>. Используется для распознавания «фантомного успеха» при HTTP 200
    /// с формой входа вместо целевого контента (issue #330/#334) и страницы входа при 200.
    /// </summary>
    internal static bool LooksLikeLoginForm(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return false;

        // Скрытые поля формы входа CAS: execution/lt — динамический токен сессии.
        var fields = ExtractFormFields(body);
        if (fields.ContainsKey("execution") || fields.ContainsKey("lt"))
            return true;

        // Текстовое поле имени пользователя формы входа.
        if (body.Contains("name=\"username\"", StringComparison.OrdinalIgnoreCase) ||
            body.Contains("name='username'", StringComparison.OrdinalIgnoreCase))
            return true;

        // Маркеры ошибки авторизации: неверный логин/пароль, капча, ссылки на форму.
        if (!string.IsNullOrEmpty(DetectAuthFailureMarkers(body)))
            return true;

        // Прямое упоминание портала входа в теле ответа.
        return body.Contains("login.1c.ru", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Ищет в теле ответа 2xx признак JS/meta-refresh-редиректа и извлекает целевой URL
    /// (issue #323/#330/#334): CAS-цепочка часто доводится до
    /// <c>releases.1c.ru/public/security_check?ticket=…</c> именно JS-редиректом, где
    /// выставляется сессионная cookie. Маркеры: <c><meta http-equiv="refresh"></c>,
    /// <c>window.location</c>, <c>location.href</c>, <c>document.location</c>, <c>top.location</c>.
    /// Возвращает URL (HTML-декодированный) или null.
    /// </summary>
    internal static string? ExtractBodyRedirectUrl(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return null;

        // 1) <meta http-equiv="refresh" content="N; url=..."> — порядок атрибутов произвольный.
        foreach (Match tag in MetaRefreshTagRegex.Matches(body))
        {
            var contentMatch = Regex.Match(tag.Value,
                @"content\s*=\s*[""'](?<content>[^""']*)[""']",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (!contentMatch.Success)
                continue;

            var urlMatch = Regex.Match(contentMatch.Groups["content"].Value,
                @"url\s*=\s*(?<url>[^;""'\s]+)",
                RegexOptions.IgnoreCase);
            if (urlMatch.Success)
                return WebUtility.HtmlDecode(urlMatch.Groups["url"].Value.Trim());
        }

        // 2) JS-редирект: window.location[.href|.replace](...) / document.location /
        //    top.location / location.href — присваивание или вызов.
        var js = Regex.Match(body,
            @"(?:\b(?:window|document|top)\s*\.\s*location|\blocation)(?:\s*\.\s*(?:href|replace))?\s*[=(]\s*[""'](?<url>[^""']+)[""']",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (js.Success)
            return WebUtility.HtmlDecode(js.Groups["url"].Value.Trim());

        return null;
    }

    /// <summary>Регулярное выражение тега <c><meta http-equiv="refresh" …></c>.</summary>
    private static readonly Regex MetaRefreshTagRegex =
        new(@"<meta\b[^>]*http-equiv\s*=\s*[""']refresh[""'][^>]*>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);

    /// <summary>Резолвит URL из JS/meta-refresh-редиректа относительно адреса POST формы.</summary>
    private static Uri? ResolveBodyRedirectTarget(string baseUrl, string rawUrl)
    {
        if (Uri.TryCreate(rawUrl, UriKind.Absolute, out var abs))
            return abs;
        if (Uri.TryCreate(new Uri(baseUrl), rawUrl, out var rel))
            return rel;
        return null;
    }

    /// <summary>
    /// True — в общем хранилище cookie есть сессионная cookie портала 1С
    /// (<c>JSESSIONID</c>/<c>TGC</c>/<c>session_id</c>), выставленная после успешного входа
    /// на login.1c.ru. Используется как подтверждение успеха входа и ранний выход из
    /// повторного логина (issue #330/#334): если сессия уже установлена — вход не нужен.
    /// </summary>
    private bool HasPortalSessionCookie()
    {
        foreach (var host in new[] { "login.1c.ru", "releases.1c.ru" })
        {
            try
            {
                foreach (Cookie cookie in _cookieContainer.GetCookies(new Uri($"https://{host}/")))
                {
                    var name = cookie.Name ?? string.Empty;
                    if (name.Equals("TGC", StringComparison.OrdinalIgnoreCase) ||
                        name.StartsWith("JSESSIONID", StringComparison.OrdinalIgnoreCase) ||
                        name.Contains("session_id", StringComparison.OrdinalIgnoreCase) ||
                        name.Equals("SESSION", StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }
            catch
            {
                // Некорректный URI/иные ошибки хранилища не должны ронять вход.
            }
        }

        return false;
    }

    /// <summary>Логирует перечень cookie общего хранилища для хостов портала 1С (имена и
    /// атрибуты, БЕЗ значений) — диагностика входа (issue #323/#330/#334).</summary>
    private void LogPortalCookieInventory()
        => _logger.Info($"[Updates] Вход: cookie контейнера: {DescribeContainerCookies()}");

    /// <summary>Имена и атрибуты (без значений) cookie в общем хранилище для hosts
    /// <c>login.1c.ru</c>/<c>releases.1c.ru</c> — строка для журнала.</summary>
    internal string DescribeContainerCookies()
    {
        var entries = new List<string>();
        foreach (var host in new[] { "login.1c.ru", "releases.1c.ru" })
        {
            try
            {
                var cookies = _cookieContainer.GetCookies(new Uri($"https://{host}/"));
                if (cookies.Count == 0)
                {
                    entries.Add($"{host}=<нет>");
                    continue;
                }

                var names = new List<string>();
                foreach (Cookie cookie in cookies)
                {
                    var attrs = new List<string>();
                    if (cookie.Secure) attrs.Add("Secure");
                    if (cookie.HttpOnly) attrs.Add("HttpOnly");
                    if (!string.IsNullOrEmpty(cookie.Path)) attrs.Add($"Path={cookie.Path}");
                    if (!string.IsNullOrEmpty(cookie.Domain)) attrs.Add($"Domain={cookie.Domain}");
                    names.Add(attrs.Count == 0 ? cookie.Name : $"{cookie.Name}{{{string.Join(",", attrs)}}}");
                }

                entries.Add($"{host}={string.Join("|", names)}");
            }
            catch
            {
                entries.Add($"{host}=<ошибка чтения>");
            }
        }

        return string.Join("; ", entries);
    }

    /// <summary>Выбирает ключ локализации ошибки авторизации для результатов проверок:
    /// «лимит попыток исчерпан» — отдельный ключ (issue #334/#330/#323); вход предпринимался
    /// и не подтверждён сервером — «вход не подтверждён (401)»; иначе — «требуется вход».</summary>
    private string AuthErrorKey(bool authFailed)
        => IsPortalLoginLimitReached() ? "Updates.LoginLimitReached"
            // Форма входа изменилась/недоступна (OAuth/JS-челлендж, issue #323/#330/#334) —
            // отдельный ключ с понятным текстом и советом открыть login.1c.ru в браузере.
            : _lastLoginResult == PortalLoginResult.FormUnavailable ? "Updates.FormUnavailable"
            : authFailed ? "Updates.AuthFailed"
            : "Updates.AuthRequired";
}