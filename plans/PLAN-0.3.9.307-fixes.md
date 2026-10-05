# PLAN 0.3.9.307 — Задача 3: Технический план исправлений (кластеры A и B)

- Дата: 2026-10-05. Режим: **архитектор** — только технический разбор; код НЕ изменяется.
- Основание: [`PLAN-0.3.9.307.md`](../PLAN-0.3.9.307.md), Задача 3; вход — [`publish/issues_requirements_2026-10-05.md`](../publish/issues_requirements_2026-10-05.md) (Задача 2), полные логи пользователя из [`issues_details.json`](../issues_details.json) и снимки `publish/`.
- Версии: кластер A → **0.3.9.307**, кластер B → **0.3.9.308** (стратегия из п. 8 плана 307).
- Проект двухплатформенный: WPF (`#if WINDOWS`) / Avalonia (`#if LINUX`); сервисы и чистая логика — общие.

---

## Кластер A — программный вход на portal.1c.ru (#323, #330, #334)

### A1. Диагноз по коду

#### A1.1 Симптом (лог пользователя 7OH, 2026-10-05 09:06–09:08 МСК)

```
[Updates] Вход запущен: reason=redirect-login, url='releases.1c.ru/project/Platform83',
          location='login.1c.ru/login?service=…'
[Updates] Вход запущен: результат=Success, повтор исходного запроса=True
[Updates] Редирект 302 (шаг 1): 'login.1c.ru/login?service=…' для 'releases.1c.ru/project/Platform83'
[WARN] retryAfterLoginStill302=true: после «успешного» входа повтор исходного запроса снова дал
       302 на login.1c.ru (фантомный успех); второй вход в рамках операции не выполняется.
→ AuthRequired / PlatformUpdate.Error.AuthRequired
```

Ключевые наблюдения по журналу (полный текст: `issues_details.json`, строки 272, 408, 552):

1. `TryLoginPortalAsync` вернул `Success` **мгновенно** — в логе НЕТ строк диагностики шагов входа:
   «Вход: GET формы status=…», «Вход: POST status=…», «Вход: POST Set-Cookie: …»,
   «Вход: FollowLoginRedirectsAsync=…». Все эти строки пишутся в [`TryLoginPortalAsync`](Configuration%20Management/Services/OneCUpdatesService.cs:1205) (GET/POST) и (1282–1347) (редиректы). Их отсутствие доказывает срабатывание **раннего выхода** на строках [1181–1186](Configuration%20Management/Services/OneCUpdatesService.cs:1181).
2. Ранний выход срабатывает при `HasPortalSessionCookie() == true`, т.е. в общем [`_cookieContainer`](Configuration%20Management/Services/OneCUpdatesService.cs:60) уже есть cookie с именем `TGC` / `JSESSIONID*` / `*session_id*` / `SESSION`.
3. `HasPortalSessionCookie()` ([2039–2062](Configuration%20Management/Services/OneCUpdatesService.cs:2039)) проверяет **только имена** cookie, без атрибутов (`Domain`, `Path`, `Secure`) и без проверки «живости» сессии на сервере.
4. Любая cookie из `Set-Cookie` ответа POST попадает в контейнер через [`ApplySetCookieToContainer`](Configuration%20Management/Services/OneCUpdatesService.cs:1524) — включая промежуточные/заглушечные cookie WAF/CDN с подходящим именем, которые сервер при следующем запросе не принимает.

#### A1.2 Корень: ложный «ранний успех» по имени cookie

Цепочка в 0.3.9.306:

1. Первая попытка операции: `SendWithAuthAsync` ([994–1126](Configuration%20Management/Services/OneCUpdatesService.cs:994)) получает 302 на `login.1c.ru` → `needsLogin=true` (1021–1023) → `CanAttemptPortalLogin()` → `TryLoginPortalAsync`.
2. `TryLoginPortalAsync` проходит GET формы → POST → (возможно) цепочку редиректов; в контейнер через `ApplySetCookieToContainer` (1273, 1524) попадает cookie типа `JSESSIONID` (например, от WAF или от незавершённой цепочки CAS). Сессионная cookie формально «есть».
3. Следующая операция/повтор: `HasPortalSessionCookie()` (1181) возвращает true → `Success` **без реального входа** (нет GET/POST — подтверждено логом).
4. Повтор исходного запроса каталога → сервер не знает эту cookie → снова 302 на `login.1c.ru` → маркер `retryAfterLoginStill302` ([1088–1095](Configuration%20Management/Services/OneCUpdatesService.cs:1088)) → ответ с редиректом как есть ([1102–1103](Configuration%20Management/Services/OneCUpdatesService.cs:1102)) → `AuthRequired` в `FetchPageCoreAsync` ([929–936](Configuration%20Management/Services/OneCUpdatesService.cs:929)) / `CheckForUpdatesAsync` ([269–281](Configuration%20Management/Services/OneCUpdatesService.cs:269)).

#### A1.3 Ограничение «второй вход не выполняется»

Локальный флаг `loginTried` ([1006](Configuration%20Management/Services/OneCUpdatesService.cs:1006), взводится в 1027 и 1065) разрешает **не более одной попытки входа за вызов** `SendWithAuthAsync`. При `retryAfterLoginStill302=true` (1088–1095) второй вход не запускается — снимается единственная рабочая возможность «свежая форма → новый execution → повторный POST». Защита от сжигания лимита (0.3.9.303) осталась, но теперь блокирует легитимный сценарий.

#### A1.4 Что уже корректно (не требует изменений)

- Динамический сбор полей формы [`ExtractFormFields`](Configuration%20Management/Services/OneCUpdatesService.cs:1733) (execution/lt/CSRF), атрибут `action` [`ExtractFormAction`](Configuration%20Management/Services/OneCUpdatesService.cs:1776), `ResolveFormPostUrl` (1802).
- POST на `action` с `Referer`/`Origin`, HTTP/1.1 (`LoginHttpVersion`, 65; 1205, 1256).
- Детектор «фантомного успеха» при 2xx без session-cookie/Set-Cookie (1349–1371).
- Следование JS/meta-refresh (`ExtractBodyRedirectUrl`, 1986; `FollowLoginRedirectsAsync`, 1668).
- Лимит попыток `MaxPortalLoginAttempts=3` + кулдаун 10 мин (`CanAttemptPortalLogin`, 1420–1463), автосброс.
- Честные статусы AuthRequired/AuthFailed/LoginLimitReached/FormUnavailable и маппинг ключей ([`AuthErrorKey`](Configuration%20Management/Services/OneCUpdatesService.cs:2110), [`PlatformUpdateService.Failure`](Configuration%20Management/Services/PlatformUpdateService.cs:289)).
- UI-панель действий при ошибке авторизации в окнах (кнопки «Открыть login.1c.ru», «Учётные данные ИТС…»).
- User-Agent из версии сборки ([46–47](Configuration%20Management/Services/OneCUpdatesService.cs:46)).

### A2. Предлагаемые изменения

Все правки — в общем сервисе [`OneCUpdatesService.cs`](Configuration%20Management/Services/OneCUpdatesService.cs) (обе платформы используют его напрямую; отдельные платформенные ветки кластера A не требуются).

| № | Файл / метод | Что изменить |
|---|--------------|--------------|
| A-1 | [`OneCUpdatesService.cs`](Configuration%20Management/Services/OneCUpdatesService.cs:1181) — ранний выход `TryLoginPortalAsync` | **Убрать мгновенный `Success` по имени cookie.** Заменить на честную проверку «живой» сессии: выполнить GET исходного URL (переданного как `loginUrl` не используется; нужен целевой URL — передать в `TryLoginPortalAsync` дополнительным параметром либо выполнять проверку в `SendWithAuthAsync` до вызова входа). Критерий «сессия жива»: ответ не 3xx и не страница входа (`LooksLikeLoginForm`). Если сессия мертва — удалить cookie хостов `login.1c.ru`/`releases.1c.ru` (метод `ClearPortalCookies()`) и выполнить полный вход (GET формы → POST). |
| A-2 | [`OneCUpdatesService.cs`](Configuration%20Management/Services/OneCUpdatesService.cs:1006) — `loginTried` в `SendWithAuthAsync` | Заменить bool-флаг на счётчик `int _loginTriedCount` с лимитом на операцию, например `const int MaxLoginAttemptsPerOperation = 2`. Сбрасывать в начале каждого вызова `SendWithAuthAsync`. В обоих местах запуска входа (1025, 1063) и в блоке повторного 302 (1088–1095) разрешить следующий вход, пока счётчик < лимита **и** `CanAttemptPortalLogin()`. |
| A-3 | [`OneCUpdatesService.cs`](Configuration%20Management/Services/OneCUpdatesService.cs:1088) — блок `retryAfterLoginStill302` | Вместо Warn-маркера с остановкой: при `_loginTriedCount < MaxLoginAttemptsPerOperation` выполнить повторный вход со **свежей формой** (`TryLoginPortalAsync` с редирект-URL), перед этим `ClearPortalCookies()` (снять мусорные cookie). При успехе — перестроить запрос и `continue`; при исчерпании повторов — оставить Warn-маркер (диагностика) и вернуть ответ как есть (1102–1103). |
| A-4 | [`OneCUpdatesService.cs`](Configuration%20Management/Services/OneCUpdatesService.cs:2039) — `HasPortalSessionCookie` | Ужесточить: учитывать cookie только с `Domain`, оканчивающимся на `.1c.ru` (или host `login.1c.ru`/`releases.1c.ru`), `Path=/` (либо Path, покрывающий `/`). Имя — как сейчас (TGC/JSESSIONID/session_id/SESSION). Метод остаётся вспомогательным; основной критерий успеха — результат пробной проверки (A-1), а не наличие cookie. |
| A-5 | Новый приватный метод `ClearPortalCookies()` | Удалить из `_cookieContainer` cookie для хостов `login.1c.ru` и `releases.1c.ru` (без влияния на Basic Auth в заголовках). Вызывается перед повторным входом (A-3) и при «мёртвой» сессии (A-1). |
| A-6 | [`OneCUpdatesService.cs`](Configuration%20Management/Services/OneCUpdatesService.cs:1198) — диагностика | В начало `TryLoginPortalAsync` (до раннего выхода/проверки) добавить `_logger.Info("[Updates] Вход: cookie контейнера: {DescribeContainerCookies()}")` — чтобы в логе было видно, какая cookie присутствовала и почему принято решение. `DescribeContainerCookies` уже существует (2071–2105, без значений). |
| A-7 | `PlatformUpdateService.cs`, ViewModel-ы, окна, локализация | **Без изменений логики**: статусы, ключи `Updates.*`/`PlatformUpdate.Error.*` и совет `PlatformUpdate.AuthAdvice` уже готовы и корректны. При реализации проверить, что после A-1…A-6 пользователь видит раздельные `AuthFailed`/`AuthRequired`/`LoginLimitReached` (по критериям Задачи 2). |

Примечание по A-1: пробный GET «живости» должен выполняться по исходному целевому URL операции (каталог), а не по `login.1c.ru`. Для этого `TryLoginPortalAsync` получает опциональный параметр `string? probeUrl` от вызывающего (в `SendWithAuthAsync` это `current.RequestUri`). Если пробный GET вернул контент каталога — вход не нужен (`Success`), лимит попыток не тратится.

### A3. Юнит-тесты

Файл [`OneCUpdatesLoginFlowTests.cs`](ConfigurationManagement.Tests/OneCUpdatesLoginFlowTests.cs) (существующие fake-handler-ы `LoginCaptureHandler`, `Post200LoginHandler`, `StaticLoginHandler` переиспользуются; `ChainedLoginHandler` — для «первый вход фантом → повторный успех»):

1. **`PreSeededSessionCookie_DoesNotShortCircuitLogin_WhenSessionIsDead`** — в контейнер заранее добавлена `JSESSIONID` (заглушка), handler: каталог → 302 на login → форма → POST → 302 → `security_check` → 200 с контентом. Ожидание: выполняется полный вход (GET+POST ≥ 1), результат `Ok`, а не мгновенный `Success` без POST.
2. **`PreSeededCookie_RemovedBeforeRelogin`** — мусорная cookie хоста `login.1c.ru` удаляется перед повторным входом (проверить через `DescribeContainerCookies()`/инвентаризацию после операции).
3. **`RetryStill302_SecondLoginWithFreshForm_ThenCatalogOk`** — сценарий из лога 7OH: «успешный» вход → повтор 302 → **выполняется второй вход** (2-й POST, свежая форма с новым `execution`) → успех → каталог Ok. Ожидание: `PostLoginCount == 2`, результат `Ok`, в логе нет/есть маркер по правилу (маркер Warn допускается только после исчерпания повторов).
4. **`LoginLoopInsideSingleCall_LimitedToTwoAttempts`** — сервер всегда 302, POST всегда возвращает 200 с формой: за один вызов не более 2 POST, затем ответ как есть → `AuthFailed`, `_portalLoginAttempts` сессии не исчерпан полностью (сохранён резерв для следующих операций).
5. **`LiveSessionProbe_SkipsFullLogin`** — при живой сессии (пробный GET каталога даёт контент, не форму) вход не выполняется (0 POST), лимит не тратится.
6. Существующий **`SendWithAuthAsync_RetryStill302_LogsPhantomSuccessMarker`** — переработать под новый контракт: при наличии лимита повторов и возможности второго входа результат может стать `Ok` (см. тест 3); маркер оставить для сценария «повторы исчерпаны».

Сопутствующие (без изменений тел, только прогон): `UpdateCheckCatalogTests.cs`, `PlatformUpdateServiceTests.cs`, `PlatformUpdateViewModelTests.cs`, `PlatformDownloadViewModelTests.cs` — проверяют статусные цепочки; после A-1…A-6 должны остаться зелёными (контракты `PortalFetchStatus`/`ConfigUpdateStatus`/`PlatformCatalogResult` не меняются).

### A4. Риски и fallback

| Риск | Влияние | Митигация / fallback |
|------|---------|----------------------|
| Портал введёт CAPTCHA/OAuth (форма без `execution`) | Программный вход невозможен | Уже распознаётся `LooksLikeOAuthOrChallenge` → `FormUnavailable` + совет открыть браузер. Дальнейший шаг (вне скоупа 0.3.9.307): импорт cookie из браузера пользователя — зафиксировать в комментарии к issue как направление. |
| Анти-брутфорс портала (временная блокировка аккаунта при неверном пароле) | Вход заблокирован на стороне 1С | Сохранить `MaxPortalLoginAttempts=3` + кулдаун 10 мин + явное сообщение `LoginLimitReached`; не увеличивать лимит. Повторный вход внутри операции ограничен `MaxLoginAttemptsPerOperation=2`. |
| Форма меняет имена полей/требует новые токены | POST 401 | Динамический сбор полей уже есть; при отсутствии `execution` → `FormUnavailable`. |
| Пробный GET «живости» удлиняет операцию на один запрос | Замедление, рост нагрузки | Только при уже установленной session-cookie (редкий случай после успешного входа ранее); в остальных случаях — сразу полный вход. |
| `retryAfterLoginStill302` маркер исчезнет из логов после фикса | Потеря диагностического признака | Маркер сохраняется при исчерпании повторов (A-3); дополнительно инвентаризация cookie (A-6). |
| Живой вход с реальными кредами ИТС автору недоступен | Критерий «каталог получен» проверяем только пользователем | В комментарии к issue — шаги проверки; юнит-тесты покрывают все ветки цепочки. |

### A5. Критерии готовности

Технические:
- `dotnet test` зелёный, включая новые тесты A3.1–A3.6; кросс-сборка Linux (`dotnet build -p:BuildLinux=true` или актуальный ключ из скриптов) без ошибок.
- Логика «cookie-заглушка не даёт ложного Success» и «302 после входа → повторный вход со свежей формой → успех» покрыта тестами.
- В логе при повторном 302 видны: инвентаризация cookie до входа, факт повторного POST, итоговый статус.

Ручные (пользователь 7OH, обязательны к публикации в комментариях):
1. Настроена учётная запись ИТС («Основная»), F9 по базе с типовой конфигурацией → в окне «Последняя версия» и кликабельная ссылка каталога, а не «Требуется вход на portal.1c.ru».
2. «Утилиты → Скачивание версии платформы 1С» → список версий загружается; выбор версии/разрядности/типа, скачивание, «Открыть папку»/«Запустить установщик» работают.
3. «Утилиты → Автообновление платформы 1С» (Ctrl+F9) → список версий Platform83 загружается; доступна проверка Platform85 (ник параметризован).
4. В журнале окна/приложения отсутствует `retryAfterLoginStill302=true` (при успешном входе).
5. С неверным паролем ИТС — понятное сообщение «вход не подтверждён» без технических деталей.

---

## Кластер B — снятие выделения после закрытия контекстного меню (#340)

### B1. Диагноз по коду

#### B1.1 Почему файл трассировки не создаётся/не наполняется

1. **Startup-запись пишется только внутри `Log()`**: [`WriteStartupIfNeeded`](Configuration%20Management/Services/MenuCloseTrace.cs:70) вызывается единственной точкой — строка 44 метода `Log`. Пока ни разу не выполнен ни один `MenuCloseTrace.Log(...)`, файл [`menuclose_trace.json`](Configuration%20Management/Services/MenuCloseTrace.cs:26) вообще не создаётся (каталог тоже).
2. **Вызовы `Log()` условны**. Полный список точек (по результатам поиска):
   - [`MainWindow.Hotkeys.cs:799`](Configuration%20Management/Views/MainWindow.Hotkeys.cs:799) `TryApply` — **только после** прохождения guard-цепочки [`TryApplyTreeClickAfterMenuClosed`](Configuration%20Management/Views/MainWindow.Hotkeys.cs:746): меню === `MainTree.ContextMenu` (748), мышь не над пунктом меню (755), **`Mouse.LeftButton == Pressed` (757)**, позиция внутри дерева (761–763), `InputHitTest` по строке базы (765–771), **клик без Ctrl/Shift** (779–785). Любой невыполненный guard → возврат **без единой записи**.
   - [`MainWindow.Events.cs:657`](Configuration%20Management/Views/MainWindow.Events.cs:657) `MouseDown` — только если `menuCloseSnapshotPresent` (снимок записан ранее).
   - [`MainWindow.Events.cs:851`](Configuration%20Management/Views/MainWindow.Events.cs:851) `MouseUp` — только если снимок был.
   - [`MainWindow.xaml.cs:111`](Configuration%20Management/Views/MainWindow.xaml.cs:111) `Deactivated` — только если pending/снимок.
   - `Fallback`/`EnsureStable` — только при выполнении соответствующих веток.
   - Avalonia-аналоги: [`MainWindow.Avalonia.Events.cs:194/225/245/261/277/292/300`](Configuration%20Management/Views/MainWindow.Avalonia.Events.cs:194) и далее; [`MainWindow.Avalonia.cs:332`](Configuration%20Management/Views/MainWindow.Avalonia.cs:332).
3. **События открытия/закрытия меню не логируются**: [`OnContextMenuOpened`/`OnContextMenuClosed`](Configuration%20Management/Views/MainWindow.Hotkeys.cs:707) (класс-обработчики, подписаны в [`MainWindow.xaml.cs:226–233`](Configuration%20Management/Views/MainWindow.xaml.cs:226)) не содержат вызовов `MenuCloseTrace.Log`. При закрытии меню по ESC или кликом мимо строки/по пункту меню записей нет вовсе — пользователь видит «в файл ничего не записывается при закрытии меню».
4. **Имя файла не совпало с ожиданием пользователя**: код пишет `menuclose_trace.json`, пользователь создал `trace.json` (семантика «файл-флаг»). Строка 26 (`FileName`) и комментарий пользователя (2026-10-05T06:15:18Z).
5. **Портативный режим**: [`PlatformPaths.AppDataDirectory`](Configuration%20Management/Services/PlatformPaths.cs:21) может указывать на каталог рядом с exe (`PortablePaths.TryResolveDataDirectory()`) — при поиске «рядом с настройками» пользователь мог смотреть в другой каталог; путь фиксируется только в startup-записи, которой нет.

#### B1.2 Почему выделение «пропадает через мгновение» (гипотеза, согласованная с кодом 0.3.9.306)

1. Штатный клик по строке дерева без модификаторов: [`OnInfobaseTree_PreviewMouseLeftButtonDown`](Configuration%20Management/Views/MainWindow.Events.cs:619) → `ClearBatchSelection` + `ApplySelection` (770–771).
2. Стабилизация `EnsureSelectionStable` запускается **только при `menuCloseSnapshotPresent`** ([781–782](Configuration%20Management/Views/MainWindow.Events.cs:781)).
3. Снимок записывается только в `TryApplyTreeClickAfterMenuClosed` (см. B1.1 п.2). Если он не записан (guard 757 «кнопка не нажата» — при закрытии попапа WPF часто уже Released; либо клик мимо строки/по пункту) — стабилизация **не запускается**.
4. После закрытия попапа `VirtualizingStackPanel` с `VirtualizationMode=Recycling` перерабатывает контейнеры; у переиспользуемого `TreeViewItem` `IsSelected` сбрасывается (двусторонней привязки к модели нет — отмечено в комментарии [`MainWindow.Tree.cs:835–837`](Configuration%20Management/Views/MainWindow.Tree.cs:835)) → визуально «строка активна → через мгновение выделение пропадает».
5. Avalonia: та же схема — снимок в [`OnTreeMenuCloseClickDedup_PointerPressed`](Configuration%20Management/Views/MainWindow.Avalonia.Events.cs:161), стабилизация при `menuCloseSnapshotPresent` (171, 192–196).

То есть корень симптома: **признак «клик был сразу после закрытия меню» слишком узкий** (опирается на успешную запись снимка), а стабилизация нужна для любого обычного клика в коротком окне после закрытия меню.

### B2. Предлагаемые изменения

| № | Файл | Что изменить |
|---|------|--------------|
| B-1 | [`MenuCloseTrace.cs`](Configuration%20Management/Services/MenuCloseTrace.cs:23) | (1) Добавить публичный `public static void EnsureStarted()` — пишет startup-запись вне зависимости от наличия событий (вызывает `WriteStartupIfNeeded` под lock). (2) Имя файла: основной — **`trace.json`** (соглашение с пользователем); если рядом существует legacy `menuclose_trace.json` (от 0.3.9.306) — дописывать в него (непрерывность), иначе — в `trace.json`. Реализация: `ResolvePath()` выбирает существующий файл либо `trace.json`. |
| B-2 | [`MainWindow.xaml.cs`](Configuration%20Management/Views/MainWindow.xaml.cs:215) (WPF) и [`MainWindow.Avalonia.cs`](Configuration%20Management/Views/MainWindow.Avalonia.cs:331) (Avalonia) | В конструкторе/OnLoaded вызвать `MenuCloseTrace.EnsureStarted()` — **файл появляется при каждом старте** независимо от действий пользователя; startup-запись содержит версию, платформу, ОС и `appDataDir` (уже в `BuildStartupLine`). |
| B-3 | [`MainWindow.Hotkeys.cs:707`](Configuration%20Management/Views/MainWindow.Hotkeys.cs:707) (WPF) | В `OnContextMenuOpened`/`OnContextMenuClosed` добавить **безусловные** записи: `MenuCloseTrace.Log($"MenuOpened: ...")`, `MenuCloseTrace.Log($"MenuClosed: ...")`. В `OnContextMenuClosed` дополнительно зафиксировать `_lastMenuCloseTick = Environment.TickCount` (новое поле). |
| B-4 | [`MainWindow.Events.cs:619`](Configuration%20Management/Views/MainWindow.Events.cs:619) (WPF) | В ветке обычного клика без модификаторов (после `ApplySelection`, ~771) запускать `EnsureSelectionStable`, если `menuCloseSnapshotPresent` **или** прошло не более `MenuCloseStabilizeWindowMs` (например, 1500 мс) с `_lastMenuCloseTick`. Предикат вынести в чистый метод `BatchSelectionHelper.ShouldStabilizeAfterMenuClose(...)` (см. B-7) — тестируемость. |
| B-5 | [`MainWindow.Tree.cs:852`](Configuration%20Management/Views/MainWindow.Tree.cs:852) | `EnsureSelectionStable` — без изменений алгоритма; при необходимости по журналу пользователя (следующая итерация) расширить окно «догоняния» (сейчас 15 проходов/1,5 с + 800 мс). |
| B-6 | Avalonia: [`MainWindow.Avalonia.Events.cs:161`](Configuration%20Management/Views/MainWindow.Avalonia.Events.cs:161) | Зеркально B-3/B-4: подписка на `MenuClosed` контекстного меню дерева (`_tree.ContextMenu`) — `Log` + `_lastMenuCloseTick`; в `OnTreeMenuCloseClickDedup_PointerPressed` расширенный признак стабилизации. (`AttachTreeMenuCloseClickDedup` уже подписан на PointerPressed/PointerReleased, 155–159.) |
| B-7 | [`BatchSelectionHelper.cs`](Configuration%20Management/Services/BatchSelectionHelper.cs:202) | Новый чистый метод: `public static bool ShouldStabilizeAfterMenuClose(bool snapshotPresent, bool isPlainLeftClickWithoutModifiers, long lastMenuCloseTick, long nowTick, long windowMs) => isPlainLeftClickWithoutModifiers && (snapshotPresent || (lastMenuCloseTick > 0 && nowTick >= lastMenuCloseTick && nowTick - lastMenuCloseTick <= windowMs));` — единая семантика для WPF и Avalonia, покрывается юнит-тестами. |
| B-8 | README + комментарий к issue | Задокументировать соглашение: файл `trace.json` (JSONL) рядом с настройками (`%APPDATA%\ConfigurationManagement\` / `~/.config/ConfigurationManagement/`, либо каталог рядом с exe в портативном режиме); создаётся при старте; подробный журнал событий меню/клика/стабилизации; legacy `menuclose_trace.json` продолжает дополняться при наличии. |

Примечание по B-3/B-4: расширенный признак **не заменяет** дедупликацию `IsSameClick` (снимок остаётся для распознавания повторной доставки и отмены fallback) — он лишь расширяет запуск стабилизации. `Ctrl`/`Shift`-клики стабилизацией не затрагиваются (предикат требует клик без модификаторов).

### B3. Юнит-тесты

- [`MenuCloseTraceFormatTests.cs`](ConfigurationManagement.Tests/MenuCloseTraceFormatTests.cs):
  - `StartupLine_UsedByEnsureStarted_ContainsVersionPlatformAndDir` (формат уже покрыт `BuildStartupLine`; добавить тест выбора пути: при отсутствии legacy-файла путь = `trace.json`, при наличии `menuclose_trace.json` — он).
- [`BatchSelectionHelperTests.cs`](ConfigurationManagement.Tests/BatchSelectionHelperTests.cs):
  - `ShouldStabilizeAfterMenuClose_SnapshotPresent_TrueForPlainLeftClick`;
  - `ShouldStabilizeAfterMenuClose_RecentMenuClose_TrueWithinWindow`;
  - `ShouldStabilizeAfterMenuClose_OutsideWindow_False`;
  - `ShouldStabilizeAfterMenuClose_NoMenuCloseNoSnapshot_False`;
  - `ShouldStabilizeAfterMenuClose_CtrlClick_False` (модификаторы не стабилизируются).
- Существующие `MenuCloseTraceFormatTests`/`BatchSelectionHelperTests` — без регрессий (сигнатуры `Log`, `BuildLine`, `TruncateToLimit`, `MaskSensitive`, `IsSameClick`, `ShouldRecordMenuCloseSnapshot`, `DecideSelectionRestore` не меняются).

### B4. Риски и fallback

| Риск | Влияние | Митигация / fallback |
|------|---------|----------------------|
| Стабилизация на каждый обычный клик в окне 1,5 с вмешается в выбор пользователя | Конфликт с быстрыми действиями | Ограничение окном + проверка `ReferenceEquals(SelectedInfobase, target)` внутри `EnsureSelectionStable` (уже есть, прерывает работу при перевыборе); предикат только для кликов без модификаторов. |
| На машине пользователя файл-флаг «не сработает» снова (имя/каталог) | Диагностика не наполняется | Startup-запись при каждом старте (B-2) + запись при каждом открытии/закрытии меню (B-3) + документирование точного пути в комментарии и README. |
| Баг воспроизводится, но журнал покажет, что стабилизация срабатывала | Гипотеза B1.2 неверна | По журналу (Dump500ms/EnsureStable) определить звено (переработка контейнера, потеря SelectedItem, повторная доставка); итерация в следующей версии. |
| Различия виртуализации WPF/Avalonia | Фикс на одной платформе не срабатывает на другой | Зеркальные правки обеих веток; ручная проверка на обеих платформах (критерий пользователя). |
| Запись в trace.json при каждом меню — рост файла | Размер | Уже есть круговое усечение ~512 КБ (`MenuCloseTraceFormat.TruncateToLimit`), события редки. |

### B5. Критерии готовности

Технические:
- `dotnet test` зелёный (включая новые тесты B3); кросс-сборка Linux без ошибок.
- `MenuCloseTrace.EnsureStarted()` создаёт `trace.json` со startup-записью при старте (проверяется локально/тестом пути).
- Записи `MenuOpened`/`MenuClosed` появляются при каждом открытии/закрытии контекстного меню (без каких-либо guard-условий).

Ручные (пользователь 7OH):
1. Обновиться на версию кластера B (0.3.9.308). После запуска рядом с настройками появился `trace.json` со startup-записью.
2. Повторить сценарий: мультивыделение (Ctrl/Shift) **или просто ПКМ** по строке → левый клик по другой строке → в `trace.json` появились записи `MenuOpened`/`MenuClosed`/`MouseDown`(или `PointerPressed`)/`Dump500ms`/`EnsureStable`.
3. Строка, по которой кликнули, остаётся активной; выделение НЕ пропадает «через мгновение»; 10+ повторений стабильно на Windows и Linux.

---

## Порядок передачи в задачи-исполнители

- **Задача 4 (версия 0.3.9.307)** — только раздел «Кластер A»: правки A-1…A-6 + тесты A3. Файлы: [`OneCUpdatesService.cs`](Configuration%20Management/Services/OneCUpdatesService.cs), [`OneCUpdatesLoginFlowTests.cs`](ConfigurationManagement.Tests/OneCUpdatesLoginFlowTests.cs). ViewModel-ы/окна/локализация не меняются (только прогон тестов).
- **Задача 5 (версия 0.3.9.308)** — только раздел «Кластер B»: правки B-1…B-8 + тесты B3. Файлы: [`MenuCloseTrace.cs`](Configuration%20Management/Services/MenuCloseTrace.cs), [`MenuCloseTraceFormat.cs`](Configuration%20Management/Services/MenuCloseTraceFormat.cs), [`BatchSelectionHelper.cs`](Configuration%20Management/Services/BatchSelectionHelper.cs), [`MainWindow.xaml.cs`](Configuration%20Management/Views/MainWindow.xaml.cs), [`MainWindow.Hotkeys.cs`](Configuration%20Management/Views/MainWindow.Hotkeys.cs), [`MainWindow.Events.cs`](Configuration%20Management/Views/MainWindow.Events.cs), [`MainWindow.Tree.cs`](Configuration%20Management/Views/MainWindow.Tree.cs), [`MainWindow.Avalonia.cs`](Configuration%20Management/Views/MainWindow.Avalonia.cs), [`MainWindow.Avalonia.Events.cs`](Configuration%20Management/Views/MainWindow.Avalonia.Events.cs), тесты [`MenuCloseTraceFormatTests.cs`](ConfigurationManagement.Tests/MenuCloseTraceFormatTests.cs)/[`BatchSelectionHelperTests.cs`](ConfigurationManagement.Tests/BatchSelectionHelperTests.cs), README.
- Конфликтов файлов между задачами нет (кластер A — сервис/тесты, кластер B — окна/сервисы трассировки); коммиты линейны, версии присваиваются последовательно (см. план 307, Задачи 6–9).

## Связанные документы

- [`PLAN-0.3.9.307.md`](../PLAN-0.3.9.307.md) — общий план цикла (Задачи 0–9).
- [`publish/issues_requirements_2026-10-05.md`](../publish/issues_requirements_2026-10-05.md) — требования и критерии проверки (Задача 2).
- [`plans/PLAN-0.3.9.306.md`](PLAN-0.3.9.306.md) — решения 0.3.9.306 (трассировка, CAS-итерация).
- [`plans/PLAN-0.3.9.301-323-330-334-345.md`](PLAN-0.3.9.301-323-330-334-345.md) — гипотезы CAS (подтверждены/опровергнуты в A1–A2).
- [`issues_details.json`](../issues_details.json), [`issues_analysis.json`](../issues_analysis.json) — полные комментарии и требования.