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
    public async Task LoginPost200_TargetContentWithSetCookie_ReturnsSuccess()
    {
        // Четвёртая итерация CAS (issue #323/#330/#334): POST вернул 200 с целевым контентом
        // каталога (не формой входа) И заголовком Set-Cookie — вход засчитывается по второму
        // критерию успеха («нет маркеров входа + есть Set-Cookie в ответе»). Без Set-Cookie
        // тот же ответ — «фантомный успех» (см. LoginPost_200WithoutSessionCookie_IsNotSuccess).
        var handler = new Post200LoginHandler(postBody: VersionsTableHtml, markLoginSucceeded: true,
            postSetCookies: new[] { "TS01=abc; Path=/; HttpOnly" });
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

    // ---------- «Фантомный успех» и JS/meta-refresh (issue #323/#330/#334, четвёртая итерация) ----------

    [Fact]
    public async Task LoginPost_200WithoutSessionCookie_IsNotSuccess()
    {
        // 200 с целевым контентом, но БЕЗ Set-Cookie и БЕЗ сессионной cookie — «фантомный
        // успех»: вход НЕ засчитывается (раньше такой ответ считался успехом, и лимит из
        // 3 попыток сжигался за одну операцию — лог issue #330). Повтор исходного запроса
        // не запускается, лимит попыток не тратится.
        var handler = new Post200LoginHandler(postBody: VersionsTableHtml, markLoginSucceeded: false);
        var logger = new CollectingLogger();
        var service = CreateService(handler, logger, login: "user1", password: "sup3r-secret");

        var result = await service.FetchPageAsync("https://releases.1c.ru/project/Platform83");

        Assert.Equal(PortalFetchStatus.AuthFailed, result.Status);
        Assert.Equal(1, handler.PostLoginCount);
        Assert.Equal(1, handler.CatalogRequestCount); // исходный запрос не повторяется
        var joined = string.Join("\n", logger.Messages);
        Assert.Contains("фантомный успех", joined);
        Assert.DoesNotContain("sup3r-secret", joined);
    }

    [Fact]
    public async Task LoginPost_200WithSessionCookie_IsSuccess()
    {
        // 200 + Set-Cookie сессии (JSESSIONID) — вход засчитывается по сессионной cookie,
        // повтор исходного запроса отдаёт версии каталога (issue #323/#330/#334).
        var handler = new Post200LoginHandler(postBody: "<html>session established</html>",
            markLoginSucceeded: true, postSetCookies: new[] { "JSESSIONID=abc123; Path=/; HttpOnly" });
        var service = CreateService(handler, login: "user1", password: "secret");

        var result = await service.FetchPageAsync("https://releases.1c.ru/project/Platform83");

        Assert.Equal(PortalFetchStatus.Ok, result.Status);
        Assert.Equal(1, handler.PostLoginCount);
    }

    [Fact]
    public async Task LoginPost_BodyWithMetaRefresh_FollowsLocation()
    {
        // POST вернул 200 с телом, содержащим meta-refresh на security_check?ticket=ST-… —
        // CAS-цепочка доводится JS/meta-refresh-редиректом, там устанавливается сессия
        // (issue #323/#330/#334, четвёртая итерация).
        var handler = new MetaRefreshHandler(
            """<html><head><meta http-equiv="refresh" content="0; url=https://releases.1c.ru/public/security_check?ticket=ST-77"></head></html>""");
        var logger = new CollectingLogger();
        var service = CreateService(handler, logger, login: "user1", password: "secret");

        var result = await service.FetchPageAsync("https://releases.1c.ru/project/Platform83");

        Assert.Equal(PortalFetchStatus.Ok, result.Status);
        Assert.Equal(1, handler.PostLoginCount);
        var joined = string.Join("\n", logger.Messages);
        Assert.Contains("JS/meta-refresh", joined);
    }

    [Fact]
    public async Task LoginDiagnostics_LogHasNoSecrets()
    {
        // Ветка 2xx: диагностика тела (contentType, bodyLength, превью, Set-Cookie именами
        // и флагами) НЕ содержит пароля/логина/значений токенов и значений cookie
        // (issue #323/#330/#334): пароль и логин положены в тело POST-ответа специально.
        var handler = new Post200LoginHandler(
            postBody: "<html>preview user1 sup3r-secret e1s2</html>",
            markLoginSucceeded: false,
            postSetCookies: new[] { "SESSION=COOKIE-VALUE-42; Path=/; HttpOnly; Secure" });
        var logger = new CollectingLogger();
        var service = CreateService(handler, logger, login: "user1", password: "sup3r-secret");

        await service.FetchPageAsync("https://releases.1c.ru/project/Platform83");

        var joined = string.Join("\n", logger.Messages);
        var previewLine = logger.Messages.FirstOrDefault(m => m.Contains("bodyPreview=", StringComparison.Ordinal));
        Assert.NotNull(previewLine);
        Assert.Contains("contentType=", joined);
        Assert.Contains("bodyLength=", joined);
        Assert.Contains("Set-Cookie", joined);
        Assert.Contains("SESSION", joined);                // имя cookie видно
        Assert.DoesNotContain("COOKIE-VALUE-42", joined);  // значение cookie — нет
        Assert.DoesNotContain("sup3r-secret", joined);     // пароль — нет нигде
        Assert.DoesNotContain("sup3r-secret", previewLine);
        Assert.DoesNotContain("user1", previewLine);       // логин — нет в превью тела
        Assert.DoesNotContain("e1s2", previewLine);        // значение токена — нет
    }

    [Fact]
    public async Task SendWithAuthAsync_RetryStill302_TwoAttemptsThenPhantomMarker()
    {
        // «Успешный» вход (200 + сессионная cookie-заглушка), повтор исходного запроса СНОВА даёт
        // 302 на login.1c.ru. В рамках операции выполняется ДВА входа (лимит
        // MaxLoginAttemptsPerOperation), после чего маркер retryAfterLoginStill302 фиксирует
        // исчерпание повторов, а результат — AuthRequired (в 0.3.9.306 был только один вход и
        // мгновенный AuthRequired без повторной попытки — issue #323/#330/#334).
        var handler = new Post200LoginHandler(postBody: "<html>session established</html>",
            markLoginSucceeded: false, postSetCookies: new[] { "JSESSIONID=xyz; Path=/; HttpOnly" });
        var logger = new CollectingLogger();
        var service = CreateService(handler, logger, login: "user1", password: "secret");

        var result = await service.FetchPageAsync("https://releases.1c.ru/project/Platform83");

        Assert.Equal(PortalFetchStatus.AuthRequired, result.Status);
        Assert.Equal(2, handler.PostLoginCount); // две попытки входа за вызов
        var joined = string.Join("\n", logger.Messages);
        Assert.Contains("retryAfterLoginStill302=true", joined);
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

    // ---------- A-1…A-6 (0.3.9.307): живая сессия, очистка cookie, повторный вход ----------

    [Fact]
    public async Task PreSeededSessionCookie_DoesNotShortCircuitLogin_WhenSessionIsDead()
    {
        // Cookie-заглушка JSESSIONID в контейнере НЕ даёт ложного Success (0.3.9.306: ранний
        // выход по имени cookie, лог 7OH «результат=Success» без единого POST): пробный GET
        // каталога даёт 302 на login → сессия мертва → cookie снимаются → выполняется полный
        // вход (GET формы + POST) → каталог Ok (issue #323/#330/#334).
        var handler = new LoginCaptureHandler(FormWithHiddenFields, HttpStatusCode.Found,
            location: new Uri("https://releases.1c.ru/public/security_check?ticket=ST-1"));
        var service = CreateService(handler, login: "user1", password: "secret");
        service.SeedPortalCookieForTesting("JSESSIONID", "phantom", "login.1c.ru");

        var result = await service.FetchPageAsync("https://releases.1c.ru/project/Platform83");

        Assert.Equal(PortalFetchStatus.Ok, result.Status);
        Assert.True(handler.PostLoginCount >= 1,
            "должен выполняться полный вход (POST), а не мгновенный Success по имени cookie");
    }

    [Fact]
    public async Task PreSeededCookie_RemovedBeforeRelogin()
    {
        // Мусорная cookie хоста login.1c.ru удаляется перед повторным входом (A-5): после
        // операции инвентаризация контейнера не содержит cookie-заглушки.
        var handler = new LoginCaptureHandler(FormWithHiddenFields, HttpStatusCode.Found,
            location: new Uri("https://releases.1c.ru/public/security_check?ticket=ST-1"));
        var service = CreateService(handler, login: "user1", password: "secret");
        service.SeedPortalCookieForTesting("JSESSIONID", "phantom-junk", "login.1c.ru");

        var result = await service.FetchPageAsync("https://releases.1c.ru/project/Platform83");

        Assert.Equal(PortalFetchStatus.Ok, result.Status);
        var inventory = service.DescribeContainerCookies();
        Assert.DoesNotContain("phantom-junk", inventory);
        Assert.DoesNotContain("JSESSIONID", inventory);
    }

    [Fact]
    public async Task RetryStill302_SecondLoginWithFreshForm_ThenCatalogOk()
    {
        // Сценарий из лога 7OH: «успешный» вход (фантом: 200 + cookie-заглушка) → повтор исходного
        // запроса снова 302 на login → ВЫПОЛНЯЕТСЯ второй вход со свежей формой (новый execution,
        // 2-й POST) → успех → каталог Ok. Маркер retryAfterLoginStill302 при этом НЕ появляется
        // (повторы не исчерпаны) — issue #323/#330/#334.
        var handler = new PhantomThenRealLoginHandler();
        var logger = new CollectingLogger();
        var service = CreateService(handler, logger, login: "user1", password: "secret");

        var result = await service.FetchPageAsync("https://releases.1c.ru/project/Platform83");

        Assert.Equal(PortalFetchStatus.Ok, result.Status);
        Assert.Equal(2, handler.PostLoginCount); // повторный вход со свежей формой выполнен
        var joined = string.Join("\n", logger.Messages);
        Assert.DoesNotContain("retryAfterLoginStill302=true", joined);
    }

    [Fact]
    public async Task LoginLoopInsideSingleCall_LimitedToTwoAttempts()
    {
        // Сервер всегда отвечает 302 на каталог; первый POST — «фантомный успех» (200 +
        // cookie-заглушка), второй POST — 200 с формой входа (вход не подтверждён). За один
        // вызов выполняется НЕ более MaxLoginAttemptsPerOperation=2 POST; лимит сессии не
        // исчерпан — следующая операция снова может входить (issue #323/#330/#334).
        var handler = new PhantomThenLoginFormHandler();
        var service = CreateService(handler, login: "user1", password: "secret");

        var result = await service.FetchPageAsync("https://releases.1c.ru/project/Platform83");

        Assert.Equal(PortalFetchStatus.AuthFailed, result.Status);
        Assert.Equal(2, handler.PostLoginCount);

        // Резерв лимита сессии сохранён: следующая операция снова предпринимает вход (3-й POST).
        var second = await service.FetchPageAsync("https://releases.1c.ru/project/Platform83");
        Assert.Equal(PortalFetchStatus.AuthFailed, second.Status);
        Assert.Equal(3, handler.PostLoginCount);
    }

    [Fact]
    public async Task LiveSessionProbe_SkipsFullLogin()
    {
        // Живая сессия: пробный GET каталога возвращает контент (не форму) — вход не выполняется
        // (0 POST), лимит попыток не тратится (issue #323/#330/#334).
        var handler = new LiveSessionProbeHandler();
        var logger = new CollectingLogger();
        var service = CreateService(handler, logger, login: "user1", password: "secret");
        service.SeedPortalCookieForTesting("JSESSIONID", "live", "login.1c.ru");

        var result = await service.FetchPageAsync("https://releases.1c.ru/project/Platform83");

        Assert.Equal(PortalFetchStatus.Ok, result.Status);
        Assert.Equal(0, handler.PostLoginCount);
        var joined = string.Join("\n", logger.Messages);
        Assert.Contains("пробная проверка живой сессии", joined);
        Assert.Contains("alive=True", joined);
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
    /// Обработчик «фантомного успеха» (issue #330): POST входа возвращает 200 (опционально
    /// с заголовками Set-Cookie). Если сессия НЕ помечается успешной, каталог продолжает
    /// редиректить на login (вход фактически не выполнен); если тело — целевой контент
    /// и сессия помечается успешной, каталог отдаёт версии. Считает запросы каталога
    /// (для проверки «исходный запрос не повторяется при фантомном успехе»).
    /// </summary>
    private sealed class Post200LoginHandler : HttpMessageHandler
    {
        private readonly string _postBody;
        private readonly bool _markLoginSucceeded;
        private readonly IReadOnlyList<string>? _postSetCookies;
        private bool _loginSucceeded;

        public int PostLoginCount { get; private set; }

        /// <summary>Число обращений к каталогу (исходному запросу) — для проверки, что
        /// при неудачном входе повтор исходного запроса не запускается.</summary>
        public int CatalogRequestCount { get; private set; }

        public Post200LoginHandler(string postBody, bool markLoginSucceeded, IReadOnlyList<string>? postSetCookies = null)
        {
            _postBody = postBody;
            _markLoginSucceeded = markLoginSucceeded;
            _postSetCookies = postSetCookies;
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri?.AbsolutePath ?? string.Empty;
            HttpResponseMessage response;

            if (path.Contains("/project/", StringComparison.OrdinalIgnoreCase))
            {
                CatalogRequestCount++;
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
                    if (_postSetCookies is not null)
                    {
                        foreach (var cookie in _postSetCookies)
                            response.Headers.Add("Set-Cookie", cookie);
                    }
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

    /// <summary>Обработчик JS/meta-refresh-цепочки: POST входа возвращает 200 с телом,
    /// содержащим meta-refresh/JS-редирект на security_check; GET security_check «устанавливает
    /// сессию» и возвращает контент (issue #323/#330/#334, четвёртая итерация).</summary>
    private sealed class MetaRefreshHandler : HttpMessageHandler
    {
        private readonly string _postBody;
        private bool _loginSucceeded;

        public int PostLoginCount { get; private set; }

        public MetaRefreshHandler(string postBody) => _postBody = postBody;

        protected override Task<HttpResponseMessage> SendAsync(
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
            return Task.FromResult(response);
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

    /// <summary>Обработчик сценария «фантомный успех → повторный вход со свежей формой → каталог Ok»
    /// (лог 7OH, issue #323/#330/#334): первый POST возвращает 200 + cookie-заглушку JSESSIONID
    /// (вход «успешен», но каталог продолжает 302), второй POST — 302 на security_check, где
    /// «устанавливается» сессия и каталог начинает отдавать версии.</summary>
    private sealed class PhantomThenRealLoginHandler : HttpMessageHandler
    {
        private bool _realLoginDone;

        public int PostLoginCount { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri?.AbsolutePath ?? string.Empty;
            HttpResponseMessage response;

            if (path.Contains("/project/", StringComparison.OrdinalIgnoreCase))
            {
                response = !_realLoginDone
                    ? Found(new Uri("https://login.1c.ru/login?service=x"))
                    : Ok(VersionsTableHtml);
            }
            else if (path.Contains("/public/security_check", StringComparison.OrdinalIgnoreCase))
            {
                _realLoginDone = true;
                response = Ok("<html>session established</html>");
            }
            else if (path.Contains("/login", StringComparison.OrdinalIgnoreCase))
            {
                if (request.Method == HttpMethod.Post)
                {
                    PostLoginCount++;
                    if (PostLoginCount == 1)
                    {
                        // Фантомный «успех»: 200 без редиректа, с cookie-заглушкой, которую сервер
                        // при следующем запросе не принимает.
                        response = Ok("<html>session established</html>");
                        response.Headers.Add("Set-Cookie", "JSESSIONID=phantom; Path=/; HttpOnly");
                    }
                    else
                    {
                        // Реальный вход: 302 → security_check (сессия устанавливается там).
                        response = Found(new Uri("https://releases.1c.ru/public/security_check?ticket=ST-2"));
                    }
                }
                else
                {
                    // Свежая форма с новым execution (как на реальном портале).
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

    /// <summary>Обработчик «фантом → форма»: каталог ВСЕГДА 302 на login; первый POST — 200 +
    /// cookie-заглушка (фантомный успех), последующие POST — 200 с формой входа (вход не
    /// подтверждён). Проверяет лимит попыток входа в рамках одного вызова
    /// (issue #323/#330/#334).</summary>
    private sealed class PhantomThenLoginFormHandler : HttpMessageHandler
    {
        public int PostLoginCount { get; private set; }

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
                    if (PostLoginCount == 1)
                    {
                        response = Ok("<html>session established</html>");
                        response.Headers.Add("Set-Cookie", "JSESSIONID=phantom; Path=/; HttpOnly");
                    }
                    else
                    {
                        response = Ok(FormWithHiddenFields);
                    }
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

    /// <summary>Обработчик живой сессии: первый запрос каталога (исходный) редиректит на login
    /// (рассинхронизация/заглушка), последующие запросы каталога — контент версий. Проверяет
    /// пробную проверку «живости» сессии (A-1): полный вход при живой сессии не выполняется.</summary>
    private sealed class LiveSessionProbeHandler : HttpMessageHandler
    {
        public int PostLoginCount { get; private set; }

        public int CatalogRequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri?.AbsolutePath ?? string.Empty;
            HttpResponseMessage response;

            if (path.Contains("/project/", StringComparison.OrdinalIgnoreCase))
            {
                response = CatalogRequestCount++ == 0
                    ? Found(new Uri("https://login.1c.ru/login?service=x"))
                    : Ok(VersionsTableHtml);
            }
            else if (path.Contains("/login", StringComparison.OrdinalIgnoreCase))
            {
                if (request.Method == HttpMethod.Post)
                    PostLoginCount++;
                response = Ok(SimpleForm);
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