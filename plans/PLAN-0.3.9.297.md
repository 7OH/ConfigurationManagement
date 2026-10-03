# PLAN 0.3.9.297 — исправление 6 открытых issues (#334, #323, #330, #335, #333, #321)

Дата: 2026-10-03. Текущая версия: **0.3.9.296** (коммит 2d28b11). Следующая микро-версия: **0.3.9.297**.
Двуплатформенный проект: Windows/WPF (`#if WINDOWS`, `*.xaml`/`*.xaml.cs`) и Linux/Avalonia (`#if LINUX`, `*.Avalonia.cs`). Common-логика (сервисы, модели, ViewModel) — общая.

**Ограничения реализации:** только код + юнит-тесты; issues не закрывать, ничего не коммитить. Реализация выполняется другой задачей в code-режиме строго по этому плану.

---

## 0. Общий приоритетный блок: редирект 302 / авторизация на сайте 1С

Один корень у трёх issues (#334 «Автообновление платформы», #323 «Проверка обновлений», #330 «Скачивание версии платформы»). Все три окна ходят на `releases.1c.ru` через **один и тот же** singleton `IOneCUpdatesService` (`PlatformUpdateService` → `IOneCUpdatesService.GetPageTextAsync`, `UpdateCheckWindow`/`ActualReleasesWindow` → `CheckForUpdatesAsync`, `PlatformDownloadViewModel`/`PlatformUpdateViewModel` → `IPlatformUpdateService`). Поэтому фикс делается **один раз** в `OneCUpdatesService` + `AppServices`, без дублирования в трёх местах.

Диагноз по коду (подтверждён чтением 0.3.9.296):

1. **Креды из справочника ИТС (#333) не доходят до `GetCredentials()`.** `AppServices` регистрирует `AddSingleton<IOneCUpdatesService, OneCUpdatesService>()`, а единственный публичный конструктор — `OneCUpdatesService(IInfobaseRepository, IAppLogger)`, который вызывает `this(repository, logger, handler: null, itsAccounts: null)`. Итог: `_itsAccounts == null` всегда, и `GetCredentials()` читает только устаревшие `AppSettings.UpdatesLogin/UpdatesPassword`. Если пользователь ввёл креды в справочнике учёток ИТС (#333), а старые поля настроек пусты — лог «[Updates] Для входа на portal.1c.ru не задан логин» (точный текст в #334) и вход невозможен.
2. **CAS-цепочка входа не доводится до конца.** `TryLoginPortalAsync` выполняет GET формы и POST логина, но при 302 после POST (Location вида `https://releases.1c.ru/public/security_check?ticket=ST-…`) только проверяет, что Location не содержит «login», и возвращает true — **без GET по этому Location**. Выпуск сессионной cookie (TGC/JSESSIONID) может происходить именно при обращении к `security_check?ticket=…`; без него повторный запрос каталога снова получает 302 на login.1c.ru. Также GET формы выполняется без параметра `service=<исходный URL>` из первого 302 — CAS может требовать его для корректного redirect'а после входа.

### A1. Внедрить `IItsAccountsStore` в `OneCUpdatesService` (корень «не задан логин»)

Файлы:
- `Configuration Management/Services/OneCUpdatesService.cs`
- `Configuration Management/AppServices.cs` (комментарий, код не требуется — регистрация уже есть)

Шаги:
1. Добавить публичный перегруженный конструктор:
   `public OneCUpdatesService(IInfobaseRepository repository, IAppLogger logger, IItsAccountsStore itsAccounts) : this(repository, logger, handler: null, itsAccounts: itsAccounts)`.
2. Оставить существующий `public OneCUpdatesService(IInfobaseRepository, IAppLogger)` как перегрузку (`: this(repository, logger, itsAccounts: null)`) — его используют прямые создания в тестах/старых местах.
3. `Microsoft.Extensions.DependencyInjection` выберет публичный конструктор с максимальным числом разрешимых параметров → `(repository, logger, itsAccounts)`. `IItsAccountsStore` уже зарегистрирован в `AppServices` (строка 45) — инжекция сработает.
4. Обновить XML-документацию конструкторов: креды берутся из справочника (#333), устаревшие поля настроек — только фолбэк.
5. Проверить все места прямого создания `OneCUpdatesService` (тесты: `PlatformDownloadTests`, `UpdateCheckCatalogTests` и др.) — они используют internal-конструктор с `itsAccounts`, сигнатуры не менять.

Критерий: при наличии записи в `its_accounts.json` (даже при пустых `UpdatesLogin/UpdatesPassword`) `GetCredentials()` возвращает её логин/пароль.

### A2. Довести до конца программный вход на portal.1c.ru (CAS)

Файлы:
- `Configuration Management/Services/OneCUpdatesService.cs` (методы `TryLoginPortalAsync`, `ExtractFormExecution`, вспомогательный `FollowRedirectChainAsync`)

Шаги:
1. В `SendWithAuthAsync` при редиректе на `login.1c.ru` передавать в `TryLoginPortalAsync` **полный URL редиректа** (с query `?service=https://releases.1c.ru/public/security_check`): `TryLoginPortalAsync(string? loginUrl = null, CancellationToken ct = default)`.
2. В `TryLoginPortalAsync`:
   - GET формы входа по переданному URL (или `PortalLoginUrl`, если URL пуст). Логировать `[Updates] Запрашиваю форму входа: <url>`.
   - Извлечь `execution`. Расширить regex `ExtractFormExecution` на одинарные кавычки: `name=["']execution["'][^>]*value=["'](?<value>[^"']*)["']` → охватить `value='…'` и порядок атрибутов (например, искать `name="execution"` и затем значение любым способом: `<input[^>]*name=["']execution["'][^>]*>` и отдельно вытащить `value`).
   - POST формы (поля остаются прежними: username/password/execution/_eventId/rememberMe/anotherComputer/geolocation/inviteCode/inviteType).
   - **Новый шаг:** если POST вернул 3xx — следовать редиректам вручную (до `MaxRedirects`), выполняя GET по Location через общий `_httpClient` (cookie накапливаются в `_cookieContainer`). Останавливаемся на 200 либо когда Location покидает `login.1c.ru` (после обязательного GET этого Location). Каждый шаг логировать: `[Updates] Редирект входа {status} (шаг N): '<Location>'`.
   - Успех: цепочка завершилась 200-страницей вне страницы входа, либо редирект на хост ≠ login.1c.ru выполнен. Логировать `[Updates] Вход на portal.1c.ru выполнен (шагов: N)`.
   - Неудача: POST вернул форму входа снова (200/302 на login.1c.ru) — `[Updates] Вход на portal.1c.ru не подтверждён (status=…, location='…')`; вернуть false.
3. Защита от зацикливания: общий лимит шагов `MaxRedirects`; при превышении — false.
4. `_portalLoginAttempted` остаётся «один раз за сессию службы» (требование из комментария: вход выполняется один раз). Если после успешного входа повтор исходного запроса снова дал 302 — НЕ входить повторно, а вернуть response как есть (дальнейшая обработка `CheckForUpdatesAsync`/`PlatformUpdateService` отдаст AuthRequired с понятным текстом).
5. **Диагностика (единая):**
   - Добавить helper `ResolveAccountName()`: имя записи из `IItsAccountsStore` (через `Resolve(settings.ItsAccountId)`), иначе «Основная» (если логин непуст), иначе «не задана». Логировать в `TryLoginPortalAsync` перед входом: `[Updates] Вход на portal.1c.ru: учётная запись '<имя>'` (без пароля).
   - В `SendWithAuthAsync` при `needsLogin && !_portalLoginAttempted` логировать причину входа (401/403 или 302 на login.1c.ru).

### A3. Тесты (общий блок 302/авторизации)

Файлы:
- `ConfigurationManagement.Tests/UpdateCheckCatalogTests.cs` (основное место: fake-handler сценариев 302/CAS)
- `ConfigurationManagement.Tests/OneCUpdatesUrlTests.cs` (при необходимости — чистые хелперы)
- `ConfigurationManagement.Tests/PlatformDownloadViewModelTests.cs` (интеграция: каталог после входа)

Новые тесты:
1. `GetCredentials_UsesItsAccountsStore_WhenStoreProvided` — store с основной записью; `OneCUpdatesService` (internal ctor с store) → при запросе используется логин из store даже при пустых `UpdatesLogin`.
2. `GetCredentials_FallsBackToLegacySettings_WhenStoreEmpty` — store пуст/нет резолва → старые поля настроек.
3. `CheckForUpdatesAsync_302ToLoginHost_CredentialsFromItsAccountsStore_Succeeds` — сценарий: GET `/project/AccountingCorp30` → 302 `Location: https://login.1c.ru/login?service=https://releases.1c.ru/public/security_check` → форма с `execution` → POST → 302 `Location: https://releases.1c.ru/public/security_check?ticket=ST-x` → GET security_check → 200 → повтор исходного запроса → 200 с `#versionsTable`. Store передаётся через internal-конструктор. Проверить, что POST ушёл с логином из store (fake-handler сверяет `form` поля `username`).
4. `TryLogin_FollowsPostRedirectToSecurityCheck_ThenCatalogSucceeds` — явно проверяет, что после POST выполнен GET по Location (счётчик запросов fake-handler: login form GET=1, POST=1, security_check GET=1, повтор каталога=1).
5. `CheckForUpdatesAsync_NoCredentials_ReturnsAuthRequired` — store без записей + пустые старые настройки → 302 → «не задан логин» → `Updates.AuthRequired` (расширение существующего `CheckForUpdatesAsync_302WithoutLocation_ReturnsAuthRequired`).
6. `PlatformDownloadViewModel.LoadCatalogAsync_AfterLogin_PopulatesReleases` — через `PlatformUpdateService` с fake-провайдером текста, имитирующим вход и выдачу HTML с версиями → `Releases.Count > 0` (существующая структура `CreateService`/`CreateVm`).

Существующие тесты (не ломать): `CheckForUpdatesAsync_302WithoutLocation_LoginThenRetry_Succeeds`, `CheckForUpdatesAsync_302WithoutLocation_ReturnsAuthRequired`, `CheckForUpdatesAsync_302ToLoginHost_ReturnsAuthRequired`.

### A4. Риски / регрессии блока A

- Изменение сигнатур конструкторов может затронуть тесты, создающие `OneCUpdatesService` напрямую — пересобрать проект, поправить вызовы при необходимости (сигнатуры сохраняем, только добавляем перегрузку).
- Реальный CAS-флоу 1С может отличаться (поля формы, cookie). Код должен быть отказоустойчивым: при неизвестной форме/отсутствии `execution` — понятная ошибка, никаких исключений наружу (`TryLoginPortalAsync` уже ловит `Exception`).
- `HttpClientHandler` с `AllowAutoRedirect=false` и общим `CookieContainer` не меняется — ручное следование редиректам сохраняет `Authorization` и cookie.
- Повторный вход после смены учётной записи в настройках в рамках одной сессии не выполняется (флаг `_portalLoginAttempted`) — оставить как есть, это осознанное ограничение (вход один раз; перезапуск приложения сбрасывает).

---

## 1. Issue #335 «Диагностика подключения» (3 дефекта)

Окно: `NetworkDiagnosticsWindow.xaml` / `.Avalonia.cs`, VM: `NetworkDiagnosticsViewModel.cs`, сервис: `NetworkDiagnosticsService.cs`, диалог: `PortsEditWindow`.

### B1. Поле «СерверЫ» → «Сервер» + список выбора у поля «Сервер»

Причина: подпись `Diagnostics.ServerLabel` переведена как «Серверы:» (`ru.json` строка 1823); пользователь ждёт единственного числа и список у самого поля сервера. Сейчас: отдельное текстовое поле Host + отдельный ComboBox «Серверы:».

Файлы:
- `Configuration Management/Localization/Languages/ru.json`, `en.json` — `Diagnostics.ServerLabel`: «Сервер:» / «Server:».
- `Configuration Management/Views/NetworkDiagnosticsWindow.xaml` — объединить TextBox `Host` и ComboBox в одно поле:
  - Удалить `<TextBox ... Text="{Binding Host, UpdateSourceTrigger=PropertyChanged}"/>`.
  - `<ComboBox IsEditable="True" IsTextSearchEnabled="False" ItemsSource="{Binding AvailableServers}" SelectedItem="{Binding SelectedServer, Mode=TwoWay}" Text="{Binding Host, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}"/>` (ширина ~200 как у прежнего поля). Убедиться, что стиль `ModernComboBox` (шаблон с `PART_EditableTextBox`) корректно работает в editable-режиме (в шаблоне уже есть `PART_EditableTextBox`).
- `Configuration Management/Views/NetworkDiagnosticsWindow.Avalonia.cs` — `hostBox` заменить на editable `ComboBox`: `IsEditable = true`, `ItemsSource` → `AvailableServers`, `SelectedItem` → `SelectedServer`, `Text` → `Host` (TwoWay).
- VM не меняется: `SelectedServer` → `Host` уже синхронизирован в setter'е (строки 114–129). Проверить обратное направление (ввод в поле → Host обновляется через binding Text→Host; SelectedServer не обязателен).

Критерий: в окне одна подпись «Сервер:», поле редактируемое со списком известных серверов; выбор из списка подставляет адрес и сохранённый порт.

Риск: WPF editable ComboBox может «съедать» Text при изменении ItemsSource — проверить вручную; при проблемах — оставить два поля, но переименовать подпись (минимальный вариант).

### B2. Догадка портов со сдвигом +4 — `ServerPortsGuesser`

Причина (точно воспроизведена): `Guess(int clusterPort)` считает сдвиг **только** от порта кластера 1541. Пользователь вводит порт другого сервиса (например 27545, похожий на RAS) и получает `offset = 27545-1541 = 26004` → `{27544, 27545, 27546, 27549}` вместо ожидаемых `{27540, 27541, 27542, 27545}`. Ожидание пользователя: введённый порт интерпретируется как порт **ближайшего** стандартного сервиса, вся карта сдвигается на одно смещение.

Файлы:
- `Configuration Management/Services/ServerPortsGuesser.cs`

Шаги:
1. В `Guess` заменить фиксированную базу `OneCPorts.Cluster` на **ближайший** стандартный порт:
   - Вычислить среди `{Agent=1540, Cluster=1541, Repository=1542, Ras=1545}` порт с минимальной дистанцией `|clusterPort - std|` (при равенстве — меньший индекс, детерминированно; для стандартных портов дистанция 0).
   - `offset = clusterPort - nearestStd`; карта = `{Agent+offset, Cluster+offset, Repository+offset, Ras+offset}` (введённый порт остаётся в карте как значение nearest-сервиса).
   - Валидация диапазона [1..65535] как сейчас; иначе `Default`.
2. Обновить XML-документацию и примеры (2541 → 2540/2541/2542/2545; 27545 → 27540/27541/27542/27545).

Файл тестов: `ConfigurationManagement.Tests/ServerPortsGuesserTests.cs`
Новые тесты:
- `Guess_RasLikePort27545_ShiftsAllServices` → 27540/27541/27542/27545 (ровно сценарий пользователя).
- `Guess_AgentLikePort27540_SameMap` → та же карта.
- `Guess_RepositoryLikePort27542_SameMap` → 27540/27541/27542/27545.
- Существующие тесты (`2541`, стандартные, недопустимые, верхняя граница 65535) — должны продолжать проходить; проверить 65535: ближайший 1545 → offset 63990 → repository=1542+63990=65532, ras=65535 — в диапазоне → карта сместится (поведение изменится относительно старого теста `Guess_PortNearUpperBound_FallsBackToDefaults`). **Скорректировать этот тест**: 65535 теперь даёт сдвинутую карту (это корректно и ожидаемо — порт RAS в диапазоне). Если хотим сохранить «фолбэк» — уточнить правило: фолбэк только когда хотя бы один смещённый порт вне диапазона; для 65535 все в диапазоне → сдвиг. Обновить тест соответственно.

### B3. Колонка «Сервис» показывает «Порт» вместо названия

Причина: `OneCPorts.GetServiceKey(port)` возвращает `Diagnostics.PortCustom` («порт») для любого нестандартного порта. После догадки пользователь получает нестандартные порты 2754x — во всех строках «Сервис» = «порт». Дополнительно `NetworkDiagnosticsHints` использует тот же `ServiceKey` в подсказках («Порт {0} ({1}) закрыт»).

Файлы:
- `Configuration Management/Models/NetworkDiagnosticsResult.cs` (`OneCPorts.GetServiceKey`) — опционально улучшить для сдвинутой карты
- `Configuration Management/ViewModels/NetworkDiagnosticsViewModel.cs` — передавать явные ключи сервисов для наборов из карты
- `Configuration Management/Services/NetworkDiagnosticsService.cs` — приём пар (порт, ключ)

Шаги:
1. Расширить `RunCoreAsync` до приёма пар `(int Port, string ServiceKey)`; `CheckPortsAsync` и `ApplyEditedPortsAndCheckAsync` передают явные ключи (`Diagnostics.PortAgent/PortCluster/PortRepository/PortRas`) — позиции известны из карты.
2. `NetworkDiagnosticsService.RunAsync` — если передан ключ, использовать его; иначе `OneCPorts.GetServiceKey(port)` (одиночная проверка поля).
3. Опционально: улучшить `OneCPorts.GetServiceKey`: если порт лежит в «сдвинутой карте» (т.е. `port - std` одинаков для одного из четырёх стандартных портов с общим сдвигом δ — эвристика по ближайшей дельте), возвращать имя соответствующего сервиса. Не обязательно для основного сценария, но улучшает одиночные проверки.

Тесты:
- `ConfigurationManagement.Tests/NetworkDiagnosticsViewModelTests.cs` (если есть) — `ApplyEditedPortsAndCheckAsync` строит строки с именами сервисов (агент/кластер/хранилище/RAS) для нестандартных портов.
- `ConfigurationManagement.Tests/NetworkDiagnosticsHintsTests.cs` — подсказки используют переданный ключ (порт 27540 → имя «агент сервера»).

Риск: изменение сигнатуры `RunCoreAsync` затрагивает только VM (внутренний вызов) — внешних потребителей нет.

---

## 2. Issue #333 «Учетки доступа к ИТС» — пункт 3 (вид учётки в списках)

Контекст кода: в 0.3.9.296 `DisplayMemberPath`/`DisplayMemberBinding` уже заданы во всех четырёх местах:
- WPF `SettingsWindow.xaml:1477-1480` — `ItsAccountsCombo DisplayMemberPath="Name"`;
- WPF `ConfigTypeEditWindow.xaml:118-120` — `AccountCombo DisplayMemberPath="Name"`;
- Avalonia `SettingsWindow.Avalonia.cs:359-364` — `DisplayMemberBinding = Binding(nameof(ItsAccountSelectionItem.Name))`;
- Avalonia `ConfigTypeEditWindow.Avalonia.cs:38` — то же.

Жалоба пользователя после 0.3.9.286 не воспроизводится прямым чтением привязок. Вероятные причины: (а) пользователь тестировал сборку до фикса; (б) WPF `ModernComboBox` с кастомным ControlTemplate в каких-то кейсах отображает `ToString()`; (в) есть ещё одно место (окно «Скачивание платформы» показывает `account.ToString()`, но это отдельная строка статуса, а не ComboBox).

План (верификация + защита от регрессии):

Файлы:
- `Configuration Management/ViewModels/ItsAccountsViewModel.cs` — `ItsAccountSelectionItem`
- `Configuration Management/Views/SettingsWindow.xaml` / `ConfigTypeEditWindow.xaml` — проверить/при необходимости усилить
- `ConfigurationManagement.Tests/SettingsWindowXamlResourcesTests.cs`

Шаги:
1. **Защитное `ToString()`**: добавить в `ItsAccountSelectionItem` `public override string ToString() => Name;` — даже если где-то пропущен `DisplayMemberPath`, будет видно имя, а не тип/ключ.
2. **Тест-страховка привязок XAML/кода** (в `SettingsWindowXamlResourcesTests.cs` или новом файле):
   - WPF: в `SettingsWindow.xaml` существует ComboBox с `x:Name="ItsAccountsCombo"` и атрибутом `DisplayMemberPath="Name"`; в `ConfigTypeEditWindow.xaml` — `AccountCombo` с `DisplayMemberPath="Name"`.
   - Avalonia: в `SettingsWindow.Avalonia.cs` и `ConfigTypeEditWindow.Avalonia.cs` есть `DisplayMemberBinding`/`DisplayMemberPath` на `nameof(ItsAccountSelectionItem.Name)`.
   (Проверить текущий стиль теста `SettingsWindowXamlResourcesTests` — вероятно, читает файлы как текст; действовать по образцу.)
3. **Воспроизведение в 0.3.9.296**: запустить сборку, открыть «Общие настройки» и «Изменить типовую конфигурацию» (обе платформы, если возможно). Если имя отображается корректно — в ответе пользователю указать, что пункт был исправлен ранее (фиксы 0.3.9.272/286) и предложить проверить 0.3.9.297; тесты остаются как страховка.
4. **Проверка маппинга выбора → `AccountId`**: `OnSaveClick` (обе платформы) уже берёт `selectedAccount?.Id`; для виртуального пункта «Основная» `Id == null` → пустая строка → резолв «Основная». Не менять. Покрыть тестом (см. п. 3): выбор по имени мапится в правильный `Id`.

Тесты:
- `ConfigurationManagement.Tests/ItsAccountsViewModelTests.cs` — `ItsAccountSelectionItem.ToString()` == Name (включая плейсхолдер «Без имени» для пустого Name).
- `SettingsWindowXamlResourcesTests` — страховка привязок (п. 2).

Критерий: в настройках и редакторе типовой конфигурации всегда видно имя учётки (или «Основная»), а не идентификатор/тип; выбор сохраняется в `AccountId`.

Риск: правки минимальны (ToString + тесты), регрессий не ожидается. Если при воспроизведении баг подтвердится (например, стиль `ModernComboBox` игнорирует DisplayMemberPath) — добавить `ItemTemplate`/`DataTemplate` с `Binding Name` в конкретные ComboBox (обе платформы), как чинящее решение.

---

## 3. Issue #321 «Окно Типовые конфигурации» (4 пункта)

Окна: `ConfigTypesEditWindow` (список) и `ConfigTypeEditWindow` (редактор), модели `OneCConfigType`/`OneCConfigEdition`, данные `BuiltInConfigTypes`, хранилище `CustomConfigTypesStore`.

### D1. Удалить «Сегмент адреса» (UrlCode) из UI редактора

Контекст: адрес обновлений строится **только** из `Nick` (`OneCUpdatesService.BuildUpdateUrl` → `BuildNickUrl(config.Nick)`). Поле `UrlCode` в редакторе бесполезно для текущей логики; пользователь требует удалить («в логике определения не требуется»). Поле `ConfigName` («Имя конфигурации») остаётся — оно используется для сопоставления данных о конфигурации.

Файлы:
- `Configuration Management/Views/ConfigTypeEditWindow.xaml` — удалить блок «Сегмент адреса» (TextBlock `Updates.UrlCode` + `UrlCodeBox`, строки 105–110) и связанный ToolTip.
- `Configuration Management/Views/ConfigTypeEditWindow.xaml.cs` — удалить `UrlCodeBox.Text = …` и `UrlCode = UrlCodeBox.Text…`.
- `Configuration Management/Views/ConfigTypeEditWindow.Avalonia.cs` — удалить поле `_urlCodeBox`, строку `MakeFieldRow(T("Updates.UrlCode"), _urlCodeBox)`, присвоение `UrlCode`.
- Модель `OneCConfigType.UrlCode` — **не удалять** (обратная совместимость JSON-файлов); в комментарии пометить как deprecated/неиспользуемый для новых записей. `ConfigTypeItemViewModel.UrlCode` — оставить (используется в `ConfigLinkItemViewModel` и «Актуальных релизах»; в списке типовых колонки UrlCode нет).
- Локализация `Updates.UrlCode`/`Updates.UrlCodeHint` — оставить (могут использоваться в `ConfigUpdateLinkWindow`), при желании пометить.

Тесты: `BuiltInConfigTypesTests.ConfigName_DoesNotAffectEffectiveUrlCode` — не ломается (модель не тронута). Добавить/поддержать: редактор больше не пишет `UrlCode` в результат (проверяется рефлексией нет смысла — просто отсутствие UI; тест не требуется).

### D2. «Восстановить типовые» → пустые НИК и табличные части

Причины в коде:
- `BuiltInConfigTypes` заполняет `Nick` только у БП (`AccountingCorp30`); у ЗУП, УТ, КА, ERP, Розницы, БГУ `Nick = string.Empty` (строки 47, 63, 80, 99, 115, 132 `BuiltInConfigTypes.cs`).
- После `RestoreDefaults` (удаляет только `OverridesBuiltIn`) показывается именно этот набор → колонка «НИК» пустая у большинства строк. «Табличные части» (`EditionsSummary`) у BuiltIn заполнены, но если у пользователя были созданы переопределения с пустыми редакциями и он их правил — результат мог «схлопнуться»; отдельно — пункт D3 (отображение одной колонки).

Файлы:
- `Configuration Management/Services/BuiltInConfigTypes.cs`

Шаги:
1. Сверить ники с данными пользователя (комментарии issue #321: `publish/issue_321*.json`) и реальными каталогами `releases.1c.ru/project/<nick>` (проверить 200 и наличие `#versionsTable`).
2. Заполнить `Nick` для основных конфигураций (ориентир из анализа: БП → `AccountingCorp30` (3.0) и `Accounting` (2.0); ЗУП → `HRM30` (3.1); для остальных — подтвердить и заполнить; если ник для конкретной редакции отличается от ника конфигурации — задать его в редакции полем `UrlOverride = "https://releases.1c.ru/project/<nick>"`).
3. Дополнить редакции, если пользователь указывал новые (проверить против списка 7OH в issue #321; часть уже покрыта тестом `Editions_IncludeUserList`).

Тесты:
- `ConfigurationManagement.Tests/BuiltInConfigTypesTests.cs`:
  - `AllBuiltInConfigs_HaveEditions` (непустой `Editions` у всех).
  - `BuiltInConfigs_WithKnownNicks_HaveNonEmptyNick` (БП/ЗУП и др. по фактически подтверждённым никам; не вводить ложные — если ник неизвестен, тест не требует).
  - `RestoreDefaults_ReturnsBuiltInWithNicksAndEditions` (в `CustomConfigTypesStoreTests` или `BuiltInConfigTypesTests`): создать переопределение с пустыми полями → `RestoreDefaults()` → `LoadAll()` содержит BuiltIn-записи с непустыми `Nick`/`Editions`.

Критерий: после «Восстановить типовые» в колонке «НИК» и «Редакции» у поставляемых конфигураций видны данные.

### D3. Табличная часть редакций: 4 колонки, потеря строк

Причина: в `ConfigTypeEditWindow` редакции показываются `ListBox` с `DisplayMemberPath="Name"` (WPF, XAML строка 137–140) / `ComboBox`-стилем списка (Avalonia, `_editionsList` без шаблона) → видна только первая колонка (имя). Баг «новая строка пустая после сохранения» в WPF: при добавлении `OnAddEditionClick` сначала выбирает новую строку (SelectionChanged → `CommitEditionFields` пишет старые значения), затем очищает поля `Text=""` → `TextChanged` → `CommitEditionFields` перетирает поля новой строки пустыми (в Avalonia защита `_editionSyncing` есть, в WPF — нет).

Решение (совмещает пункт D3 и пункт D4): перевести редакции на **таблицу из 4 колонок** + **модальный диалог** добавления/правки.

Новый файл (обе платформы):
- `Configuration Management/Views/EditionEditWindow.xaml` / `EditionEditWindow.xaml.cs` (WPF, `#if WINDOWS`)
- `Configuration Management/Views/EditionEditWindow.Avalonia.cs` (Avalonia, `#if LINUX`)
  - Поля: Имя (`Updates.Name`), Ред (`Updates.EditionRed`), Подред (`Updates.EditionSubRed`), URL переопределения (`Updates.UrlOverride`). Результат `OneCConfigEdition? Result`; валидация: имя или ред непусто (иначе предупреждение). Фокус на первом поле; размер окна через `WindowSizeMath` по образцу других редакторов.

Изменения:
- WPF `ConfigTypeEditWindow.xaml`: заменить `ListBox`+4 `TextBox`+кнопки «+/−» на `DataGrid` с 4 колонками (`Name`, `Red`, `SubRed`, `UrlOverride`; `IsReadOnly="True"`, `SelectionMode="Single"`, `HeadersVisibility="Column"`) + кнопки «Добавить…», «Изменить…», «Удалить» (стиль `OutlineButtonStyle`).
- WPF `ConfigTypeEditWindow.xaml.cs`: удалить инлайн-поля и самокоммиты (`EditionNameBox`/`RedBox`/`SubRedBox`/`UrlOverrideBox`, `CommitEditionFields`, `LoadEditionFields`, `OnEditionSelectionChanged`); кнопки: «Добавить…» → `new EditionEditWindow(null)` → `_editions.Add(result)`; «Изменить…» → `new EditionEditWindow(copy)` → заменить элемент в `_editions`; «Удалить» → `_editions.Remove(selected)`. `EditionsList` заменить на `EditionsGrid`.
- Avalonia `ConfigTypeEditWindow.Avalonia.cs`: аналогично — `_editionsList` заменить на Grid-таблицу (заголовок + строки, как `ConfigTypesEditWindow.Avalonia.cs BuildRow`): колонки Имя/Ред/Подред/URL; клик/выбор строки; кнопки «Добавить…»/«Изменить…»/«Удалить»; открытие `EditionEditWindow`.
- Avalonia `EditionEditWindow` — по образцу `ItsAccountEditWindow.Avalonia.cs` (поля + OK/Отмена, `ShowSync`).

Тесты:
- `ConfigurationManagement.Tests/CustomConfigTypesStoreTests.cs`: убедиться, что сохранение нескольких редакций с 4 полями переживает сериализацию/десериализацию (если теста `SaveLoad_MultipleEditions_ArePreserved` нет — добавить: 2+ редакции с `Red`/`SubRed`/`UrlOverride` → `Save` → `Load` → все поля на месте, порядок сохранён).
- Отображение: редакции с пустым `Name` показывают `Red` (`OneCConfigEdition.ToString()` уже так делает — покрыть тестом, если нет).

Критерий: в редакторе видна таблица редакций из 4 колонок; добавление — модальный диалог; после сохранения и повторного открытия строки и их поля на месте.

### D4. Инлайн-кнопка «Добавить» → модальный диалог

Выполняется в составе D3 (кнопка «Добавить…» открывает `EditionEditWindow`). Дополнительно:
- Убрать из UI все инлайн-поля редакции (они и были предметом жалобы «поля под таблицей не интуитивны»).
- Кнопку «Добавить» переименовать в «Добавить…» (локализация `Updates.Add` — проверить, отдельный ключ `Updates.AddEdition` не обязателен; можно оставить «Добавить»).

---

## 4. Порядок реализации (рекомендуемый)

1. **Блок A (общий)**: A1 (DI-инъекция store) → A2 (CAS-цепочка) → A3 (тесты). Прогнать `UpdateCheckCatalogTests`, `PlatformDownloadTests`, `PlatformUpdateServiceTests`.
2. **#335**: B2 (Guesser + тесты) → B3 (имена сервисов) → B1 (поле «Сервер»/«Серверы»).
3. **#333**: ToString + тесты-страховки.
4. **#321**: D3+D4 (модальный диалог редакции + таблица) → D1 (удалить UrlCode из UI) → D2 (BuiltIn-ники) → тесты.
5. Полный прогон `ConfigurationManagement.Tests`; сборка WPF и Avalonia (`#if WINDOWS` / `#if LINUX`); ручная проверка обоих окон на каждой платформе.

## 5. Сводка затрагиваемых файлов

Сервисы/модели (common):
- `Configuration Management/Services/OneCUpdatesService.cs` (A1, A2)
- `Configuration Management/Services/ServerPortsGuesser.cs` (B2)
- `Configuration Management/Services/NetworkDiagnosticsService.cs` (B3, приём ключей)
- `Configuration Management/Models/NetworkDiagnosticsResult.cs` (B3, опционально GetServiceKey)
- `Configuration Management/Models/OneCConfigType.cs` (D1, deprecated-комментарий)
- `Configuration Management/Services/BuiltInConfigTypes.cs` (D2)
- `Configuration Management/ViewModels/ItsAccountsViewModel.cs` (C: ToString)
- `Configuration Management/ViewModels/NetworkDiagnosticsViewModel.cs` (B3)
- `Configuration Management/Localization/Languages/ru.json`, `en.json` (B1: «Сервер:»)

Views WPF (`.xaml` + `.xaml.cs`):
- `NetworkDiagnosticsWindow` (B1)
- `ConfigTypeEditWindow` (D1, D3, D4)
- `EditionEditWindow` — новый (D3)

Views Avalonia (`*.Avalonia.cs`):
- `NetworkDiagnosticsWindow.Avalonia.cs` (B1)
- `ConfigTypeEditWindow.Avalonia.cs` (D1, D3, D4)
- `EditionEditWindow.Avalonia.cs` — новый (D3)

Тесты:
- `UpdateCheckCatalogTests.cs` (A3)
- `ServerPortsGuesserTests.cs` (B2)
- `NetworkDiagnosticsViewModelTests.cs` / `NetworkDiagnosticsHintsTests.cs` (B3)
- `ItsAccountsViewModelTests.cs` (C)
- `SettingsWindowXamlResourcesTests.cs` (C)
- `BuiltInConfigTypesTests.cs` / `CustomConfigTypesStoreTests.cs` (D2, D3)
- `PlatformDownloadViewModelTests.cs` (A3, при необходимости)

## 6. Критерии приёмки (для всех issues)

- #334/#323/#330: при настроенной учётке ИТС в справочнике (и пустых старых полях) проверка обновлений, каталог платформы и скачивание работают; в журнале видны шаги входа (`[Updates] Редирект входа …`, `Вход на portal.1c.ru выполнен`) и имя учётки без пароля.
- #335: одна подпись «Сервер:» со списком; догадка 27545 → 27540/27541/27542/27545; колонка «Сервис» показывает имена сервисов, а не «порт».
- #333: во всех ComboBox учёток видно имя/«Основная», не ключ; выбор сохраняется в `AccountId`.
- #321: нет поля «Сегмент адреса»; после «Восстановить типовые» видны ники и редакции; редакции отображаются таблицей из 4 колонок и сохраняются; добавление — модальный диалог.

## 7. Общие риски

- Реальная сеть 1С недоступна в юнит-тестах — CAS-флоу проверяется только fake-handler'ом; после релиза нужна ручная проверка пользователем.
- Двуплатформенность: каждый UI-фикс дублируется в WPF и Avalonia — следить за `#if WINDOWS`/`#if LINUX` и симметрией файлов.
- Сериализация JSON: поля моделей не удаляются (обратная совместимость), только скрываются из UI.
- Micro-версия инкрементируется в CHANGELOG/manifest на этапе сборки выпуска (вне этой задачи).