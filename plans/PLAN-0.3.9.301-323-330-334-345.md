# PLAN 0.3.9.301 — CAS-авторизация portal.1c.ru: #334, #330, #323 + Platform85; публикация анализа #345

Дата: 2026-10-04. Текущая версия: **0.3.9.300**. Следующая микро-версия: **0.3.9.301**.
Источники кандидатов: открытые issues GitHub по состоянию на 2026-10-04 (9 открытых: #345, #342, #340, #334, #330, #323, #321, #309, #305), логи пользователя в последних комментариях #334/#330/#323.
Двуплатформенный проект: Windows/WPF (`#if WINDOWS`, `*.xaml`/`*.xaml.cs`) и Linux/Avalonia (`#if LINUX`, `*.Avalonia.cs`). Common-логика (сервисы, модели, ViewModel) — общая.

**В скоупе:** #334, #330, #323 (единый корень — вход на portal.1c.ru) и #345 (анализ, БЕЗ изменения кода).
**Вне скоупа (уже исправлено в 0.3.9.300, комментарии от sivatorov опубликованы):** #342, #340, #321, #309, #305 — не трогаем.

**Ограничения реализации:** только код + юнит-тесты; issues не закрывать, ничего не коммитить без согласования. Реализация выполняется другой задачей в code-режиме строго по этому плану.

**Правило пользователя «где нет комментариев — изменения делать на основе описания»** относится к #345 (0 комментариев): описание требует **анализа** и решения сообща — поэтому для #345 план предусматривает только анализ и публикацию комментария с вопросами, код НЕ меняется.

---

## Политика версий (объём релиза)

- **Релиз A — 0.3.9.301:** единая группа **#334/#330/#323** — починка CAS-входа на portal.1c.ru в [`OneCUpdatesService`](Configuration%20Management/Services/OneCUpdatesService.cs) + доработка каталога `Platform85` (#334, пожелание пользователя). Требует живой проверки с реальными учётными данными ИТС (см. риски). В этом же релизе публикуется комментарий-анализ по **#345** (код не меняется).
- **Релиз B — 0.3.9.302 (условно):** вторая итерация по данным диагностических логов из 0.3.9.301, если 401 сохранится; либо реализация решения по #345, если пользователь после анализа примет решение «упростить/убрать» связывание. План релиза B составляется отдельно по факту результатов A.

Обоснование: проблема CAS не может быть подтверждена без живого входа (портал 1С), поэтому фикс делается «максимально вероятным + диагностируемым» сразу, а не разбивается на две версии вслепую; #345 — аналитическая задача, отдельная от сетевых правок.

---

## Группа 1. Единый корень #334/#330/#323: программный вход на portal.1c.ru не подтверждается (401)

### 1.1 Факты из логов пользователя (2026-10-03)

**#334** (21:45:17):
```
[INFO]  Обновление платформы: получение каталога версий с портала 1С
[INFO]  [Updates] Вход на portal.1c.ru: учётная запись 'Основная', форма: https://login.1c.ru/login?service=...security_check
[WARN]  [Updates] Вход на portal.1c.ru не подтверждён (status=401).
[INFO]  [Updates] Редирект 302 (шаг 0): 'https://login.1c.ru/login?service=...' для 'https://releases.1c.ru/project/Platform83'
[WARN]  [Updates] Не удалось получить страницу (пустое тело или HTTP-ошибка): https://releases.1c.ru/project/Platform83
[WARN]  [PlatformUpdate] Пустой ответ или HTTP-ошибка: ...
[WARN]  Обновление платформы: каталог не получен — PlatformUpdate.Error.NetworkError
```
Плюс пожелание: «стоит проверять и https://releases.1c.ru/project/Platform85».

**#330** (21:50:04): та же цепочка 302 → «Пустой ответ или HTTP-ошибка» → `PlatformUpdate.Error.NetworkError`, но **строки «Вход на portal.1c.ru» в логе НЕТ** — попытка входа не выполнялась вовсе.

**#323** (21:48:46): `HTTP 302 для 'https://releases.1c.ru/project/Accounting30' (requestUri=<тот же>)` — тоже без повторной попытки входа, сообщение пользователя «Всё ещё редирект».

**Вывод:** в сессии приложения первый вход завершился 401 (#334), после чего флаг `_portalLoginAttempted` «отравил» всю сессию: следующие окна (#330, #323) уже не пробуют вход, просто получают 302 на login.1c.ru и показывают NetworkError / AuthRequired.

### 1.2 Диагноз по коду

| № | Место | Что происходит |
|---|-------|----------------|
| 1 | [`SendWithAuthAsync`](Configuration%20Management/Services/OneCUpdatesService.cs:872) | Гибридная авторизация: вход запускается при 401/403 или редиректе на login.1c.ru, **один раз за сессию службы** — `needsLogin && !_portalLoginAttempted` (строки 895–910). После неудачного входа флаг остаётся `true`, и все последующие запросы (новые окна) идут без входа. Аналогично ветка self-redirect 302 без Location (строки 924–941) также подчинена `_portalLoginAttempted`. |
| 2 | [`TryLoginPortalAsync`](Configuration%20Management/Services/OneCUpdatesService.cs:1022) | GET формы входа → извлечение только токена `execution` ([`ExtractFormExecution`](Configuration%20Management/Services/OneCUpdatesService.cs:1185)) → POST с **жёстко захардкоженным набором полей** (строки 1052–1063): `username, password, execution, _eventId=submit, rememberMe, anotherComputer, geolocation, inviteCode, inviteType`. Стандартное поле Spring Security CAS **`lt`** (login ticket) в списке ОТСУТСТВУЕТ. Если форма портала изменилась (новые/обязательные скрытые поля, CSRF-токен, `lt`) — сервер отклоняет POST статусом 401. |
| 3 | [`TryLoginPortalAsync`](Configuration%20Management/Services/OneCUpdatesService.cs:1100) | При `postStatus == 401` пишется только строка «Вход не подтверждён (status=401)», **тело ответа не читается** — невозможно отличить «неверный логин/пароль» от «устаревший execution/lt» от «требуется капча». |
| 4 | [`GetTextAsync`](Configuration%20Management/Services/OneCUpdatesService.cs:848) / [`GetPageTextAsync`](Configuration%20Management/Services/OneCUpdatesService.cs:834) | Пустой ответ после 302 → `null` → ниже по стеку `PlatformUpdateService` маппит в `NetworkError` (не в `AuthRequired`/`AuthFailed`) — пользователь видит бессмысленную сетевую ошибку. |
| 5 | [`CheckForUpdatesAsync`](Configuration%20Management/Services/OneCUpdatesService.cs:194) | При 302 с редиректом на login.1c.ru ответ возвращается как есть (строки 948–949), затем код 232–238 не срабатывает (RequestUri остаётся releases.1c.ru, редирект не пройден) и срабатывает ветка 240–253: `HTTP 302 … requestUri=<тот же>` → `Updates.AuthRequired`. Логика корректна, но без рабочего входа симптом сохраняется. |
| 6 | [`GetCredentials`](Configuration%20Management/Services/OneCUpdatesService.cs:999) | Берёт запись «Основная» из `its_accounts.json` или устаревшие `UpdatesLogin/UpdatesPassword`. Возможна рассинхронизация с реальными рабочими данными пользователя (нужно подтверждение). |
| 7 | [`CreateHttpClient`](Configuration%20Management/Services/OneCUpdatesService.cs:132) | `AllowAutoRedirect=false`, общий `CookieContainer`, User-Agent жёстко «ConfigurationManagement/0.3.9.3» (строка 46). Потенциальные проблемы: HTTP/2 по умолчанию (некоторые Spring CAS-серверы некорректно работают с HTTP/2), отсутствие Referer/Origin при POST, старый User-Agent. |

### 1.3 Гипотезы причины 401 (проверять в порядке приоритета)

1. **Форма login.1c.ru изменилась** — появились обязательные скрытые поля (`lt`/CSRF), которые текущий POST не передаёт. Наиболее вероятно: стандартная форма Spring Security CAS содержит поле `lt`, которое мы не отправляем. Проверка: сравнить фактические `<input hidden>` формы (GET) с набором полей в POST; диагностический лог покажет имена полей.
2. **Учётные данные «Основной» в `its_accounts.json` устарели/не совпадают с браузерными.** Проверка: пользователь логинится на login.1c.ru в инкогнито теми же данными.
3. **Портал отклоняет POST из-за отсутствующих заголовков** (Referer/Origin) или из-за HTTP/2. Проверка: добавить Referer/Origin, принудительный HTTP/1.1 для запросов входа.
4. **Портал ввёл капчу/JS-челлендж** — программный вход невозможен в принципе. Fallback: понятное сообщение + кликабельная ссылка ручной проверки (уже есть с 0.3.9.287), в перспективе — перенос cookie из браузера (вне скоупа).

### 1.4 Изменения

#### 1.4.1 [`OneCUpdatesService.cs`](Configuration%20Management/Services/OneCUpdatesService.cs) — главный файл

1. **Динамический сбор полей формы вместо жёсткого списка:**
   - Заменить `ExtractFormExecution(html)` на общий `ExtractHiddenFormFields(html)` → `Dictionary<string,string>`: все `<input type="hidden" name="…" value="…">` + checkbox (`rememberMe`), устойчиво к порядку атрибутов и кавычкам (по образцу текущего regex, строки 1185–1201).
   - В [`TryLoginPortalAsync`](Configuration%20Management/Services/OneCUpdatesService.cs:1022) собирать POST так: обязательные `username`/`password`/`_eventId=submit` + все остальные поля **из фактической формы** (`execution`, `lt`, прочие). Если `execution` отсутствует — вход не выполняется с понятным предупреждением (как сейчас).
2. **Диагностика 401 без секретов:**
   - При `postStatus == 401` (и при 200-с-формой-ошибки) читать тело ответа и логировать анонимизированные признаки: размер HTML, наличие маркеров «Неверный логин и/или пароль» / «invalid» / «captcha» / «execution» / «lt», список имён полей формы. **Никогда** не логировать логин/пароль/execution (значения маскировать).
   - Добавить ключевые заголовки на POST формы: `Referer = formUrl`, `Origin = https://login.1c.ru`; рассмотреть принудительный `Version = HttpVersion.Version11` для запросов к login.1c.ru (гипотеза 3; вынести в константу, чтобы легко отключить).
3. **Отказ от «отравляющего» флага `_portalLoginAttempted` (строки 72, 895, 927):**
   - Заменить на счётчик `_portalLoginAttempts` с лимитом (например, 3 за сессию службы) + признак использованной учётной записи (`_lastAccountSignature` = хэш login+accountId, без пароля): при смене учётной записи счётчик сбрасывается. Так окно, открытое после неудачного входа, сможет попробовать снова, но анти-брутфорс защита портала не сработает от лавины запросов.
   - Лимит и политику вынести в `private const int MaxPortalLoginAttempts`.
4. **Разделение статусов авторизации:**
   - `TryLoginPortalAsync` возвращает enum `PortalLoginResult { Success, AuthFailed, NoCredentials, FormUnavailable, TooManyRedirects }` вместо `bool`.
   - `GetTextAsync`/`GetPageTextAsync`: при `AuthFailed` логировать и возвращать **специальный маркер** (например, `const string AuthFailedMarker = "__AUTH_FAILED__"` или новый метод `GetPageTextAsync` с out-статусом), чтобы `PlatformUpdateService` и `CheckForUpdatesAsync` отличали «учётные данные не приняты (401)» от «сеть недоступна» и от «нет учётных данных».
5. **User-Agent:** вынести текущую версию из `VersionInfo`/csproj вместо захардкоженного `0.3.9.3` (строка 46) — сервер может отдавать иные страницы под старым UA.
6. **Комментарии/XML-доки** методов обновить под новое поведение (файл активно используется тестами как эталон).

#### 1.4.2 [`PlatformUpdateService.cs`](Configuration%20Management/Services/PlatformUpdateService.cs)

- [`FetchTextAsync`](Configuration%20Management/Services/PlatformUpdateService.cs:160): добавить обработку статуса «авторизация не подтверждена»: при маркере `AuthFailed` (п. 1.4.1.4) возвращать `PortalFetchStatus.AuthFailed` вместо `NetworkError`/`AuthRequired`.
- [`Failure`](Configuration%20Management/Services/PlatformUpdateService.cs:226): новый статус → новый ключ локализации `PlatformUpdate.Error.AuthFailed`.
- Константа `LoginHostMarker` (строка 36) остаётся для страницы входа (AuthRequired).

#### 1.4.3 ViewModel-и и окна

- [`PlatformUpdateViewModel`](Configuration%20Management/ViewModels/PlatformUpdateViewModel.cs) (метод проверки/`AppendLog`): выводить понятное сообщение при `AuthFailed`: «Вход на portal.1c.ru не подтверждён (401): проверьте логин/пароль ИТС в справочнике».
- [`PlatformDownloadViewModel`](Configuration%20Management/ViewModels/PlatformDownloadViewModel.cs) (`LoadCatalogAsync`, строка ~353): то же самое.
- Окна `PlatformUpdateWindow` / `PlatformDownloadWindow` / `UpdateCheckWindow` (WPF `.xaml.cs` + Avalonia `.Avalonia.cs`): отображение нового ключа ошибки; кликабельную ссылку проверки в браузере (добавлена в 0.3.9.287) **не трогаем**.

#### 1.4.4 Локализация

- [`ru.json`](Configuration%20Management/Localization/Languages/ru.json) / `en.json`:
  - Новый ключ `Updates.AuthFailed`: «Вход на portal.1c.ru не подтверждён (401). Проверьте логин/пароль учётной записи ИТС в справочнике „Учетные данные ИТС"».
  - Новый ключ `PlatformUpdate.Error.AuthFailed` (аналогичный текст для окон платформы).
  - Существующие `Updates.AuthRequired` (строка 2603) и `PlatformUpdate.Error.AuthRequired` (строка 2844) — без изменений.

#### 1.4.5 Проверка ников конфигураций (#323)

- В логе #323 запрос идёт по `Accounting30` (правильный ник редакции 3.0 БП — см. [`BuiltInConfigTypes.cs:38`](Configuration%20Management/Services/BuiltInConfigTypes.cs:38)). Ранее был `AccountingCorp30`. Сверить все ники в [`BuiltInConfigTypes.cs`](Configuration%20Management/Services/BuiltInConfigTypes.cs) с реальными каталогами releases.1c.ru (тест 1.5.5). Код менять только при подтверждённом расхождении.

### 1.5 Тесты

1. **Новый `OneCUpdatesLoginFlowTests.cs`** (fake `HttpMessageHandler` через внутренний конструктор [`OneCUpdatesService`](Configuration%20Management/Services/OneCUpdatesService.cs:105)):
   - `PostIncludesAllHiddenFormFields`: GET формы с полями `execution` + `lt` → POST содержит оба поля (динамический сбор), значения username/password корректно кодируются.
   - `LoginPost401_ReturnsAuthFailed`: fake возвращает 401 на POST → результат `AuthFailed` (не NetworkError), повторный вызов не блокируется флагом.
   - `RetryAfterFailedLogin_PerformsSecondAttempt`: первая попытка 401, вторая — успешная CAS-цепочка (302 → `releases.1c.ru/public/security_check?ticket=ST-…` → 200) → `Success`, каталог получается.
   - `LoginAttemptsLimited`: более `MaxPortalLoginAttempts` попыток не выполняется.
   - `AccountSwitch_ResetsAttemptCounter`: смена учётной записи (другое имя/логин) → счётчик сбрасывается.
   - `Anonymized401Log_NoSecrets`: при 401 лог содержит признаки тела, но не содержит пароль/логин/execution.
   - `Http11ForLoginForm` (при включении гипотезы HTTP/1.1): запрос к login.1c.ru идёт с `Version = 1.1`.
2. **`UpdateCheckCatalogTests.cs`** (существующий): добавить сценарий `AuthFailed` — проверка каталога конфигурации при 401 → результат Failed с ключом `Updates.AuthFailed`, не NetworkError.
3. **`PlatformUpdateServiceTests.cs`**: маппинг `AuthFailed` → статус `PortalFetchStatus.AuthFailed` + ключ `PlatformUpdate.Error.AuthFailed`.
4. **`PlatformUpdateViewModelTests.cs` / `PlatformDownloadViewModelTests.cs`**: отображение ключа `AuthFailed` в журнале/ошибке окна.
5. **`BuiltInConfigTypesTests.cs`**: валидность ников — каждый `Nick`/`UrlOverride` соответствует паттерну `https://releases.1c.ru/project/<ник>`; список ников сверяется со справочным набором (Accounting, Accounting30, Accounting20_82, HRM30, Trade, Trade110, Trade103, ARAutomation, ARAutomation20/11/10, EnterpriseERP20, Retail, Retail30, Retail23 и т.д.).

---

## Группа 2. Каталог Platform85 (#334, пожелание пользователя)

### 2.1 Суть

Пользователь: «стоит проверять и https://releases.1c.ru/project/Platform85». Сейчас каталог платформы жёстко один: [`OneCPlatformCatalogParser.PlatformNick = "Platform83"`](Configuration%20Management/Services/OneCPlatformCatalogParser.cs:24), а [`PlatformUpdateService.BuildCatalogUrl()`](Configuration%20Management/Services/PlatformUpdateService.cs:202) строит URL только из него. `BuildVersionFilesUrl` (строки 209–223) тоже использует `PlatformNick`.

### 2.2 Изменения

- [`OneCPlatformCatalogParser.cs`](Configuration%20Management/Services/OneCPlatformCatalogParser.cs):
  - Добавить константы `Platform83Nick = "Platform83"`, `Platform85Nick = "Platform85"`; `PlatformNick` оставить как алиас `Platform83Nick` (обратная совместимость с тестами) либо заменить использование на список.
  - `public static readonly IReadOnlyList<string> SupportedPlatformNicks = [Platform83Nick, Platform85Nick];`
- [`IPlatformUpdateService.cs`](Configuration%20Management/Services/IPlatformUpdateService.cs):
  - Добавить свойство/метод для списка поддерживаемых ников каталога и параметризованный запрос: `Task<PlatformCatalogResult> GetAvailableReleasesAsync(string nick, CancellationToken ct = default)` (перегрузка; старая сигнатура — обёртка с `Platform83`), чтобы не ломать существующие вызовы и тесты.
- [`PlatformUpdateService.cs`](Configuration%20Management/Services/PlatformUpdateService.cs):
  - `BuildCatalogUrl()` → `BuildCatalogUrl(string nick)`; `BuildVersionFilesUrl(release, nick)` — параметризовать ником (брать из релиза, если он несёт ник, иначе из переданного).
  - `GetAvailableReleasesAsync(string nick, ...)`: валидация ника (только из `SupportedPlatformNicks` или любой непустой валидный ник — для будущих каталогов), лог с ником.
  - По желанию: последовательная проверка нескольких каталогов (Platform83 → Platform85) — вернуть результат «самой свежей найденной» версии либо список; **минимальный объём** для #334: параметризация + возможность проверки Platform85. Механика «или/или» согласовывается на этапе реализации (риск неоднозначности результата для пользователя).
- ViewModel-и окон платформы: при неуспехе каталога Platform83 — подсказка/кнопка проверки Platform85 (если позволит минимальный объём; в противном случае — только лог в журнале).

### 2.3 Тесты

- `OneCPlatformCatalogParserTests.cs`: парсинг HTML каталога Platform85 (та же таблица `#versionsTable`), `SupportedPlatformNicks` содержит оба ника.
- `PlatformUpdateServiceTests.cs`: `GetAvailableReleasesAsync("Platform85")` строит `https://releases.1c.ru/project/Platform85`; невалидный ник → NotFound/предупреждение, не исключение.
- `OneCUpdatesUrlTests.cs`: URL каталога для обоих ников.

---

## Группа 3. #345 — анализ функционала связывания базы (публикация, БЕЗ изменения кода)

### 3.1 Статус

Issue #345 (0 комментариев) — вопрос: «нужен ли функционал связи базы с типовой, если есть колонка "Имя конфигурации"?». Описание прямо требует: «Для начала просто нужно проанализировать — решение о действии примем сообща после анализа». Полный анализ **уже выполнен** и зафиксирован в [`plans/PLAN-0.3.9.300.md`](plans/PLAN-0.3.9.300.md) (Группа 9): карта использований, зависимость F9/«Актуальных релизов» от явной связи, три варианта с последствиями, рекомендация.

### 3.2 Что делает связывание (краткая сводка для плана)

- Окно «Связать с конфигурацией» ([`ConfigUpdateLinkWindow.xaml.cs:328`](Configuration%20Management/Views/ConfigUpdateLinkWindow.xaml.cs:328), `.Avalonia.cs:365`) — единственная точка ЗАПИСИ полей `UpdateConfigCode` / `UpdateUrlOverride` / `UpdateUrlSegment` ([`Infobase.cs:331`](Configuration%20Management/Models/Infobase.cs:331)); применяет связь ко всем базам с тем же `ConfigurationName`.
- F9 / «Проверка обновлений» (#323): явная связь имеет **приоритет** над автоопределением; автоопределение — [`ConfigTypeMatcher.FindByInfobaseName`](Configuration%20Management/Services/ConfigTypeMatcher.cs:67) (fallback по «Имени конфигурации»).
- Кэш результатов `UpdateCheckCache` индексируется по `UpdateConfigCode`; [`MaintenanceCenterViewModel.ResolveUpdateResult`](Configuration%20Management/ViewModels/MaintenanceCenterViewModel.cs:317) читает его.
- «Актуальные релизы» (ALT+F9) — явную связь НЕ используют ([`ActualReleasesViewModel.cs:54`](Configuration%20Management/ViewModels/ActualReleasesViewModel.cs:54)).
- Где явная связь — **единственный** способ получить каталог: имя базы ≠ имени/подстроки типовой; персональный ник (`UpdateUrlSegment`) или ручная ссылка (`UpdateUrlOverride`).

### 3.3 Рекомендация (материал для комментария)

**Рекомендация: оставить как есть.** Автоопределение по «Имени конфигурации» уже покрывает основной сценарий (fallback в F9), явная связь закрывает «крайние» случаи и является единственным механизмом персонального ника/ручной ссылки. Варианты «упростить» (убрать кнопку из меню) и «удалить» (отказ от `UpdateConfigCode` с миграцией настроек) — только по явному решению пользователя, т.к. несут риск регресса F9 и потери кэша/персональных ссылок.

### 3.4 Действия в релизе 0.3.9.301 (код НЕ меняется)

1. Создать черновик [`publish/comment-345-0.3.9.301.md`](publish/comment-345-0.3.9.301.md): анализ (карта использований + зависимость F9 + варианты с последствиями) + вопросы пользователю (п. 9.4 плана 0.3.9.300):
   - Наблюдается ли на практике случай, когда F9 не находит каталог при заполненном «Имени конфигурации» (без явной связи)? Какой именно?
   - Используются ли персональный ник базы / ручная ссылка (`UpdateUrlSegment`/`UpdateUrlOverride`)?
   - Есть ли потребность в пакетной привязке нескольких баз к одной конфигурации помимо текущего механизма?
2. Опубликовать комментарий в #345 (на этапе релиза, вместе с остальными). Issue НЕ закрывать — ждём решения сообща.
3. Если пользователь решит «упростить»/«удалить» — новая версия 0.3.9.302+ с отдельным планом (оценочные файлы: `ConfigUpdateLinkWindow` (WPF+Avalonia), `MainWindow.Tree.cs`/`MainWindow.Avalonia.Tree.cs` пункты меню, `UpdateCheckWindow` (WPF+Avalonia), `MaintenanceCenterViewModel`, кэш, HTML-отчёт, миграция `Infobase.UpdateConfigCode`).

---

## Версии и соответствие issues

| Версия | Issues | Содержание |
|--------|--------|-----------|
| **0.3.9.301** | #334, #330, #323 (+ Platform85 как часть #334) | Починка CAS-входа portal.1c.ru (динамические поля формы, диагностика 401, retry-политика, разделение статусов), понятные сообщения AuthFailed, параметризация каталога платформы + Platform85 |
| **0.3.9.301** | #345 | Только публикация анализа-комментария (код не меняется) |
| 0.3.9.302 (условно) | #334/#330/#323 (остаток) | Вторая итерация CAS по диагностическим логам 0.3.9.301, если 401 сохранится |
| 0.3.9.302+ (условно) | #345 (реализация) | Только после решения пользователя по анализу (упростить/удалить связывание) |

В одном коммите допускается несколько микро-версий (прецедент проекта), но 0.3.9.301 выходит отдельно как релиз A.

---

## Процесс (соответствие правилам проекта)

1. Реализация в code-режиме (разделы 1.4, 2.2) + юнит-тесты (1.5, 2.3); полный прогон `dotnet test` (существующий набор ~1462 теста) и кросс-сборка Linux (`dotnet build -p:BuildLinux=true`) без ошибок.
2. Версия: [`Configuration Management.csproj:62-65`](Configuration%20Management/Configuration%20Management.csproj:62) — `Version`/`AssemblyVersion`/`FileVersion`/`InformationalVersion` = `0.3.9.301`.
3. [`CHANGELOG.md`](CHANGELOG.md): секция `## [0.3.9.301] — 2026-10-04` над `0.3.9.300` (стиль прошлых записей): CAS-вход portal.1c.ru (#334/#330/#323), каталог Platform85, понятная ошибка авторизации, анализ #345 в комментарии.
4. [`README.md`](README.md): бейдж версии `Версия-0.3.9.300` → `Версия-0.3.9.301`.
5. Сборка артефактов:
   - Windows/WPF single-file: `powershell -NoProfile -ExecutionPolicy Bypass -File "Configuration Management\build-windows-single-file.ps1"` → `dist/win-x64/ConfigurationManagement.exe`.
   - Linux/Avalonia single-file: `powershell -NoProfile -ExecutionPolicy Bypass -File "Configuration Management\build-linux-single-file.ps1"` → `dist/linux-x64/ConfigurationManagement`.
   - .deb-пакет: `python publish/build_deb_win_0.3.9.270.py` (версия берётся из csproj) → `package/linux/deb/out/configuration-management_0.3.9.301_amd64.deb`.
   - Проверки: PE/ELF-magic, `FileVersion/ProductVersion = 0.3.9.301`, smoke `--help` (код 0), контроль .deb по образцу `publish/check_deb_win_0.3.9.300.py` (параметризовать версией).
   - Артефакты в `publish/out-0.3.9.301/` + `SHA256SUMS.txt`; `publish/release_body_0.3.9.301.md`; релиз GitHub `v0.3.9.301`.
6. Комментарии в issues (после релиза, issues НЕ закрывать): «Исправлено в версии **0.3.9.301**» в **#334, #330, #323** (черновики `publish/comment-334-0.3.9.301.md`, `comment-330-0.3.9.301.md`, `comment-323-0.3.9.301.md`); анализ в **#345** (`comment-345-0.3.9.301.md`).
7. Живая проверка с пользователем: вход в браузере инкогнито теми же данными (подтверждение гипотезы 2), прогон окон F9/«Обновление платформы»/«Скачивание платформы» с реальной учётной записью ИТС, сбор логов.

---

## Декомпозиция на задачи (для orchestrator/code-режимов)

1. **Задача 1 — реализация CAS-фикса + Platform85 (code):** разделы 1.4.1–1.4.4, 2.2, 1.4.5 (проверка ников) + юнит-тесты 1.5, 2.3. Локализация. Прогон всех тестов.
2. **Задача 2 — версия/документация:** csproj → 0.3.9.301, CHANGELOG.md, README.md, черновики комментариев `comment-334/330/323/345-0.3.9.301.md` в `publish/`.
3. **Задача 3 — сборка и релиз 0.3.9.301:** exe + linux-x64 + deb, проверки, `release_body`, GitHub release, публикация комментариев в #334/#330/#323/#345 (issues не закрывать).
4. **Задача 4 — живая отладка с пользователем:** собрать логи 0.3.9.301 при реальных учётных данных ИТС; при сохранении 401 — составить план итерации (0.3.9.302) на основе новых диагностических данных.
5. **Задача 5 — решение по #345:** после ответа пользователя на вопросы анализа — либо закрыть вопрос «оставить как есть», либо отдельный план реализации (0.3.9.302+).

---

## Сводка затрагиваемых файлов

Код:
- `Configuration Management/Services/OneCUpdatesService.cs` — CAS-вход, динамические поля, диагностика 401, retry, статусы, User-Agent.
- `Configuration Management/Services/OneCPlatformCatalogParser.cs` — ники Platform83/Platform85.
- `Configuration Management/Services/PlatformUpdateService.cs` — параметризация ника, статус AuthFailed.
- `Configuration Management/Services/IPlatformUpdateService.cs` — перегрузка с ником (сигнатуры/комментарии).
- `Configuration Management/ViewModels/PlatformUpdateViewModel.cs`, `PlatformDownloadViewModel.cs` — показ AuthFailed.
- `Configuration Management/Views/PlatformUpdateWindow.xaml.cs`, `.Avalonia.cs`, `PlatformDownloadWindow.xaml.cs`, `.Avalonia.cs`, `UpdateCheckWindow.xaml.cs`, `.Avalonia.cs` — отображение ошибки (без замены существующей кликабельной ссылки).
- `Configuration Management/Localization/Languages/ru.json`, `en.json` — ключи `Updates.AuthFailed`, `PlatformUpdate.Error.AuthFailed`.
- `Configuration Management/Services/BuiltInConfigTypes.cs` — только проверка ников (правка при подтверждённом расхождении).

Тесты:
- Новый `ConfigurationManagement.Tests/OneCUpdatesLoginFlowTests.cs`.
- `ConfigurationManagement.Tests/PlatformUpdateServiceTests.cs`, `PlatformUpdateViewModelTests.cs`, `PlatformDownloadViewModelTests.cs`, `UpdateCheckCatalogTests.cs`, `OneCPlatformCatalogParserTests.cs`, `OneCUpdatesUrlTests.cs`, `BuiltInConfigTypesTests.cs`.

Документация/релиз:
- `Configuration Management/Configuration Management.csproj` (0.3.9.301), `CHANGELOG.md`, `README.md`, `publish/comment-{334,330,323,345}-0.3.9.301.md`, `publish/release_body_0.3.9.301.md`, `publish/out-0.3.9.301/`.

Код для #345 в 0.3.9.301 НЕ меняется (только комментарий). Код для условного 0.3.9.302 — по отдельному плану.

---

## Критерии приёмки

- **#334**: окно «Обновление платформы 1С» получает каталог версий с реальной учётной записью ИТС; при неверном пароле показывает «Вход не подтверждён (401)» вместо NetworkError; каталог Platform85 проверяется (если существует на releases.1c.ru).
- **#330**: окно «Скачивание платформы» получает каталог и позволяет скачать выбранную версию (реальная проверка с пользователем).
- **#323**: окно «Проверка обновлений» (F9) для базы «Бухгалтерия предприятия» (ник Accounting30) получает каталог релизов; «HTTP 302 … requestUri=<тот же>» не появляется при корректных учётных данных.
- **#345**: в issue опубликован анализ и вопросы; код не менялся; issue остаётся открытым.
- Все юнит-тесты (включая новые) зелёные; кросс-сборка Linux без ошибок; артефакты 0.3.9.301 (exe/linux-x64/deb) с контрольными суммами; комментарии «исправлено в версии 0.3.9.301»; issues открыты.

---

## Риски

- **Главный риск — невозможность подтвердить исправление без валидных учётных данных ИТС.** Все изменения кода максимизируют вероятность (динамические поля формы, lt/CSRF, заголовки, HTTP/1.1) и диагностируемость (анонимизированный лог 401), но финальная проверка — только живой сеанс с пользователем. При сохранении 401 план предусматривает итерацию 0.3.9.302 по данным новых логов.
- **Капча/JS-челлендж на портале** могут сделать программный вход невозможным в принципе. Fallback: понятное сообщение + кликабельная ссылка ручной проверки (есть); перенос cookie из браузера — кандидат в отдельную задачу, в скоуп не включаем.
- **Изменение политики retry** (`_portalLoginAttempted` → счётчик) может спровоцировать анти-брутфорс блокировку порталом при слишком частых попытках: лимит 2–3 попытки на сессию + сброс только при смене учётной записи.
- **Параметризация каталога платформы** может изменить поведение существующих окон (Platform83 остаётся по умолчанию; Platform85 — дополнительно, без изменения дефолтного пути). Механику «проверять оба каталога» согласовать на этапе реализации, чтобы не дать пользователю неоднозначный результат.
- **Двуплатформенность:** логика в common-сервисах; окна — обе платформы (WPF и Avalonia), правки симметричны.
- **#345:** публикация анализа без кода — ожидаемо по правилу «решение примем сообща»; риск — затягивание решения; в комментарии явно перечислены вопросы, чтобы пользователь мог ответить коротко.
- **Регресс кликабельной ссылки / существующих сообщений авторизации** в окнах F9/платформы — не трогаем то, что работает (0.3.9.287), только добавляем различение AuthFailed.