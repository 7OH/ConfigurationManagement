using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Configuration_Management.Models;
using Configuration_Management.Services;
using Xunit;

namespace ConfigurationManagement.Tests;

/// <summary>
/// Тесты программного входа на portal.1c.ru (гибридная авторизация CAS) в
/// <see cref="OneCUpdatesService"/> (issue #334/#330/#323): динамический сбор полей формы
/// (execution + lt и пр.), различение AuthRequired/AuthFailed при 401, повторные попытки
/// входа вместо «одноразового» флага, лимит попыток и сброс при смене учётной записи,
/// анонимизированная диагностика (без пароля и значений токенов в журнале).
/// Сетевой стек — fake <see cref="HttpMessageHandler"/>, реальная сеть не используется.
/// </summary>
public sealed class OneCUpdatesLoginFlowTests
{
    private const string FormWithHiddenFields = """
        <html><body>
        <form id="fm1" action="/login" method="post">
          <input type="hidden" name="execution" value="e1s2t3" />
          <input type="hidden" name="lt" value="LT-123-abc" />
          <input type="checkbox" name="rememberMe" checked="checked" value="on" />
          <input type="checkbox" name="anotherComputer" value="on" />
          <input type="text" name="username" />
        </form>
        </body></html>
        """;

    private const string SimpleForm = """<form><input type="hidden" name="execution" value="e1s2"/></form>""";

    private const string VersionsTableHtml = """
        <html><body>
        <table id="versionsTable">
          <tr><td><a href="/version_files?nick=Platform83&ver=8.3.27.2214">8.3.27.2214</a></td></tr>
        </table>
        </body></html>
        """;

    // ---------- Чистый парсинг формы ----------

    [Fact]
    public void ExtractFormFields_CollectsHiddenAndCheckedCheckbox_IgnoresOthers()
    {
        var fields = OneCUpdatesService.ExtractFormFields(FormWithHiddenFields);

        Assert.Equal("e1s2t3", fields["execution"]);
        Assert.Equal("LT-123-abc", fields["lt"]);
        // Отмеченный checkbox попадает в словарь; неотмеченный и текстовое поле — нет.
        Assert.Equal("on", fields["rememberMe"]);
        Assert.False(fields.ContainsKey("anotherComputer"));
        Assert.False(fields.ContainsKey("username"));
    }

    [Fact]
    public void ExtractFormFields_EmptyOrBrokenHtml_ReturnsEmpty()
    {
        Assert.Empty(OneCUpdatesService.ExtractFormFields(null!));
        Assert.Empty(OneCUpdatesService.ExtractFormFields(string.Empty));
        Assert.Empty(OneCUpdatesService.ExtractFormFields("<html>нет формы</html>"));
    }

    [Fact]
    public void ExtractFormFields_SingleQuotedValues_Parsed()
    {
        var fields = OneCUpdatesService.ExtractFormFields(
            "<input type='hidden' name='execution' value='v1'/>");

        Assert.Equal("v1", fields["execution"]);
    }

    // ---------- POST формы: динамический набор полей ----------

    [Fact]
    public async Task LoginPost_IncludesAllHiddenFormFields()
    {
        var handler = new LoginCaptureHandler(FormWithHiddenFields, postStatus: HttpStatusCode.Found,
            location: new Uri("https://releases.1c.ru/public/security_check?ticket=ST-1"));
        var service = CreateService(handler, login: "user1", password: "p@ss word");

        var result = await service.FetchPageAsync("https://releases.1c.ru/project/Platform83");

        Assert.Equal(PortalFetchStatus.Ok, result.Status);
        Assert.NotNull(handler.LastPostBody);
        Assert.Contains("username=user1", handler.LastPostBody!);
        Assert.Contains("password=p%40ss+word", handler.LastPostBody!);
        Assert.Contains("execution=e1s2t3", handler.LastPostBody!);
        Assert.Contains("lt=LT-123-abc", handler.LastPostBody!);
        Assert.Contains("_eventId=submit", handler.LastPostBody!);
    }

    // ---------- 401 → AuthFailed, без секретов в журнале ----------

    [Fact]
    public async Task LoginPost401_FetchPage_ReturnsAuthFailed()
    {
        var handler = new StaticLoginHandler(postStatus: HttpStatusCode.Unauthorized,
            postBody: "<html><body>Неверный логин и/или пароль</body></html>");
        var logger = new CollectingLogger();
        var service = CreateService(handler, logger, login: "user1", password: "sup3r-secret");

        var result = await service.FetchPageAsync("https://releases.1c.ru/project/Platform83");

        Assert.Equal(PortalFetchStatus.AuthFailed, result.Status);
        // В журнале есть признак причины, но НЕТ пароля и значений токенов формы.
        // (Логин может присутствовать как отображаемое имя учётной записи — по дизайну.)
        var joined = string.Join("\n", logger.Messages);
        Assert.Contains("признаки: неверный логин/пароль", joined);
        Assert.DoesNotContain("sup3r-secret", joined);
        Assert.DoesNotContain("e1s2", joined);
    }

    [Fact]
    public async Task LoginPost401_CheckForUpdates_ReturnsAuthFailedErrorKey()
    {
        var handler = new StaticLoginHandler(postStatus: HttpStatusCode.Unauthorized, postBody: "<html>ошибка</html>");
        var service = CreateService(handler, login: "user1", password: "secret");

        var result = await service.CheckForUpdatesAsync(
            "Бухгалтерия предприятия", "3.0.120.1", "https://releases.1c.ru/project/AccountingCorp30");

        Assert.Equal(ConfigUpdateStatus.Failed, result.Status);
        Assert.Equal("Updates.AuthFailed", result.Error);
    }

    // ---------- Повторные попытки вместо «одноразового» флага ----------

    [Fact]
    public async Task FirstLoginFails_SecondCallAttemptsLoginAgain_AndSucceeds()
    {
        // Issue #330/#323: одна ошибка входа не должна «отравлять» сессию — следующее
        // окно/операция пробуют войти снова.
        var handler = new RetryLoginHandler(failFirstPost: true);
        var service = CreateService(handler, login: "user1", password: "secret");

        var first = await service.FetchPageAsync("https://releases.1c.ru/project/Platform83");
        Assert.Equal(PortalFetchStatus.AuthFailed, first.Status);

        var second = await service.FetchPageAsync("https://releases.1c.ru/project/Platform83");
        Assert.Equal(PortalFetchStatus.Ok, second.Status);
        Assert.Equal(2, handler.PostLoginCount);
    }

    [Fact]
    public async Task LoginAttempts_LimitedToMax_ThenNoMoreAttempts()
    {
        var handler = new StaticLoginHandler(postStatus: HttpStatusCode.Unauthorized, postBody: "<html>нет</html>");
        var service = CreateService(handler, login: "user1", password: "secret");

        var r1 = await service.FetchPageAsync("https://releases.1c.ru/project/Platform83");
        var r2 = await service.FetchPageAsync("https://releases.1c.ru/project/Platform83");
        var r3 = await service.FetchPageAsync("https://releases.1c.ru/project/Platform83");
        var r4 = await service.FetchPageAsync("https://releases.1c.ru/project/Platform83");

        Assert.Equal(PortalFetchStatus.AuthFailed, r1.Status);
        Assert.Equal(PortalFetchStatus.AuthFailed, r2.Status);
        Assert.Equal(PortalFetchStatus.AuthFailed, r3.Status);
        // Лимит попыток исчерпан: четвёртый вызов вход НЕ предпринимает (счётчик тот же),
        // результат остаётся информативным «вход не подтверждён» (последняя причина 401).
        Assert.Equal(PortalFetchStatus.AuthFailed, r4.Status);
        Assert.Equal(3, handler.PostLoginCount);
    }

    [Fact]
    public async Task AccountSwitch_ResetsAttemptCounter_AllowsNewLogin()
    {
        var handler = new StaticLoginHandler(postStatus: HttpStatusCode.Unauthorized, postBody: "<html>нет</html>");
        var repo = new MemRepo();
        repo.Settings.UpdatesLogin = "user-a";
        repo.Settings.UpdatesPassword = "pwd-a";
        var service = new OneCUpdatesService(repo, new CollectingLogger(), handler);

        var r1 = await service.FetchPageAsync("https://releases.1c.ru/project/Platform83");
        Assert.Equal(PortalFetchStatus.AuthFailed, r1.Status);

        // Пользователь сменил учётную запись — счётчик попыток сбрасывается.
        repo.Settings.UpdatesLogin = "user-b";
        repo.Settings.UpdatesPassword = "pwd-b";

        var r2 = await service.FetchPageAsync("https://releases.1c.ru/project/Platform83");
        Assert.Equal(PortalFetchStatus.AuthFailed, r2.Status); // новая попытка выполнена
        Assert.Equal(2, handler.PostLoginCount);
    }

    [Fact]
    public async Task NoCredentials_LoginNotAttempted_ReturnsAuthRequired()
    {
        var handler = new StaticLoginHandler(postStatus: HttpStatusCode.Unauthorized, postBody: "<html>нет</html>");
        var service = CreateService(handler, login: string.Empty, password: string.Empty);

        var result = await service.FetchPageAsync("https://releases.1c.ru/project/Platform83");

        Assert.Equal(PortalFetchStatus.AuthRequired, result.Status);
        Assert.Equal(0, handler.PostLoginCount);
    }

    // ---------- Вспомогательные ----------

    private static OneCUpdatesService CreateService(
        HttpMessageHandler handler,
        CollectingLogger? logger = null,
        string login = "user",
        string password = "secret")
    {
        var repo = new MemRepo();
        repo.Settings.UpdatesLogin = login;
        repo.Settings.UpdatesPassword = password;
        return new OneCUpdatesService(repo, logger ?? new CollectingLogger(), handler);
    }

    private static HttpResponseMessage Ok(string body)
        => new(HttpStatusCode.OK) { Content = new StringContent(body) };

    private static HttpResponseMessage Found(Uri location)
    {
        var response = new HttpResponseMessage(HttpStatusCode.Found);
        response.Headers.Location = location;
        return response;
    }

    /// <summary>Обработчик: первый запрос каталога → 302 на login.1c.ru → форма → POST
    /// (настраиваемый статус/тело/редирект), после успешного входа повторный запрос каталога
    /// → 200 с версиями. Считает POST-ы входа и фиксирует тело последнего POST.</summary>
    private sealed class LoginCaptureHandler : HttpMessageHandler
    {
        private readonly string _formHtml;
        private readonly HttpStatusCode _postStatus;
        private readonly Uri? _postLocation;
        private readonly string _postBody;
        private bool _loginSucceeded;

        public int PostLoginCount { get; private set; }
        public string? LastPostBody { get; private set; }

        public LoginCaptureHandler(string formHtml, HttpStatusCode postStatus, Uri? location = null, string postBody = "")
        {
            _formHtml = formHtml;
            _postStatus = postStatus;
            _postLocation = location;
            _postBody = postBody;
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri?.AbsolutePath ?? string.Empty;
            HttpResponseMessage response;

            if (path.Contains("/project/", StringComparison.OrdinalIgnoreCase))
            {
                response = !_loginSucceeded
                    ? Found(new Uri("https://login.1c.ru/login?service=https://releases.1c.ru/public/security_check"))
                    : Ok(VersionsTableHtml);
            }
            else if (path.Contains("/public/security_check", StringComparison.OrdinalIgnoreCase))
            {
                _loginSucceeded = true;
                response = Ok("<html>session established</html>");
            }
            else if (path.Contains("/login", StringComparison.OrdinalIgnoreCase))
            {
                if (request.Method == HttpMethod.Post && request.Content is not null)
                {
                    PostLoginCount++;
                    LastPostBody = await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                    response = _postLocation is not null
                        ? Found(_postLocation)
                        : new HttpResponseMessage(_postStatus) { Content = new StringContent(_postBody) };
                }
                else
                {
                    response = Ok(_formHtml);
                }
            }
            else
            {
                response = new HttpResponseMessage(HttpStatusCode.Found);
            }

            response.RequestMessage = request;
            return response;
        }
    }

    /// <summary>Обработчик «всегда одна и та же форма/ответ на POST» (без security_check).</summary>
    private sealed class StaticLoginHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _postStatus;
        private readonly string _postBody;

        public int PostLoginCount { get; private set; }

        public StaticLoginHandler(HttpStatusCode postStatus, string postBody)
        {
            _postStatus = postStatus;
            _postBody = postBody;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri?.AbsolutePath ?? string.Empty;
            HttpResponseMessage response;

            if (path.Contains("/project/", StringComparison.OrdinalIgnoreCase))
            {
                response = Found(new Uri("https://login.1c.ru/login?service=x"));
            }
            else if (path.Contains("/login", StringComparison.OrdinalIgnoreCase))
            {
                if (request.Method == HttpMethod.Post)
                {
                    PostLoginCount++;
                    response = new HttpResponseMessage(_postStatus) { Content = new StringContent(_postBody) };
                }
                else
                {
                    response = Ok(SimpleForm);
                }
            }
            else
            {
                response = new HttpResponseMessage(HttpStatusCode.Found);
            }

            response.RequestMessage = request;
            return Task.FromResult(response);
        }
    }

    /// <summary>Обработчик: первый POST входа → 401 (вход не подтверждён), при следующей
    /// операции — вход выполняется (302 → security_check → 200) и каталог возвращает версии.
    /// Проверяет, что одна ошибка входа не «отравляет» сессию (issue #330/#323).</summary>
    private sealed class RetryLoginHandler : HttpMessageHandler
    {
        private readonly bool _failFirstPost;
        private bool _loginSucceeded;

        public int PostLoginCount { get; private set; }

        public RetryLoginHandler(bool failFirstPost) => _failFirstPost = failFirstPost;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri?.AbsolutePath ?? string.Empty;
            HttpResponseMessage response;

            if (path.Contains("/project/", StringComparison.OrdinalIgnoreCase))
            {
                // До успешного входа каталог редиректит на login; после — отдаёт версии.
                response = !_loginSucceeded
                    ? Found(new Uri("https://login.1c.ru/login?service=x"))
                    : Ok(VersionsTableHtml);
            }
            else if (path.Contains("/public/security_check", StringComparison.OrdinalIgnoreCase))
            {
                _loginSucceeded = true;
                response = Ok("<html>session established</html>");
            }
            else if (path.Contains("/login", StringComparison.OrdinalIgnoreCase))
            {
                if (request.Method == HttpMethod.Post)
                {
                    PostLoginCount++;
                    response = _failFirstPost && PostLoginCount == 1
                        ? new HttpResponseMessage(HttpStatusCode.Unauthorized) { Content = new StringContent("<html>нет</html>") }
                        : Found(new Uri("https://releases.1c.ru/public/security_check?ticket=ST-1"));
                }
                else
                {
                    response = Ok(SimpleForm);
                }
            }
            else
            {
                response = new HttpResponseMessage(HttpStatusCode.Found);
            }

            response.RequestMessage = request;
            return Task.FromResult(response);
        }
    }

    /// <summary>Репозиторий в памяти (настройки портала; базы/группы пустые).</summary>
    private sealed class MemRepo : IInfobaseRepository
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

    /// <summary>Логгер-заглушка: собирает сообщения для проверки анонимизации.</summary>
    private sealed class CollectingLogger : IAppLogger
    {
        public List<string> Messages { get; } = new();

        public void Info(string message) => Messages.Add(message);
        public void Warn(string message) => Messages.Add(message);
        public void Error(string message, Exception? exception = null) => Messages.Add(message);
    }
}