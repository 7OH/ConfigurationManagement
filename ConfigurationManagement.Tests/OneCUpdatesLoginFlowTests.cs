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

    // ---------- Атрибут action формы (issue #323/#330/#334, третья итерация) ----------

    [Fact]
    public void ExtractFormAction_QuotedAction_ReturnsValue()
    {
        var action = OneCUpdatesService.ExtractFormAction(
            """<form id="fm1" action="/login/cas?service=https%3A%2F%2Freleases.1c.ru" method="post">""");

        Assert.Equal("/login/cas?service=https%3A%2F%2Freleases.1c.ru", action);
    }

    [Fact]
    public void ExtractFormAction_SingleQuotedAndUnquoted_Parsed()
    {
        Assert.Equal("/cas",
            OneCUpdatesService.ExtractFormAction("""<form action='/cas'>"""));
        Assert.Equal("https://login.1c.ru/auth",
            OneCUpdatesService.ExtractFormAction("""<form action=https://login.1c.ru/auth>"""));
    }

    [Fact]
    public void ExtractFormAction_NoActionOrHash_ReturnsNull()
    {
        Assert.Null(OneCUpdatesService.ExtractFormAction("""<form id="fm1" method="post">"""));
        Assert.Null(OneCUpdatesService.ExtractFormAction("""<form action="#">"""));
        Assert.Null(OneCUpdatesService.ExtractFormAction(null!));
        Assert.Null(OneCUpdatesService.ExtractFormAction(string.Empty));
    }

    [Fact]
    public void LooksLikeOAuthOrChallenge_DetectsChangedFormMarkers()
    {
        // Форма без execution/lt, но с признаками OAuth/JS-челленджа — автоматический вход
        // невозможен (issue #323/#330/#334).
        Assert.True(OneCUpdatesService.LooksLikeOAuthOrChallenge(
            """<html><script src="/oauth/authorize?client_id=app"></script><div>challenge</div></html>"""));
        Assert.True(OneCUpdatesService.LooksLikeOAuthOrChallenge(
            """<html>csrf protection required</html>"""));
        Assert.False(OneCUpdatesService.LooksLikeOAuthOrChallenge(
            """<form><input type="hidden" name="execution" value="e1"/></form>"""));
        Assert.False(OneCUpdatesService.LooksLikeOAuthOrChallenge(null!));
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

    [Fact]
    public async Task LoginPost_UsesFormActionUrl()
    {
        // Третья итерация CAS (issue #323/#330/#334): POST формы должен идти на атрибут
        // action формы, а не на URL GET-формы (Spring Security CAS часто указывает отдельный
        // action «/login/cas?service=…»). GET-форма отдана по /login?service=…; action = «/cas».
        const string formWithAction = """
            <html><body>
            <form id="fm1" action="/cas?service=https%3A%2F%2Freleases.1c.ru" method="post">
              <input type="hidden" name="execution" value="e1s2t3" />
            </form>
            </body></html>
            """;
        var handler = new LoginCaptureHandler(formWithAction, postStatus: HttpStatusCode.Found,
            location: new Uri("https://releases.1c.ru/public/security_check?ticket=ST-1"));
        var service = CreateService(handler, login: "user1", password: "secret");

        var result = await service.FetchPageAsync("https://releases.1c.ru/project/Platform83");

        Assert.Equal(PortalFetchStatus.Ok, result.Status);
        Assert.NotNull(handler.LastPostUri);
        Assert.Equal("https://login.1c.ru/cas", handler.LastPostUri!.GetLeftPart(System.UriPartial.Path));
        Assert.Contains("service=https%3A%2F%2Freleases.1c.ru", handler.LastPostUri!.Query);
    }

    [Fact]
    public async Task LoginPost_NoFormAction_PostsToFormUrl()
    {
        // Регресс: форма без action — POST остаётся на адресе GET-формы (прежнее поведение).
        var handler = new LoginCaptureHandler(SimpleForm, postStatus: HttpStatusCode.Found,
            location: new Uri("https://releases.1c.ru/public/security_check?ticket=ST-1"));
        var service = CreateService(handler, login: "user1", password: "secret");

        var result = await service.FetchPageAsync("https://releases.1c.ru/project/Platform83");

        Assert.Equal(PortalFetchStatus.Ok, result.Status);
        Assert.NotNull(handler.LastPostUri);
        Assert.Contains("/login", handler.LastPostUri!.AbsolutePath);
    }

    [Fact]
    public async Task LoginForm_NoExecution_FormUnavailableWithMarker()
    {
        // Форма без execution/lt, но с маркерами OAuth/JS-челленджа: программный вход
        // невозможен — результат FormUnavailable, в журнале маркер изменённой формы
        // (issue #323/#330/#334).
        var handler = new OAuthChallengeHandler();
        var logger = new CollectingLogger();
        var service = CreateService(handler, logger, login: "user1", password: "secret");

        var result = await service.FetchPageAsync("https://releases.1c.ru/project/Platform83");

        Assert.Equal(PortalFetchStatus.FormUnavailable, result.Status);
        var joined = string.Join("\n", logger.Messages);
        Assert.Contains("OAuth/JS-челленджа", joined);
        Assert.DoesNotContain("secret", joined);
    }

    [Fact]
    public async Task SendWithAuthAsync_LogsReasonForLogin()
    {
        // Диагностика входа (issue #323/#330/#334): журнал фиксирует причину запуска входа
        // (redirect-login/self-redirect/http-401) и результат TryLoginPortalAsync.
        var handler = new StaticLoginHandler(postStatus: HttpStatusCode.Unauthorized,
            postBody: "<html><body>Неверный логин</body></html>");
        var logger = new CollectingLogger();
        var service = CreateService(handler, logger, login: "user1", password: "sup3r-secret");

        await service.FetchPageAsync("https://releases.1c.ru/project/Platform83");

        var joined = string.Join("\n", logger.Messages);
        Assert.Contains("Вход запущен: reason=", joined);
        Assert.Contains("результат=AuthFailed", joined);
        Assert.DoesNotContain("sup3r-secret", joined);
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
        // Лимит попыток исчерпан: четвёртый вызов вход НЕ предпринимает (счётчик тот же)
        // и возвращает отдельный статус «лимит исчерпан» (issue #334/#330/#323) вместо
        // вводящего в заблуждение «вход не подтверждён».
        Assert.Equal(PortalFetchStatus.LoginLimitReached, r4.Status);
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

    // ---------- «Фантомный успех»: 200 с формой входа в теле (Причина 1, issue #330) ----------

    [Fact]
    public async Task LoginPost200_WithLoginFormInBody_ReturnsAuthFailed()
    {
        // CAS при неверном логине возвращает 200 с телом формы входа (execution/lt) и БЕЗ
        // сессионной cookie. Такой ответ НЕ считается успехом: следующий запрос каталога
        // снова дал бы 302 → повторный вход → исчерпание лимита (лог issue #330).
        var handler = new Post200LoginHandler(postBody: FormWithHiddenFields, markLoginSucceeded: false);
        var logger = new CollectingLogger();
        var service = CreateService(handler, logger, login: "user1", password: "sup3r-secret");

        var result = await service.FetchPageAsync("https://releases.1c.ru/project/Platform83");

        Assert.Equal(PortalFetchStatus.AuthFailed, result.Status);
        Assert.Equal(1, handler.PostLoginCount);
        // Анонимизированная диагностика в журнале: признак формы входа есть, секретов нет.
        var joined = string.Join("\n", logger.Messages);
        Assert.Contains("Вход на portal.1c.ru не подтверждён", joined);
        Assert.DoesNotContain("sup3r-secret", joined);
        Assert.DoesNotContain("e1s2t3", joined);
    }

    [Fact]
    public async Task LoginPost200_WithoutLoginForm_ReturnsSuccess()
    {
        // POST вернул 200 с целевым контентом каталога (без полей формы) — вход выполнен,
        // следующий запрос каталога отдаёт версии.
        var handler = new Post200LoginHandler(postBody: VersionsTableHtml, markLoginSucceeded: true);
        var service = CreateService(handler, login: "user1", password: "secret");

        var result = await service.FetchPageAsync("https://releases.1c.ru/project/Platform83");

        Assert.Equal(PortalFetchStatus.Ok, result.Status);
        Assert.Equal(1, handler.PostLoginCount);
    }

    [Fact]
    public void LooksLikeLoginForm_DetectsFormByFields_MarkersAndHost()
    {
        // Форма входа распознаётся по скрытым полям execution/lt, маркерам ошибки и
        // упоминанию login.1c.ru; каталог версий формой не является.
        Assert.True(OneCUpdatesService.LooksLikeLoginForm(FormWithHiddenFields));
        Assert.True(OneCUpdatesService.LooksLikeLoginForm(SimpleForm));
        Assert.True(OneCUpdatesService.LooksLikeLoginForm("<html>Неверный логин и/или пароль</html>"));
        Assert.True(OneCUpdatesService.LooksLikeLoginForm(
            "<html><a href=\"https://login.1c.ru/login\">Вход</a></html>"));
        Assert.False(OneCUpdatesService.LooksLikeLoginForm(VersionsTableHtml));
        Assert.False(OneCUpdatesService.LooksLikeLoginForm(string.Empty));
        Assert.False(OneCUpdatesService.LooksLikeLoginForm(null!));
    }

    // ---------- Сброс счётчика попыток при успехе (Причина 2) ----------

    [Fact]
    public async Task SuccessfulLogin_ResetsAttemptCounter()
    {
        // Провалы на 1-м, 2-м и 4-м POST; 3-й POST — успех (302 → security_check → 200).
        // После успеха счётчик обнуляется, поэтому четвёртая операция снова может входить
        // (issue #334/#330/#323): служба singleton не должна блокировать вход навсегда.
        var handler = new ChainedLoginHandler(1, 2, 4);
        var service = CreateService(handler, login: "user1", password: "secret");

        var r1 = await service.FetchPageAsync("https://releases.1c.ru/project/Platform83");
        Assert.Equal(PortalFetchStatus.AuthFailed, r1.Status); // попытка 1

        var r2 = await service.FetchPageAsync("https://releases.1c.ru/project/Platform83");
        Assert.Equal(PortalFetchStatus.AuthFailed, r2.Status); // попытка 2

        var r3 = await service.FetchPageAsync("https://releases.1c.ru/project/Platform83");
        Assert.Equal(PortalFetchStatus.Ok, r3.Status);         // попытка 3 — успех, счётчик сброшен

        var r4 = await service.FetchPageAsync("https://releases.1c.ru/project/Platform83");
        Assert.Equal(PortalFetchStatus.AuthFailed, r4.Status); // счётчик обнулён — вход снова возможен
        Assert.Equal(4, handler.PostLoginCount);
    }

    // ---------- Цикл 302→вход→302 внутри одного вызова (Причина 3) ----------

    [Fact]
    public async Task LoginLoopInsideSingleCall_LimitedToOneAttempt()
    {
        // Сервер отвечает 302→login при каждом обращении к каталогу, а POST входа
        // возвращает 200 с формой (вход не выполнен). За один вызов выполняется ТОЛЬКО
        // одна попытка входа, остальной лимит сохраняется для следующих операций.
        var handler = new Post200LoginHandler(postBody: FormWithHiddenFields, markLoginSucceeded: false);
        var service = CreateService(handler, login: "user1", password: "secret");

        var result = await service.FetchPageAsync("https://releases.1c.ru/project/Platform83");

        Assert.Equal(PortalFetchStatus.AuthFailed, result.Status);
        Assert.Equal(1, handler.PostLoginCount);
    }

    // ---------- Страница входа при HTTP 200 по содержимому (Причина 4) ----------

    [Fact]
    public async Task CatalogPage_200_WithLoginFormBody_ReturnsAuthRequired()
    {
        // releases.1c.ru вернул 200 с HTML формы входа (без редиректа) — распознаём по
        // содержимому, а не парсим «версии из каталога» (issue #330/#323).
        var handler = new DirectFormHandler(FormWithHiddenFields);
        var service = CreateService(handler, login: "user1", password: "secret");

        var page = await service.FetchPageAsync("https://releases.1c.ru/project/Platform83");
        Assert.Equal(PortalFetchStatus.AuthRequired, page.Status);

        var check = await service.CheckForUpdatesAsync(
            "Бухгалтерия предприятия", "3.0.120.1", "https://releases.1c.ru/project/AccountingCorp30");
        Assert.Equal(ConfigUpdateStatus.Failed, check.Status);
        Assert.Equal("Updates.AuthRequired", check.Error);
    }

    // ---------- Лимит: автосброс по таймеру (Причина 5) ----------

    [Fact]
    public async Task LoginLimitReached_AfterCooldown_AllowsRetry()
    {
        var handler = new StaticLoginHandler(postStatus: HttpStatusCode.Unauthorized, postBody: "<html>нет</html>");
        var service = CreateService(handler, login: "user1", password: "secret");
        var now = DateTime.UtcNow;
        service.UtcNowProvider = () => now;

        var r1 = await service.FetchPageAsync("https://releases.1c.ru/project/Platform83");
        var r2 = await service.FetchPageAsync("https://releases.1c.ru/project/Platform83");
        var r3 = await service.FetchPageAsync("https://releases.1c.ru/project/Platform83");
        Assert.Equal(PortalFetchStatus.AuthFailed, r3.Status);
        Assert.Equal(3, handler.PostLoginCount);

        // Лимит исчерпан: новая попытка запрещена (статус «лимит»), POST не выполняется.
        var r4 = await service.FetchPageAsync("https://releases.1c.ru/project/Platform83");
        Assert.Equal(PortalFetchStatus.LoginLimitReached, r4.Status);
        Assert.Equal(3, handler.PostLoginCount);

        // Автосброс по таймеру: после LoginLimitCooldown вход снова разрешён.
        now = now.AddMinutes(11);
        var r5 = await service.FetchPageAsync("https://releases.1c.ru/project/Platform83");
        Assert.Equal(PortalFetchStatus.AuthFailed, r5.Status); // новая попытка выполнена (снова 401)
        Assert.Equal(4, handler.PostLoginCount);
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

        /// <summary>URL последнего POST формы входа (для проверки атрибута action, issue #323/#330/#334).</summary>
        public Uri? LastPostUri { get; private set; }

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
            // Форма входа: GET — на /login, POST — на атрибут action формы (может быть /cas,
            // issue #323/#330/#334); маршрутизируем обе ветки.
            else if (path.Contains("/login", StringComparison.OrdinalIgnoreCase)
                     || path.Contains("/cas", StringComparison.OrdinalIgnoreCase))
            {
                if (request.Method == HttpMethod.Post && request.Content is not null)
                {
                    PostLoginCount++;
                    LastPostUri = request.RequestUri;
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

    /// <summary>
    /// Обработчик «фантомного успеха» (issue #330): POST входа возвращает 200. Если тело —
    /// форма входа (execution/lt) и сессия НЕ помечается успешной, каталог продолжает
    /// редиректить на login (вход фактически не выполнен); если тело — целевой контент
    /// каталога и сессия помечается успешной, каталог отдаёт версии.
    /// </summary>
    private sealed class Post200LoginHandler : HttpMessageHandler
    {
        private readonly string _postBody;
        private readonly bool _markLoginSucceeded;
        private bool _loginSucceeded;

        public int PostLoginCount { get; private set; }

        public Post200LoginHandler(string postBody, bool markLoginSucceeded)
        {
            _postBody = postBody;
            _markLoginSucceeded = markLoginSucceeded;
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri?.AbsolutePath ?? string.Empty;
            HttpResponseMessage response;

            if (path.Contains("/project/", StringComparison.OrdinalIgnoreCase))
            {
                response = !_loginSucceeded
                    ? Found(new Uri("https://login.1c.ru/login?service=x"))
                    : Ok(VersionsTableHtml);
            }
            else if (path.Contains("/login", StringComparison.OrdinalIgnoreCase))
            {
                if (request.Method == HttpMethod.Post)
                {
                    PostLoginCount++;
                    if (_markLoginSucceeded)
                        _loginSucceeded = true;
                    response = Ok(_postBody);
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
            return response;
        }
    }

    /// <summary>Обработчик с заданной последовательностью результатов POST входа:
    /// «провал» (401) либо «успех» (302 → security_check → 200). Проверяет сброс счётчика
    /// попыток после успешного входа (issue #334/#330/#323).</summary>
    private sealed class ChainedLoginHandler : HttpMessageHandler
    {
        private readonly IReadOnlySet<int> _failPostNumbers;
        private bool _loginSucceeded;

        public int PostLoginCount { get; private set; }

        public ChainedLoginHandler(params int[] failPostNumbers)
            => _failPostNumbers = new HashSet<int>(failPostNumbers);

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri?.AbsolutePath ?? string.Empty;
            HttpResponseMessage response;

            if (path.Contains("/project/", StringComparison.OrdinalIgnoreCase))
            {
                // «Сессия» действует только внутри одного вызова: первый запрос каталога
                // каждого НОВОГО вызова снова редиректит на login — так тест проверяет
                // именно сброс счётчика попыток, а не сохранение сессии обработчиком.
                var granted = _loginSucceeded;
                _loginSucceeded = false;
                response = granted
                    ? Ok(VersionsTableHtml)
                    : Found(new Uri("https://login.1c.ru/login?service=x"));
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
                    response = _failPostNumbers.Contains(PostLoginCount)
                        ? new HttpResponseMessage(HttpStatusCode.Unauthorized)
                        {
                            Content = new StringContent("<html>нет</html>"),
                        }
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

    /// <summary>Обработчик: каталог всегда отвечает 200 с HTML формы входа (без редиректов
    /// на login.1c.ru) — проверяет распознавание страницы входа по содержимому
    /// (issue #330/#323).</summary>
    private sealed class DirectFormHandler : HttpMessageHandler
    {
        private readonly string _body;

        public DirectFormHandler(string body) => _body = body;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = Ok(_body);
            response.RequestMessage = request;
            return Task.FromResult(response);
        }
    }

    /// <summary>Обработчик: каталог редиректит на login.1c.ru, GET формы входа возвращает HTML
    /// БЕЗ execution/lt, но с маркерами OAuth/JS-челленджа — программный вход невозможен
    /// (issue #323/#330/#334, третья итерация).</summary>
    private sealed class OAuthChallengeHandler : HttpMessageHandler
    {
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
                // Форма изменилась радикально: нет классических токенов CAS, есть OAuth/JS-маркеры.
                response = Ok("""
                    <html><head><script src="/oauth/authorize?client_id=portal"></script></head>
                    <body><h2>JavaScript challenge</h2></body></html>
                    """);
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