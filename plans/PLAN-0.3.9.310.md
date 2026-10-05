# PLAN 0.3.9.310 — Кластер A: программный вход на portal.1c.ru (#323; общий корень с #334/#330)

- Дата: 2026-10-05. Режим: **архитектор** — только технический разбор; код НЕ изменяется.
- Основание: сводный [`plans/PLAN-0.3.9.310-312.md`](PLAN-0.3.9.310-312.md), задача T2; вход — полные тексты issues `publish/issue_323_full.md` (20 комментариев), `publish/issue_334_full.md`, `publish/issue_330_full.md`, снимок 2026-10-05.
- Версия: **0.3.9.310** (микро-версия кластера A; комментарии в #323 после релиза, issues не закрывать).
- Проект двухплатформенный: WPF (`#if WINDOWS`) / Avalonia (`#if LINUX`); вход реализован в ОБЩЕМ сервисе [`OneCUpdatesService.cs`](Configuration%20Management/Services/OneCUpdatesService.cs) — платформенных веток кластера A не требуется.
- Связанные issues **НЕ трогаем кодом** в этой итерации: #334 и #330 (последний комментарий от sivatorov, 2026-10-05T11:30Z, «Исправлено в версии 0.3.9.307»). Они используют тот же сервис — регрессия проверяется прогоном их тестов (`PlatformUpdateServiceTests`, `PlatformDownloadViewModelTests`, `PlatformUpdateViewModelTests`) без правок.

---

## A1. Диагноз по коду

### A1.1 Симптом (лог 7OH на 0.3.9.308, комментарий 20 в #323, 2026-10-05T11:56:24Z)

```
[Updates] Вход запущен: reason=redirect-login, url='releases.1c.ru/project/Accounting30',
          location='login.1c.ru/login?service=https%3A%2F%2Freleases.1c.ru%2Fpublic%2Fsecurity_check'
[Updates] Вход: cookie контейнера: login.1c.ru=SESSION{Secure,HttpOnly,Path=/,Domain=login.1c.ru}|_ddg8{...}|_ddg10{...}|_ddg9{...}|_ddg1{...}; ...
[Updates] Вход: пробная проверка живой сессии 'releases.1c.ru/project/Accounting30' => status=302, bodyLength=0, loginForm=нет, alive=False
[Updates] Вход: сессионная cookie в контейнере, но пробный GET показал мёртвую сессию — cookie портала удаляются, выполняется полный вход со свежей формой.
[Updates] Вход: GET формы status=200, execution=есть, lt=нет, action='/login', поля: _eventId,anotherComputer,execution,geolocation,inviteCode,inviteType,rememberMe
[Updates] Вход: POST на action формы 'login.1c.ru/login' (GET-форма: login.1c.ru/login?service=…)
[Updates] Вход: POST status=200, location='<нет>', sessionCookie=True
[Updates] Вход: POST Set-Cookie: _ddg8; Domain=.1c.ru; Path=/ | _ddg10; … | _ddg9; …
[Updates] Вход: POST 2xx диагностика status=200, contentType='text/html; charset=UTF-8', bodyLength=21690,
          bodyPreview=' <title>Личные данные</title> <link rel="stylesheet" type="text…'
[WARN] Вход на portal.1c.ru не подтверждён (status=200: в теле форма входа).
       (body_len=21690, признаки: неверный логин/пароль, execution, поля формы: _eventId, anotherComputer, execution, geolocation, inviteCode, inviteType, rememberMe)
[INFO] Вход запущен: результат=AuthFailed (попытка 1/2), повтор исходного запроса=False
→ «Требуется вход на portal.1c.ru»
```

Ключевые наблюдения по журналу (и код 0.3.9.309, [`OneCUpdatesService.cs`](Configuration%20Management/Services/OneCUpdatesService.cs)):

1. **POST прошёл и вернул НЕ форму входа, а страницу личного кабинета**: `bodyPreview` начинается с `<title>Личные данные</title>` (21690 байт). Страница «Личные данные» отдаётся порталом **после успешной аутентификации** — при неверном пароле CAS возвращает форму входа с сообщением об ошибке, а не личный кабинет.
2. **Код объявил AuthFailed из-за ложного срабатывания [`LooksLikeLoginForm`](Configuration%20Management/Services/OneCUpdatesService.cs:2029)**:
   - ветка POST 2xx: `if (LooksLikeLoginForm(postBody)) → AuthFailed` ([1354–1361](Configuration%20Management/Services/OneCUpdatesService.cs:1354));
   - `LooksLikeLoginForm` возвращает true, потому что `ExtractFormFields(postBody)` нашёл на странице форму с полем `execution` ([2035–2037](Configuration%20Management/Services/OneCUpdatesService.cs:2035)) — поля `username`/`password` на странице «Личные данные» отсутствуют (в логе перечислены только `_eventId, anotherComputer, execution, geolocation, inviteCode, inviteType, rememberMe`);
   - дополнительно `DetectAuthFailureMarkers` ([1980–2007](Configuration%20Management/Services/OneCUpdatesService.cs:1980)) дал маркер «неверный логин/пароль» и «execution» — по простому `ContainsAny(body, "Неверный логин", …, "incorrect"…)` и `body.Contains("execution")` **по всему HTML**, включая JS-скрипты и подсказки валидации страницы. Это тоже ложные признаки.
3. **`sessionCookie=True` после POST не доказывает авторизованную сессию**: в начале входа в контейнере уже была `SESSION{Domain=login.1c.ru}` (проходит [`IsPortalDomainCookie`](Configuration%20Management/Services/OneCUpdatesService.cs:2147) — домен оканчивается на `.1c.ru`, Path=/); после `ClearPortalCookies()` при GET формы входа `login.1c.ru` устанавливает НОВУЮ `SESSION` (сессию страницы входа), а POST приносит только трекинговые `_ddg*`. То есть `HasPortalSessionCookie()` не отличает «сессию страницы входа» от «авторизованной сессии каталога».
4. **Итог**: вход фактически УСПЕШЕН (сервер вернул личный кабинет и принял креды), но из-за ложного детектора вход возвращается `AuthFailed`, повтор исходного запроса каталога не выполняется, пользователь видит «Требуется вход на portal.1c.ru».

### A1.2 Корень (кратко)

| № | Узел кода | Проблема |
|---|-----------|----------|
| 1 | [`LooksLikeLoginForm`](Configuration%20Management/Services/OneCUpdatesService.cs:2029) | Слишком широкий детектор: наличие любого поля `execution`/`lt` на странице объявляет «форму входа». На страницах личного кабинета (встроенные формы приглашений/смены аккаунта) поле `execution` присутствует, а полей `username`/`password` нет. |
| 2 | [`DetectAuthFailureMarkers`](Configuration%20Management/Services/OneCUpdatesService.cs:1980) | Маркеры ищутся подстрокой по всему HTML (`body.Contains("execution")`, `ContainsAny(…, "incorrect"…)`) — JS-скрипты и тексты валидации личного кабинета дают ложные «признаки отказа». |
| 3 | [`HasPortalSessionCookie`](Configuration%20Management/Services/OneCUpdatesService.cs:2115) | Не отличает сессию страницы входа (SESSION от GET формы) от авторизованной сессии. Основной критерий успеха — живость сессии пробным GET каталога — применяется только ДО входа, но не после POST. |

### A1.3 Что уже корректно (не требует изменений)

- Динамический сбор полей формы `ExtractFormFields`, POST на `action` формы с Referer/Origin, HTTP/1.1.
- Пробная проверка «живой» сессии до входа (`IsPortalSessionAliveAsync`, [1436](Configuration%20Management/Services/OneCUpdatesService.cs:1436)).
- Повторный вход со свежей формой при `retryAfterLoginStill302` (счётчик `MaxLoginAttemptsPerOperation=2`).
- Лимит `MaxPortalLoginAttempts=3` + кулдаун 10 мин; раздельные статусы `AuthRequired`/`AuthFailed`/`LoginLimitReached`/`FormUnavailable`.
- UI-панель действий при ошибке авторизации в окнах F9/платформа (кнопки «Открыть login.1c.ru», «Учётные данные ИТС…»).
- Инвентаризация cookie в лог (`DescribeContainerCookies`), санитизация секретов.

### A1.4 Вывод: нужен ли новый код?

**Да, нужен.** Найдена конкретная ошибка кода: ложное срабатывание `LooksLikeLoginForm` на странице личного кабинета после успешного POST. Одного комментария-запроса пользователю недостаточно — без правки детектора даже при корректных кредах вход будет стабильно отклоняться. Дополнительно к коду — комментарий-запрос о проверке кредов в браузере (см. A5.2): он отличит «код» от «креды/блокировка аккаунта/капча» на стороне 1С.

---

## A2. Предлагаемые изменения

Все правки — в общем сервисе [`OneCUpdatesService.cs`](Configuration%20Management/Services/OneCUpdatesService.cs) + локализация `ru.json`/`en.json` + тесты. Файлы платформенных окон НЕ меняются (кнопки/тексты уже готовы; новые ключи локализации подхватываются автоматически).

| № | Файл / метод | Что изменить |
|---|--------------|--------------|
| A-1 | [`LooksLikeLoginForm`](Configuration%20Management/Services/OneCUpdatesService.cs:2029) | Ужесточить критерии «страницы входа» (порядок проверки):<br/>1) наличие **полей ввода логина и пароля** (`name="username"` И `name="password"`) — главный признак;<br/>2) ИЛИ (`execution`/`lt` как **поле формы** — через `ExtractFormFields` — И один из признаков: маркер ошибки авторизации ИЗ ФОРМЫ входа, упоминание `login.1c.ru` в `action` формы или `form` с `id/class`-маркером входа).<br/>Страница с `execution`, но БЕЗ полей `username`/`password` и БЕЗ формы-входа (`action` не `login`) — формой входа НЕ считается. |
| A-2 | Новый чистый helper `DetectPersonalAreaPage(string body)` | Маркеры страницы ПОСЛЕ успешного входа (извлекать `<title>` и заголовки): «Личные данные», «Личный кабинет», «личный кабинет», «Главная», «Профиль», «Мои данные». Используется в POST-ветке как положительный признак успеха (даже если поле `execution` на странице есть). Логика — статический `internal`, покрывается юнит-тестами. |
| A-3 | [`DetectAuthFailureMarkers`](Configuration%20Management/Services/OneCUpdatesService.cs:1980) | Сузить ложные срабатывания:<br/>- «execution»/«csrf»/«lt» — искать как **поле формы** (`name="execution"` / `name='execution'` и т.п.), а не любое вхождение слова в HTML;<br/>- «неверный логин/пароль» — только точные фразы отказа («Неверный логин или пароль», «Неверные учётные данные», «incorrect username or password», «bad credentials», «authentication failed») в разметке страницы (как сейчас подстрока — сузить список, убрав слишком короткие «incorrect»), чтобы JS-подсказки личного кабинета не давали маркер;<br/>- «капча»/«captcha»/«recaptcha» — сохранить (это отдельный признак, см. A-6). |
| A-4 | Ветка POST 2xx ([1347–1409](Configuration%20Management/Services/OneCUpdatesService.cs:1347)) | Новый порядок решения:<br/>1. Если `LooksLikeLoginForm(postBody)` (уточнённый) → `AuthFailed` (как сейчас, но теперь без ложных срабатываний).<br/>2. Если `DetectPersonalAreaPage(postBody) == true` (страница личного кабинета) → считать вход УСПЕШНЫМ: сброс `_portalLoginAttempts=0`, `_lastLoginResult=Success`, возврат `Success` (повтор исходного запроса каталога выполнит `SendWithAuthAsync`; если сервер всё равно отдаст 302 — сработает существующий повторный вход до `MaxLoginAttemptsPerOperation`).<br/>3. JS/meta-refresh редирект (`ExtractBodyRedirectUrl`) — как сейчас.<br/>4. Fallback «фантомного успеха» (`!hasSession && !hasSetCookie` → `AuthFailed`) — как сейчас, НО перед ним добавить запись в лог факта `sessionCookie` **по имени/домену**: различать `SESSION Domain=login.1c.ru` (сессия страницы входа) и сессию с доменом `.1c.ru`/`releases.1c.ru`. |
| A-5 | [`HasPortalSessionCookie`](Configuration%20Management/Services/OneCUpdatesService.cs:2115) | Не менять критерий (оставить), но добавить перегрузку/параметр для диагностики: `DescribeSessionCookies()` — перечень имён+доменов «сессионных» cookie после POST (без значений). Используется в A-4 и в POST-диагностике. |
| A-6 | Новый признак «капча/подтверждение» | Ввести поле `private string? _lastAuthFailureReason` (заполняется в `DetectAuthFailureMarkers`/`LogAnonymizedAuthFailure` при найденном признаке «капча»). [`AuthErrorKey`](Configuration%20Management/Services/OneCUpdatesService.cs:2251) → при `_lastAuthFailureReason=="капча"` возвращать **новый ключ `Updates.CaptchaRequired`**. Текст: «Портал запросил подтверждение (капча). Автоматический вход временно невозможен — выполните вход в браузере на login.1c.ru и повторите проверку». Кнопки «Открыть login.1c.ru в браузере»/«Учётные данные ИТС…» уже есть в окнах. |
| A-7 | Локализация `ru.json`/`en.json` | Добавить: `Updates.CaptchaRequired` (A-6); уточнить текст `Updates.AuthFailed`: «Вход на portal.1c.ru не подтверждён. Проверьте логин/пароль учётной записи ИТС (Настройки → Учётные данные ИТС); при необходимости проверьте вход на login.1c.ru в браузере (режим инкогнито) — если браузер просит капчу или отклоняет пароль, автоматический вход также не сработает». Существующие ключи не удалять. |
| A-8 | [`LogPost2xxDiagnostics`](Configuration%20Management/Services/OneCUpdatesService.cs:1929) | В диагностику POST 2xx добавить извлечённый `<title>` страницы (санитизированный, до 60 симв.) — чтобы по логу было сразу видно «Личные данные» vs «Вход». Аналогично в `LogAnonymizedAuthFailure` добавить title страницы. |
| A-9 | README / ARCHITECTURE (при необходимости) | Задокументировать соглашение: страница личного кабинета после POST считается успешным входом; детектор формы входа опирается на поля username/password. |

Примечание A-4: **критерий успеха — повторный запрос каталога** выполняется штатно в `SendWithAuthAsync` после `Success` (перестроенный `HttpRequestMessage` + `continue`, [1049–1056](Configuration%20Management/Services/OneCUpdatesService.cs:1049)). В TryLoginPortalAsync достаточно вернуть `Success` по странице личного кабинета; живость сессии на каталоге подтвердится на следующем звене, а при 302 — повторный вход (повторы уже ограничены).

---

## A3. Юнит-тесты

Файл [`OneCUpdatesLoginFlowTests.cs`](ConfigurationManagement.Tests/OneCUpdatesLoginFlowTests.cs) (существующие fake-handler-ы переиспользуются; для страницы личного кабинета — новый `PersonalAreaLoginHandler`):

1. **`Post200PersonalAreaPage_NotTreatedAsAuthFailed`** — POST возвращает 200 с телом `<title>Личные данные</title>` + форма с полем `execution` (без username/password). Ожидание: результат входа `Success`, повтор исходного запроса выполняется, НЕ `AuthFailed`.
2. **`LooksLikeLoginForm_ExecutionWithoutUserPass_False`** — HTML с `execution`/`lt` в форме, но без `username`/`password` и без `action` на login → `false`.
3. **`LooksLikeLoginForm_LoginFormWithUserPass_True`** — классическая форма входа (username+password+execution+action `/login`) → `true`.
4. **`DetectAuthFailureMarkers_JsHintDoesNotMatch`** — в HTML есть JS-строка «incorrect»/«execution» в скрипте, но нет полей `name="execution"` и нет фразы отказа → пустые признаки (без «execution», без «неверный логин/пароль»).
5. **`DetectAuthFailureMarkers_Captcha_Detected`** — в HTML есть «captcha»/«капча» → признак «капча».
6. **`AuthErrorKey_CaptchaReason_ReturnsCaptchaRequired`** — при `_lastAuthFailureReason=="капча"` `AuthErrorKey(true)` → `"Updates.CaptchaRequired"`.
7. **`Post200PersonalArea_ThenCatalog302_SecondLoginFreshForm_Ok`** — цепочка из лога 7OH: GET каталога → 302 login → GET формы → POST 200 «Личные данные» (Success) → повтор каталога снова 302 → **повторный вход со свежей формой** → POST 200 «Личные данные» → повтор каталога 200 с `#versionsTable` → `NewerAvailable`. Ожидание: `PostLoginCount == 2`, результат каталога Ok.
8. Регрессия существующих 12+ сценариев: «фантомный успех» без cookie (по-прежнему `AuthFailed` — тело там форма входа с username/password), 401 → AuthFailed, лимит, `FormUnavailable` (OAuth/JS-челлендж), `AuthRequired` без кредов.

Сопутствующие прогоны (без правок тел): `UpdateCheckCatalogTests`, `PlatformUpdateServiceTests`, `PlatformUpdateViewModelTests`, `PlatformDownloadViewModelTests` — проверяют статусные цепочки #334/#330; после A-1…A-9 должны остаться зелёными (контракты `PortalFetchStatus`/`ConfigUpdateStatus`/`PlatformCatalogResult` не меняются).

---

## A4. Риски и fallback

| Риск | Влияние | Митигация / fallback |
|------|---------|----------------------|
| Страница личного кабинета меняет title/верстку (локализация ru/en) | `DetectPersonalAreaPage` не сработает, вход снова AuthFailed | Несколько маркеров (title ru/en + наличие формы с execution без username/password + отсутствие «неверный логин/пароль» в контексте); при сомнении — продолжать как раньше (AuthFailed) + диагностика title в логе (A-8). |
| Креды пользователя действительно неверны (или аккаунт заблокирован/капча) | Вход отклоняется сервером по-настоящему | Комментарий-запрос (A5.2) о проверке в браузере инкогнито; новый признак капчи (A-6) покажет понятное сообщение; существующий `LoginLimitReached` защищает от брутфорса. |
| После «успешного» POST с личным кабинетом каталог всё равно 302 (сессия не полная, нужен security_check) | Круг 302→вход→302 | Существующий повторный вход со свежей формой (до 2 попыток за операцию) + финальный честный `AuthRequired`; при этом старый код давал бы мгновенный AuthFailed — мы улучшаем диагностику, а не ухудшаем. |
| Ужесточение `LooksLikeLoginForm` ослабит детектор реальной формы входа (пропустим каталог вместо ошибки) | F9 покажет «версия не распарсена» вместо понятной ошибки | Детектор требует наличия хотя бы одного из: username/password, форма-с-action-на-login + execution, маркер отказа ИЗ формы. Редкий вариант «каталог доступен, но не распарсен» уже имеет свой статус `Unavailable` — приемлемо. |
| Живой вход с реальными кредами ИТС автору недоступен | Критерий «каталог получен» проверяем только пользователем | Юнит-тесты покрывают все ветки цепочки (включая новый сценарий 7); в комментарии к issue — чек-лист проверки. |
| Регрессия в #334/#330 (общий сервис) | Окна платформы/скачивания ломаются | Код общий — новые тесты A3.1–A3.8 + прогон тестов #334/#330; ручная проверка по чек-листу в комментариях. |

---

## A5. Критерии готовности

### A5.1 Технические

- `dotnet test` зелёный, включая новые тесты A3.1–A3.8; кросс-сборка Linux (`dotnet build -p:BuildLinux=true`) без ошибок.
- `LooksLikeLoginForm` не даёт true для страницы личного кабинета (title «Личные данные», форма с execution, без username/password).
- `Updates.CaptchaRequired` и уточнённый `Updates.AuthFailed` переведены в ru/en; окна F9/платформа выводят их через существующий маппинг ключей.
- В логе POST 2xx виден `<title>` ответа (A-8).

### A5.2 Ручные (пользователь 7OH — публикуются в комментарии к #323 после релиза 0.3.9.310)

1. Установить 0.3.9.310. В «Общих настройках» — запись ИТС («Основная»).
2. **До/после установки**: проверить ту же пару логин/пароль на login.1c.ru в браузере (режим инкогнито): входит ли портал вручную, просит ли капчу, отклоняет ли пароль. Это отличит проблему кредов от проблемы кода.
3. F9 по базе «Бухгалтерия предприятия» (`Accounting30`): ожидание — «Последняя версия» + кликабельная ссылка каталога; в журнале — `Вход: POST 2xx … title=… Личные данные …` и `Вход на portal.1c.ru выполнен`, БЕЗ `Вход на portal.1c.ru не подтверждён (status=200: в теле форма входа)`.
4. Если браузер показывает капчу/отказ — приложение должно показать понятное сообщение (`Updates.CaptchaRequired`/`Updates.AuthFailed` с советом), а не техническую строку.
5. Повторить на Linux (Avalonia) — сервис общий.
6. Повторить окна «Автообновление платформы» и «Скачивание платформы» (#334/#330) — регрессия.

---

## A6. Перечень затрагиваемых файлов

- [`Configuration Management/Services/OneCUpdatesService.cs`](Configuration%20Management/Services/OneCUpdatesService.cs) — A-1…A-6, A-8.
- [`Configuration Management/Localization/Languages/ru.json`](Configuration%20Management/Localization/Languages/ru.json) / [`en.json`](Configuration%20Management/Localization/Languages/en.json) — A-7.
- [`ConfigurationManagement.Tests/OneCUpdatesLoginFlowTests.cs`](ConfigurationManagement.Tests/OneCUpdatesLoginFlowTests.cs) — A3.
- Прогон без правок: `ConfigurationManagement.Tests/UpdateCheckCatalogTests.cs`, `PlatformUpdateServiceTests.cs`, `PlatformUpdateViewModelTests.cs`, `PlatformDownloadViewModelTests.cs`.
- README/ARCHITECTURE — при необходимости (A-9).

---

## A7. Порядок передачи в задачу-исполнитель (code)

1. Реализация строго по A-1…A-9; версия csproj → `0.3.9.310` (4 поля); CHANGELOG секция `## [0.3.9.310]`; README (при UX-изменениях).
2. `dotnet test` + кросс-сборка Linux.
3. Сборка артефактов и релиз — по шаблону сводного плана (T6/T7).
4. Публикация комментария в **#323** (черновик `publish/comment-323-0.3.9.310.md`): что исправлено, чек-лист A5.2, ссылка на релиз. В #334/#330 **комментарии НЕ публиковать** (последний комментарий от sivatorov; общее исправление упоминается только внутри #323). Issues не закрывать.