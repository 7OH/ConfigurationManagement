using System;
using System.IO;
using System.Linq;
using System.Net;
using Configuration_Management.Models;
using Configuration_Management.Services;
using Xunit;

namespace ConfigurationManagement.Tests;

/// <summary>
/// Тесты цепочки «связанная конфигурация / свойства конфигурации базы → URL каталога релизов →
/// парсинг ответа» для окна проверки обновлений F9 (issue #323): каталог строится из свойств
/// конфигурации (вкладка «Платформа»), явное связывание остаётся override, а при пустом нике
/// на releases.1c.ru проверка честно сообщает причину.
/// </summary>
public sealed class UpdateCheckCatalogTests : IDisposable
{
    private readonly string _tempDir;

    public UpdateCheckCatalogTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "cm_update_check_catalog_tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, recursive: true);
        }
        catch
        {
            // Игнорируем: каталог мог быть занят или уже удалён.
        }
    }

    private CustomConfigTypesStore CreateStore() => new(directoryOverride: _tempDir);

    /// <summary>Воспроизводит шаг «после связывания»: запись с кодом связи в файле + поиск по коду.</summary>
    private static OneCConfigType? FindLinked(ICustomConfigTypesStore store, string code) =>
        store.LoadAll().FirstOrDefault(c => string.Equals(c.Code, code, StringComparison.OrdinalIgnoreCase));

    [Fact]
    public void AfterLinking_UrlBuiltFromConfigNick()
    {
        var store = CreateStore();
        // Пользователь дополнил встроенную ЗУП (правка предопределённой строки): создана
        // копия-переопределение с кодом ZUP и ником на releases.1c.ru — F9 после связывания
        // находит её по коду связи и строит каталог из ника.
        store.Save(new[]
        {
            new OneCConfigType
            {
                Code = "ZUP",
                Name = "Зарплата и управление персоналом",
                Nick = "Zup31Nick",
                OverridesBuiltIn = true,
                Editions = { new OneCConfigEdition { Name = "3.1", Red = "3.1" } },
            },
        });

        var linked = FindLinked(store, "ZUP");
        Assert.NotNull(linked);

        var url = new OneCUpdatesService(
                repository: new InfobaseRepository(directory: _tempDir),
                logger: new TestLogger())
            .BuildUpdateUrl(linked, linked!.DefaultEdition, null, null);

        Assert.Equal("https://releases.1c.ru/project/Zup31Nick", url);
    }

    [Fact]
    public void PersonalSegment_OverridesConfigNick()
    {
        var store = CreateStore();
        store.Save(new[]
        {
            new OneCConfigType
            {
                Code = "BP",
                Name = "Бухгалтерия предприятия",
                Nick = "AccountingCorp30",
                // Пользовательская копия встроенной: в общем списке заменяет встроенную БП
                // (иначе FindLinked вернул бы встроенную с ником Accounting из 0.3.9.297).
                OverridesBuiltIn = true,
            },
        });

        var linked = FindLinked(store, "BP")!;
        var service = new OneCUpdatesService(new InfobaseRepository(directory: _tempDir), new TestLogger());

        var url = service.BuildUpdateUrl(linked, linked.DefaultEdition, null, "MyPersonalSegment");

        Assert.Equal("https://releases.1c.ru/project/MyPersonalSegment", url);
    }

    [Fact]
    public void PropertiesFromPlatformTab_AutoMatchConfig_BuildsUrl()
    {
        // База НЕ связана, но свойства конфигурации определены (вкладка «Платформа»):
        // каталог релизов строится из них автоматически (issue #323). Пользовательская ЗУП
        // с ником переопределяет встроенную — в общем списке она и подбирается по имени.
        var store = CreateStore();
        store.Save(new[]
        {
            new OneCConfigType
            {
                Code = "ZUP",
                Name = "Зарплата и управление персоналом",
                Nick = "Zup30Nick",
                OverridesBuiltIn = true,
                Editions = { new OneCConfigEdition { Name = "3.0", Red = "3.0" } },
            },
        });

        var config = ConfigTypeMatcher.FindByInfobaseName(store.LoadAll(), "Зарплата и управление персоналом");
        Assert.NotNull(config);

        var service = new OneCUpdatesService(new InfobaseRepository(directory: _tempDir), new TestLogger());
        var url = service.BuildUpdateUrl(config, config!.DefaultEdition, null, null);

        Assert.Equal("https://releases.1c.ru/project/Zup30Nick", url);
    }

    [Fact]
    public void LinkedConfigWithoutNick_ReturnsEmptyUrl()
    {
        // Конфигурация без ника: URL построить нельзя — проверка должна завершиться
        // понятной ошибкой, а не «молчать». (Встроенная ЗУП с 0.3.9.297 имеет ник HRM30 —
        // проверяем на пользовательской записи без ника.)
        var store = CreateStore();
        store.Save(new[]
        {
            new OneCConfigType
            {
                Code = "NO_NICK",
                Name = "Без ника",
                Nick = string.Empty,
                Editions = { new OneCConfigEdition { Name = "1.0", Red = "1.0" } },
            },
        });
        var linked = FindLinked(store, "NO_NICK");
        Assert.NotNull(linked);
        Assert.True(string.IsNullOrWhiteSpace(linked!.Nick));

        var service = new OneCUpdatesService(new InfobaseRepository(directory: _tempDir), new TestLogger());
        var url = service.BuildUpdateUrl(linked, linked.DefaultEdition, null, null);

        Assert.Equal(string.Empty, url);
    }

    // ---------- Парсер HTML-ответа каталога releases.1c.ru/project/<ник> (issue #323). ----------

    [Fact]
    public void ParseProjectHtml_TakesFirstRowOfVersionsTable()
    {
        const string html = """
            <html><body>
            <table id="versionsTable">
              <tr><td>1</td><td><a href="/version_files?nick=Zup31Nick&ver=3.1.14.1">3.1.14.1</a></td></tr>
              <tr><td>2</td><td><a href="/version_files?nick=Zup31Nick&ver=3.1.13.5">3.1.13.5</a></td></tr>
            </table>
            </body></html>
            """;

        var version = OneCUpdatesService.ParseLatestVersionFromProjectHtml(html);

        Assert.Equal("3.1.14.1", version);
    }

    [Fact]
    public void ParseProjectHtml_Fallback_WhenNoTable()
    {
        const string html =
            "<div><a href=\"/version_files?nick=X&ver=8.3.24.1646\">8.3.24.1646</a></div>";

        var version = OneCUpdatesService.ParseLatestVersionFromProjectHtml(html);

        Assert.Equal("8.3.24.1646", version);
    }

    [Fact]
    public void ParseProjectHtml_EmptyOrGarbage_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, OneCUpdatesService.ParseLatestVersionFromProjectHtml(null!));
        Assert.Equal(string.Empty, OneCUpdatesService.ParseLatestVersionFromProjectHtml("   "));
        Assert.Equal(string.Empty, OneCUpdatesService.ParseLatestVersionFromProjectHtml("<html>нет версий</html>"));
    }

    [Fact]
    public void ParseArchiveLinks_TakesMaxVersion()
    {
        // Имена дистрибутивов вида 1c_<версия>.zip (паттерн ArchiveLinkRegex: «1c…zip»).
        const string html =
            "<a href='https://cdn.example/1c_3.0.13.7.zip'>v1</a>" +
            "<a href='https://cdn.example/1c_3.0.15.2.zip'>v2</a>" +
            "<a href='https://cdn.example/1c_3.0.9.1.zip'>v3</a>";

        var version = OneCUpdatesService.ParseLatestVersion(html);

        Assert.Equal("3.0.15.2", version);
    }

    // ---------- HTTP 302 от releases.1c.ru и авторизация портала 1С (issue #323/#334) ----------

    [Fact]
    public async System.Threading.Tasks.Task CheckForUpdatesAsync_302WithoutLocation_ReturnsAuthRequired()
    {
        // releases.1c.ru при отсутствии сессии может вернуть 302 БЕЗ заголовка Location
        // (CAS). Учётные данные не настроены — вход невозможен: проверка должна завершиться
        // понятной ошибкой авторизации, а не техническим «HTTP 302».
        var handler = new StaticHandler(new HttpResponseMessage(HttpStatusCode.Found));
        var service = new OneCUpdatesService(CreateRepo(), new TestLogger(), handler);

        var result = await service.CheckForUpdatesAsync(
            "Бухгалтерия предприятия", "3.0.120.1", "https://releases.1c.ru/project/AccountingCorp30");

        Assert.Equal(ConfigUpdateStatus.Failed, result.Status);
        Assert.Equal("Updates.AuthRequired", result.Error);
    }

    [Fact]
    public async System.Threading.Tasks.Task CheckForUpdatesAsync_302ToLoginHost_ReturnsAuthRequired()
    {
        // Классический CAS: редирект на login.1c.ru. Учётных данных нет — вход не выполнен,
        // проверка завершается понятной ошибкой авторизации.
        var response = new HttpResponseMessage(HttpStatusCode.Found);
        response.Headers.Location = new Uri("https://login.1c.ru/login");
        var handler = new StaticHandler(response);
        var service = new OneCUpdatesService(CreateRepo(), new TestLogger(), handler);

        var result = await service.CheckForUpdatesAsync(
            "Бухгалтерия предприятия", "3.0.120.1", "https://releases.1c.ru/project/AccountingCorp30");

        Assert.Equal(ConfigUpdateStatus.Failed, result.Status);
        Assert.Equal("Updates.AuthRequired", result.Error);
    }

    [Fact]
    public async System.Threading.Tasks.Task CheckForUpdatesAsync_302WithoutLocation_LoginThenRetry_Succeeds()
    {
        // Сценарий #323/#334 с настроенными учётными данными: первый запрос каталога получает
        // 302 без Location → служба выполняет вход на portal.1c.ru (GET формы + POST) и повторяет
        // запрос каталога — тот отвечает 200 с таблицей версий.
        var handler = new AuthFlowHandler();
        var repo = CreateRepo();
        repo.Settings.UpdatesLogin = "its-user";
        repo.Settings.UpdatesPassword = "secret";
        var service = new OneCUpdatesService(repo, new TestLogger(), handler);

        var result = await service.CheckForUpdatesAsync(
            "Бухгалтерия предприятия", "3.0.120.1", "https://releases.1c.ru/project/AccountingCorp30");

        Assert.Equal(ConfigUpdateStatus.NewerAvailable, result.Status);
        Assert.Equal("3.0.130.1", result.LatestVersion);
        Assert.True(handler.ProjectRequests >= 2, "Запрос каталога должен быть повторён после входа.");
    }

    [Fact]
    public async System.Threading.Tasks.Task CheckForUpdatesAsync_200WithProjectHtml_ReturnsNewerAvailable()
    {
        // Зелёный путь без редиректов: 200 с HTML каталога — находится более новая версия.
        const string html = """
            <html><body>
            <table id="versionsTable">
              <tr><td>1</td><td><a href="/version_files?nick=AccountingCorp30&ver=3.0.130.1">3.0.130.1</a></td></tr>
            </table>
            </body></html>
            """;
        var handler = new StaticHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(html),
        });
        var service = new OneCUpdatesService(CreateRepo(), new TestLogger(), handler);

        var result = await service.CheckForUpdatesAsync(
            "Бухгалтерия предприятия", "3.0.120.1", "https://releases.1c.ru/project/AccountingCorp30");

        Assert.Equal(ConfigUpdateStatus.NewerAvailable, result.Status);
        Assert.Equal("3.0.130.1", result.LatestVersion);
    }

    [Fact]
    public async System.Threading.Tasks.Task CheckForUpdatesAsync_302WithoutLocation_CredentialsFromItsAccountsStore_Succeeds()
    {
        // #334/#333: учётные данные ИТС берутся из справочника (its_accounts.json), а НЕ из
        // устаревших полей настроек. Старые поля пусты — вход всё равно должен выполниться.
        var repo = CreateRepo();
        var accounts = new ItsAccountsStore(repository: repo, profileService: null, directoryOverride: _tempDir);
        accounts.Upsert(new ItsAccount { Name = "Основная", Login = "store-user", Password = "store-pwd" });

        var handler = new AuthFlowHandler();
        var service = new OneCUpdatesService(repo, new TestLogger(), handler, accounts);

        var result = await service.CheckForUpdatesAsync(
            "Бухгалтерия предприятия", "3.0.120.1", "https://releases.1c.ru/project/AccountingCorp30");

        Assert.Equal(ConfigUpdateStatus.NewerAvailable, result.Status);
        Assert.Equal("3.0.130.1", result.LatestVersion);
        Assert.True(handler.ProjectRequests >= 2, "Запрос каталога должен быть повторён после входа.");
    }

    [Fact]
    public async System.Threading.Tasks.Task CheckForUpdatesAsync_302ToLoginWithService_CredentialsFromStore_SecurityCheckCompleted()
    {
        // #323/#334: полная CAS-цепочка — releases.1c.ru редиректит на login.1c.ru?service=…,
        // вход выполняется кредами из справочника, после POST служба следует за редиректом
        // на releases.1c.ru/public/security_check?ticket=… (именно там выставляется cookie),
        // затем повторяет исходный запрос каталога.
        var repo = CreateRepo();
        var accounts = new ItsAccountsStore(repository: repo, profileService: null, directoryOverride: _tempDir);
        accounts.Upsert(new ItsAccount { Name = "Основная", Login = "store-user", Password = "store-pwd" });

        var handler = new CasFlowHandler();
        var logger = new TestLogger();
        var service = new OneCUpdatesService(repo, logger, handler, accounts);

        var result = await service.CheckForUpdatesAsync(
            "Бухгалтерия предприятия", "3.0.120.1", "https://releases.1c.ru/project/AccountingCorp30");

        Assert.True(
            result.Status == ConfigUpdateStatus.NewerAvailable,
            $"status={result.Status}, error={result.Error}, securityChecks={handler.SecurityCheckRequests}, " +
            $"postBody={handler.LastPostBody ?? "<пусто>"}, requests=[{string.Join(" | ", handler.Log)}], " +
            $"log=[{string.Join(" | ", logger.Messages)}]");
        Assert.Equal("3.0.130.1", result.LatestVersion);
        Assert.Equal(1, handler.SecurityCheckRequests);
        Assert.Contains("username=store-user", handler.LastPostBody ?? string.Empty);
    }

    [Fact]
    public async System.Threading.Tasks.Task CheckForUpdatesAsync_EmptyStoreAndLegacy_ReturnsAuthRequired()
    {
        // Справочник ИТС пуст и старые поля настроек пусты: вход невозможен
        // («Для входа на portal.1c.ru не задан логин») — понятная ошибка авторизации.
        var repo = CreateRepo();
        var accounts = new ItsAccountsStore(repository: repo, profileService: null, directoryOverride: _tempDir);
        var handler = new StaticHandler(new HttpResponseMessage(HttpStatusCode.Found));
        var service = new OneCUpdatesService(repo, new TestLogger(), handler, accounts);

        var result = await service.CheckForUpdatesAsync(
            "Бухгалтерия предприятия", "3.0.120.1", "https://releases.1c.ru/project/AccountingCorp30");

        Assert.Equal(ConfigUpdateStatus.Failed, result.Status);
        Assert.Equal("Updates.AuthRequired", result.Error);
    }

    /// <summary>Репозиторий с настройками в памяти (для входных данных авторизации).</summary>
    private FakeRepo CreateRepo() => new();

    /// <summary>Логгер-заглушка для OneCUpdatesService (собирает сообщения для диагностики).</summary>
    private sealed class TestLogger : IAppLogger
    {
        public System.Collections.Generic.List<string> Messages { get; } = new();

        public void Info(string message) => Messages.Add(message);
        public void Warn(string message) => Messages.Add(message);
        public void Error(string message, Exception? exception = null)
        {
            Messages.Add(message);
            if (exception is not null)
                Messages.Add($"{exception.GetType().Name}: {exception.Message}");
        }
    }

    /// <summary>Обработчик, всегда возвращающий один и тот же ответ.</summary>
    private sealed class StaticHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public StaticHandler(HttpResponseMessage response)
            => _responder = _ => response;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = _responder(request);
            response.RequestMessage = request;
            return Task.FromResult(response);
        }
    }

    /// <summary>Fake репозитория: настройки портала в памяти, базы/группы пустые.</summary>
    private sealed class FakeRepo : IInfobaseRepository
    {
        public AppSettings Settings { get; set; } = new();

        public List<Infobase> Load() => new();

        public void Save(List<Infobase> infobases) { }

        public Task SaveAsync(List<Infobase> infobases, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public List<Group> LoadGroups() => new();

        public void SaveGroups(List<Group> groups) { }

        public Task SaveGroupsAsync(List<Group> groups, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public AppSettings LoadSettings() => Settings;

        public void SaveSettings(AppSettings settings) => Settings = settings;

        public Task SaveSettingsAsync(AppSettings settings, CancellationToken cancellationToken = default)
        {
            Settings = settings;
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// Обработчик сценария «302 без Location → вход на portal.1c.ru → повтор запроса»:
    /// первый запрос каталога возвращает 302 без Location, форма входа содержит токен
    /// <c>execution</c>, POST входа редиректит обратно на каталог, повторный запрос
    /// каталога возвращает 200 с таблицей версий.
    /// </summary>
    private sealed class AuthFlowHandler : HttpMessageHandler
    {
        private int _projectRequests;

        public int ProjectRequests => _projectRequests;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri?.ToString() ?? string.Empty;
            HttpResponseMessage response;

            if (url.Contains("/project/AccountingCorp30", StringComparison.OrdinalIgnoreCase))
            {
                _projectRequests++;
                response = _projectRequests == 1
                    ? new HttpResponseMessage(HttpStatusCode.Found) // 302 без Location
                    : Ok(HtmlWithVersions);
            }
            else if (url.Contains("/login", StringComparison.OrdinalIgnoreCase))
            {
                // GET формы входа → HTML с токеном; POST входа → редирект обратно на каталог.
                response = request.Method == HttpMethod.Post
                    ? Redirect(new Uri("https://releases.1c.ru/project/AccountingCorp30"))
                    : Ok("<form><input type=\"hidden\" name=\"execution\" value=\"e1s2\"/></form>");
            }
            else
            {
                response = new HttpResponseMessage(HttpStatusCode.Found);
            }

            response.RequestMessage = request;
            return Task.FromResult(response);
        }

        private static HttpResponseMessage Ok(string body) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(body),
        };

        private static HttpResponseMessage Redirect(Uri location)
        {
            var response = new HttpResponseMessage(HttpStatusCode.Found);
            response.Headers.Location = location;
            return response;
        }

        private const string HtmlWithVersions = """
            <html><body>
            <table id="versionsTable">
              <tr><td>1</td><td><a href="/version_files?nick=AccountingCorp30&ver=3.0.130.1">3.0.130.1</a></td></tr>
            </table>
            </body></html>
            """;
    }

    /// <summary>
    /// Обработчик полной CAS-цепочки: каталог → 302 на login.1c.ru?service=… → форма с токеном
    /// <c>execution</c> → POST логина → 302 на releases.1c.ru/public/security_check?ticket=… →
    /// GET security_check (200) → повтор каталога (200 с таблицей версий). Фиксирует тело POST
    /// входа (для проверки, что логин из справочника дошёл до формы).
    /// </summary>
    private sealed class CasFlowHandler : HttpMessageHandler
    {
        private int _projectRequests;
        private int _securityCheckRequests;

        public int SecurityCheckRequests => _securityCheckRequests;
        public string? LastPostBody { get; private set; }
        public System.Collections.Generic.List<string> Log { get; } = new();

        protected override async System.Threading.Tasks.Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri?.ToString() ?? string.Empty;
            // Путь без query: service=… у формы логина содержит "/public/security_check",
            // поэтому маршрутизация только по AbsolutePath (иначе форма трактуется как security_check).
            var path = request.RequestUri?.AbsolutePath ?? string.Empty;
            Log.Add($"{request.Method} {url}");
            HttpResponseMessage response;

            if (path.Contains("/project/AccountingCorp30", StringComparison.OrdinalIgnoreCase))
            {
                _projectRequests++;
                response = _projectRequests == 1
                    ? Redirect(new Uri("https://login.1c.ru/login?service=https://releases.1c.ru/public/security_check"))
                    : Ok(HtmlWithVersions);
            }
            else if (path.Contains("/public/security_check", StringComparison.OrdinalIgnoreCase))
            {
                _securityCheckRequests++;
                response = Ok("<html>session established</html>");
            }
            else if (path.Contains("/login", StringComparison.OrdinalIgnoreCase))
            {
                if (request.Method == HttpMethod.Post && request.Content is not null)
                {
                    LastPostBody = await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                    response = Redirect(new Uri("https://releases.1c.ru/public/security_check?ticket=ST-123"));
                }
                else
                {
                    response = Ok("<form><input type=\"hidden\" name=\"execution\" value=\"e1s2\"/></form>");
                }
            }
            else
            {
                response = new HttpResponseMessage(HttpStatusCode.Found);
            }

            response.RequestMessage = request;
            return response;
        }

        private static HttpResponseMessage Ok(string body) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(body),
        };

        private static HttpResponseMessage Redirect(Uri location)
        {
            var response = new HttpResponseMessage(HttpStatusCode.Found);
            response.Headers.Location = location;
            return response;
        }

        private const string HtmlWithVersions = """
            <html><body>
            <table id="versionsTable">
              <tr><td>1</td><td><a href="/version_files?nick=AccountingCorp30&ver=3.0.130.1">3.0.130.1</a></td></tr>
            </table>
            </body></html>
            """;
    }
}