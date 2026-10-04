# PLAN 0.3.9.300 — исправление открытых issues #305, #309, #321, #324, #340, #342 (релиз A) + #323/#330/#334/#345 (план для релиза B)

Дата: 2026-10-03. Текущая версия: **0.3.9.299**. Следующая микро-версия: **0.3.9.300**.
Источник кандидатов: [`publish/issues_candidates.md`](publish/issues_candidates.md) (Задача 1, 10 открытых issues).
Двуплатформенный проект: Windows/WPF (`#if WINDOWS`, `*.xaml`/`*.xaml.cs`) и Linux/Avalonia (`#if LINUX`, `*.Avalonia.cs`). Common-логика (сервисы, модели, ViewModel) — общая.

**Ограничения реализации:** только код + юнит-тесты; issues не закрывать, ничего не коммитить. Реализация выполняется другой задачей в code-режиме строго по этому плану.

**Политика версий (объём релиза):**
- **Релиз A — 0.3.9.300:** 6 исправлений UI/парсинга, проверяемых юнит-тестами и ручными сценариями БЕЗ доступа к порталу 1С: **#342, #340, #309, #321, #305, #324**. Плюс **#345** — анализ функционала связывания (без изменения кода, готовится материал для решения сообща).
- **Релиз B — 0.3.9.301:** единая группа **#334/#330/#323** — CAS-авторизация на portal.1c.ru (требует живой отладки с реальными учётными данными ИТС, поэтому выносится из релиза A). В этот же релиз — доработка каталога `Platform85` (#334).

Обоснование разделения: все правки релиза A локализованы и не зависят от сети; группа CAS зависит от поведения внешнего сервера 1С (форма входа, статус 401) и требует итеративного исследования с пользователем. Один релиз из всех 10 пунктов нереалистичен — сетевые правки блокируют остальные без возможности выпуска промежуточного результата.

---

## Группа 1. CAS-авторизация портала 1С: #334, #330, #323 (релиз B, версия 0.3.9.301)

### 1.1 Общий корень (единый фикс `OneCUpdatesService`)

Все три окна страдают одинаково: запрос `releases.1c.ru/...` при отсутствии сессии получает **302 на `login.1c.ru`**, запускается программный вход ([`TryLoginPortalAsync`](Configuration%20Management/Services/OneCUpdatesService.cs:1022)), POST логина завершается **401 «Вход на portal.1c.ru не подтверждён»** (см. последние логи в #334), после чего цепочка не доводится и каталог возвращается как `NetworkError`. #323 дополнительно показывает «Редирект 302 … Редирект не пройден» при проверке каталогов конфигураций (тот же вход не выполняется/не подтверждается).

### 1.2 Диагноз по коду

- [`SendWithAuthAsync`](Configuration%20Management/Services/OneCUpdatesService.cs:872) — гибридная авторизация: вход запускается при 401/403 или редиректе на login.1c.ru, один раз за сессию (`_portalLoginAttempted`).
- [`TryLoginPortalAsync`](Configuration%20Management/Services/OneCUpdatesService.cs:1022) — GET формы → извлечение `execution` ([`ExtractFormExecution`](Configuration%20Management/Services/OneCUpdatesService.cs:1185)) → POST полей (`username/password/execution/_eventId=submit/rememberMe/anotherComputer/geolocation/inviteCode/inviteType`) → шаг 3 доведение редиректов ([`FollowLoginRedirectsAsync`](Configuration%20Management/Services/OneCUpdatesService.cs:1133)).
- При `postStatus == 401` вход считается неудавшимся, лог `[Updates] Вход на portal.1c.ru не подтверждён (status=401)` — а каталог затем помечается `NetworkError` ([`GetTextAsync`](Configuration%20Management/Services/OneCUpdatesService.cs:848)).

**Гипотезы причины 401 (проверить в порядке приоритета):**
1. Форма login.1c.ru изменилась (новые обязательные скрытые поля/CAPTCHA) — POST со старым набором полей отклоняется сервером 401. Сверить HTML формы (`GET https://login.1c.ru/login`) с набором полей в POST.
2. Учётные данные «Основной» записи из `its_accounts.json` устарели/неверны или пароль хранится не в том виде ([`GetCredentials`](Configuration%20Management/Services/OneCUpdatesService.cs:999)) — сверить с теми, что работают в браузере пользователя.
3. Порядок/формат куки TGC/JSESSIONID: вход не подтверждается, потому что редирект-цепочка обрывается раньше `releases.1c.ru/public/security_check?ticket=ST-…` ([`FollowLoginRedirectsAsync`](Configuration%20Management/Services/OneCUpdatesService.cs:1133)).

### 1.3 Изменения

Файлы:
- `Configuration Management/Services/OneCUpdatesService.cs`:
  1. Доработать POST-набор полей по фактической форме (при изменении формы); логировать анонимизированные признаки ответа 401 (размер HTML, наличие формы, имена полей) — без пароля.
  2. Ввести отдельный результат «авторизация не подтверждена» вместо пустого текста/`NetworkError`: `TryLoginPortalAsync` возвращает статус, `GetPageTextAsync`/`CheckForUpdatesAsync` пробрасывают `AuthFailed` в модель результата (например, расширить enum статусов `ConfigUpdateCheckResult`/`PlatformUpdate.Error`).
  3. Понятное пользователю сообщение «Вход на portal.1c.ru не подтверждён (401): проверьте учётную запись ИТС в справочнике» (см. также `ru.json`/`en.json`).
- `Configuration Management/Services/OneCPlatformCatalogParser.cs` — константа `PlatformNick = "Platform83"` (строка 24): заменить на список поддерживаемых каталогов (`Platform83`, `Platform85`).
- `Configuration Management/Services/PlatformUpdateService.cs` — [`BuildCatalogUrl`](Configuration%20Management/Services/PlatformUpdateService.cs:202): параметризовать ником каталога; добавлена проверка `Platform85` (#334, п.2 реплики пользователя: «стоит проверять и releases.1c.ru/project/Platform85»).
- `Configuration Management/Services/IPlatformUpdateService.cs` — сигнатура/комментарии при введении параметра каталога.
- `Configuration Management/ViewModels/PlatformDownloadViewModel.cs` и `PlatformUpdateViewModel.cs` — проброс статуса `AuthFailed` в лог/сообщение окна.
- `Configuration Management/Views/PlatformDownloadWindow.xaml.cs`, `.Avalonia.cs`, `PlatformUpdateWindow.xaml.cs`, `.Avalonia.cs` — отображение понятной ошибки авторизации.
- `Configuration Management/Views/UpdateCheckWindow.xaml.cs` ([`FindLinkedConfig`](Configuration%20Management/Views/UpdateCheckWindow.xaml.cs:127)), `.Avalonia.cs` (строка 168) — распознавание `AuthFailed`, предложение проверить учётную запись; кликабельная ссылка уже есть (0.3.9.287), не трогаем.
- `Configuration Management/Services/ConfigUpdateService.cs` — статус `AuthFailed` при редиректе на login.1c.ru (маркер `LoginHostMarker`).
- `Configuration Management/Services/BuiltInConfigTypes.cs` — сверить ники конфигураций: в последнем логе #323 запрос идёт по `Accounting30`, ранее был `AccountingCorp30`; убедиться, что ник соответствует действительному каталогу releases.1c.ru.
- `Configuration Management/Localization/Languages/ru.json`, `en.json` — новые строки сообщения об ошибке авторизации.

### 1.4 Тесты

- `ConfigurationManagement.Tests/OneCUpdatesUrlTests.cs` — построение URL каталога платформы (Platform83/Platform85).
- `ConfigurationManagement.Tests/OneCPlatformCatalogParserTests.cs` — парсинг каталога Platform85 (формат таблицы `versionsTable` тот же).
- `ConfigurationManagement.Tests/PlatformUpdateServiceTests.cs` — статус `AuthFailed` при 401/редиректе на login.1c.ru.
- `ConfigurationManagement.Tests/PlatformDownloadTests.cs`, `PlatformDownloadViewModelTests.cs`, `PlatformUpdateViewModelTests.cs` — обработка `AuthFailed`.
- `ConfigurationManagement.Tests/UpdateCheckCatalogTests.cs` — статус авторизации при проверке каталога конфигураций.
- `ConfigurationManagement.Tests/BuiltInConfigTypesTests.cs` — корректность ников (например `Accounting30`/`AccountingCorp30`).

### 1.5 Критерий приёмки

- С реальной учётной записью ИТС: каталог версий платформы получается в окнах «Проверка обновлений платформы», «Скачивание версии платформы» и «Проверка обновлений конфигурации» (F9); вместо `NetworkError` при неверном логине — понятное сообщение «не подтверждён (401)».
- Каталог `Platform85` проверяется и находится (если существует на releases.1c.ru).

---

## Группа 2. Стабильность выделения: #342 (релиз A)

### 2.1 Корень (найден)

[`ProcessInspectorViewModel.ApplyRows`](Configuration%20Management/ViewModels/ProcessInspectorViewModel.cs:172) при каждом перезаполнении (автообновление каждые [`AutoRefreshIntervalMs = 5000`](Configuration%20Management/ViewModels/ProcessInspectorViewModel.cs:20)) безусловно выполняет `SelectedRow = null` (строка 178) после `Processes.Clear()`+`Add`. Выделение сбрасывается через 4–5 секунд — ровно период таймера, — и кнопка «Завершить процесс» (`KillSelected`, строка 107) находит `SelectedRow == null` и молча возвращается.

### 2.2 Изменения

Файлы:
- `Configuration Management/ViewModels/ProcessInspectorViewModel.cs`:
  1. В `ApplyRows`: перед `Processes.Clear()` запомнить `var selectedPid = SelectedRow?.Pid;`.
  2. После перезаполнения найти строку с `Pid == selectedPid` и восстановить `SelectedRow` (новая строка создаётся заново при каждом опросе — сравнивать по идентификатору процесса, а не по ссылке).
  3. Если процесса с сохранённым PID в новом списке нет (процесс завершился) — оставить `SelectedRow = null`.
  4. `KillSelected`: при пустом `SelectedRow` вместо молчаливого `return` показать подсказку «Выберите процесс из списка» (защита от повторения жалобы «ничего не происходит»).
- `Configuration Management/Views/ProcessInspectorWindow.xaml` / `.xaml.cs` — проверить, что `SelectedItem` DataGrid связан с `SelectedRow` TwoWay и переживает замену коллекции (WPF пересоздаёт привязку при `Clear`+`Add` — восстановление через VM достаточно; окно не менять, если не потребуется).
- `Configuration Management/Views/ProcessInspectorWindow.Avalonia.cs` — аналогично для ListBox `SelectedItem`.
- `Configuration Management/ViewModels/ProcessRowViewModel.cs` — свойство `Pid` уже есть (используется в `KillConfirm`); изменений не требуется.

### 2.3 Тесты

- `ConfigurationManagement.Tests/ProcessInspectorParsingTests.cs` (или новый `ProcessInspectorSelectionTests.cs`):
  1. `Refresh_PreservesSelectionByPid` — выбран PID X → `Refresh()` (fake-источник возвращает тот же список) → `SelectedRow.Pid == X`.
  2. `Refresh_ClearsSelectionWhenProcessGone` — процесс X исчез из нового списка → `SelectedRow == null`.
  3. `KillSelected_NoSelection_ShowsHint` — `SelectedRow == null` → `KillSelected` не вызывает киллер, диалог-подсказка вызван.

---

## Группа 3. Снятие выделения после мультивыделения/контекстного меню: #340 (релиз A)

### 3.1 Контекст

Было три попытки (0.3.9.277, 0.3.9.291, 0.3.9.299): `TryApplyTreeClickAfterMenuClosed` ([`MainWindow.Hotkeys.cs:736`](Configuration%20Management/Views/MainWindow.Hotkeys.cs:736), [`MainWindow.Events.cs:736`](Configuration%20Management/Views/MainWindow.Events.cs:736)) и `SuppressTreeMenuCloseClickTail` ([`MainWindow.Events.cs:803`](Configuration%20Management/Views/MainWindow.Events.cs:803)) на WPF, `BatchSelectionHelper.DecideAfterMenuCloseClick` ([`BatchSelectionHelper.cs:205`](Configuration%20Management/Services/BatchSelectionHelper.cs:205)). Ни одна не помогла: строка сначала становится активной, затем (отложенно) выделение пропадает.

### 3.2 Новый подход (дедупликация клика по данным события, а не подавление по флагу)

Отказаться от паттерна «применить выбор отложенно → подавить повторную доставку». Вместо этого: **клик, которым закрыли контекстное меню, вообще не должен доходить до дерева как новое действие выбора**.

Файлы (WPF):
- `Configuration Management/Views/MainWindow.Events.cs`, `Configuration Management/Views/MainWindow.Hotkeys.cs`:
  1. В момент, когда попап контекстного меню захватил мышь, запомнить «исходный клик»: `(кнопка, время MouseDown, координаты)` — в обработчике `ContextMenu.Closed` или `PreviewMouseLeftButtonDown` дерева, предшествующем закрытию меню.
  2. В `OnInfobaseTree_PreviewMouseLeftButtonDown`: если событие совпадает с запомненным исходным кликом (временной допуск ~300 мс + позиция в пределах строки) — `e.Handled = true` **без применения выбора** (строка не активируется, текущее выделение не трогаем).
  3. Применять выбор строки только из данных последнего реального `MouseDown`, обработанного до/вне меню, однократно. Отложенный повтор `TryApplyTreeClickAfterMenuClosed` и флаг `_suppressTreeClickTail` — удалить/выключить (оставить только простой детектор «клик при закрытом меню», если нужен для крайних случаев).
- `Configuration Management/Services/BatchSelectionHelper.cs` — добавить чистый helper для решения «это повторная доставка того же клика?»: `IsSameClick(timestamp, position, toleranceMs, tolerancePx)` + функцию выбора единственного действия по данным последнего MouseDown. `DecideAfterMenuCloseClick` оставить, если используется в Avalonia-пути, или упростить.
- `Configuration Management/Localization/Languages/ru.json` — при добавлении подсказки — строка.

Файлы (Avalonia/Linux):
- `Configuration Management/Views/MainWindow.Avalonia.Events.cs`, `MainWindow.Avalonia.Hotkeys.cs`, `MainWindow.Avalonia.Tree.cs` — тот же механизм (контекстное меню строится в [`MainWindow.Avalonia.Tree.cs`](Configuration%20Management/Views/MainWindow.Avalonia.Tree.cs:1974)); Avalonia доставляет PointerPressed/PointerReleased — дедупликация по времени+позиции аналогична.

### 3.3 Тесты

- `ConfigurationManagement.Tests/BatchSelectionHelperTests.cs` — новые тесты на `IsSameClick` (совпадение по времени/позиции, разные клики) и на «выбор применяется один раз по данным последнего MouseDown».
- Ручные сценарии обязательны (в приёмке): WPF и Linux/Avalonia:
  1. Выделить несколько строк → правый клик → клик по другой строке → новая строка остаётся активной, выделение не пропадает.
  2. Простой кейс: контекстное меню по строке → клик по другой строке → строка остаётся активной.

---

## Группа 4. Парсер rac «cluster list»: #324 (релиз A)

### 4.1 Корень (по фактам issue)

Команда `rac.exe localhost:27545 cluster list` выполняется с `exit=0, stdout=1060 симв.`, но монитор показывает «Подключение установлено, но кластеры не найдены». Приведённый пользователем вывод **не таблица, а блоки «ключ : значение» с выравниванием пробелами и двоеточием**:

```
cluster                                   : cbc95ef0-99c9-4b1a-909f-cff4c8de61d9
host                                      : ALF
port                                      : 27541
name                                      : "Локальный кластер"
```

[`RacOutputParser.ToClusters`](Configuration%20Management/Services/RacOutputParser.cs:199) рассчитан на табличный формат (заголовок + строки колонок, `ParseTable`, разделители `\t`/2+ пробела) — формат «key : value» не извлекается, результат пуст. (Парсер info-команд — `ToClusterInfo` — уже понимает «ключ: значение», но путь `ToClusters` его не использует.)

### 4.2 Изменения

Файлы:
- `Configuration Management/Services/RacOutputParser.cs`:
  1. В `ToClusters`: если табличный разбор дал ноль кластеров — попробовать формат «ключ : значение»: разбить вывод на блоки по повторяющемуся ключу `cluster` (значение — GUID), внутри блока извлечь `name`, `port` (и при наличии — `host`); значения снять с кавычек (`"Локальный кластер"`).
  2. Единый внутренний helper `TryParseKeyValueBlocks(output, IReadOnlyList<(key, mapTo)> spec)` — переиспользовать для устойчивости к обеим версиям rac.
  3. XML-комментарий: документировать оба формата вывода cluster list (таблица и key-value блоки).
- `Configuration Management/Services/RacClient.cs` — [`GetClustersAsync`](Configuration%20Management/Services/RacClient.cs:50): предупреждение «пустой вывод» оставить; при необходимости дополнить подсказкой про формат вывода (не менять поведение).
- `Configuration Management/ViewModels/ServerMonitorViewModel.cs` — сообщение «Подключение установлено, но кластеры не найдены»: после фикса парсера покажет реальные кластеры; текст менять только при подтверждении, что он вводит в заблуждение.
- `Configuration Management/ViewModels/ClusterImportViewModel.cs` — то же окно «Подключение»; без изменений логики, прогон тестов.
- `Configuration Management/Models/RacModels.cs` — проверить соответствие полей `RacCluster` (id/name/port) разобранным значениям; `port` при пустом значении — по умолчанию 1541 (существующее поведение).

### 4.3 Тесты

- `ConfigurationManagement.Tests/RacOutputParserTests.cs` — новые:
  1. `ToClusters_ParsesKeyValueBlocks` — реальный вывод из issue (блок «ключ : значение» с выравниванием) → 1 кластер с id/name/port.
  2. `ToClusters_ParsesMultipleKeyValueBlocks` — 2+ кластера подряд.
  3. `ToClusters_KeyValueWithQuotedName` — имя в кавычках снимается.
  4. `ToClusters_TableFormatStillWorks` — существующий табличный формат не сломан (регрессия).
- `ConfigurationManagement.Tests/RacClientTests.cs` — маршрут `GetClustersAsync → ToClusters` на образце key-value вывода (если тест-класс имеет инфраструктуру fake-процесса).
- Прогон `ServerMonitorViewModelTests.cs`, `ClusterImportViewModelTests.cs` — без регрессий.

---

## Группа 5. Окно «Типовые конфигурации»: #321 (релиз A)

### 5.1 Что исправить (остаток из последних реплик)

1. Окно `EditionEditWindow` («Добавить/Изменить строку с версией») сделать выше — поле URL (`UrlOverrideBox`) сейчас не видно ([`EditionEditWindow.xaml`](Configuration%20Management/Views/EditionEditWindow.xaml:7), `Height=360`).
2. Поля формы и окно поиска выглядят «серыми» (кажется, недоступны для правки) — поправить стили TextBox/фон.
3. Главному окну списка `ConfigTypesEditWindow` — стандартная кнопка максимизации.

### 5.2 Изменения

Файлы:
- `Configuration Management/Views/EditionEditWindow.xaml`:
  - `Height` 360 → 520, `MinHeight` 330 → 470; `ResizeMode` оставить `CanResizeWithGrip` (или `CanResize`), `SizeToContent="Height"` НЕ ставить без проверки (позволит ресайз вверх/вниз).
- `Configuration Management/Views/EditionEditWindow.xaml.cs` — при необходимости установить размер окна по контенту; без логики.
- `Configuration Management/Views/EditionEditWindow.Avalonia.cs` — высота окна Avalonia (`ModalWindowBase`), поля — те же правки по коду.
- `Configuration Management/Views/ConfigTypeEditWindow.Avalonia.cs` — окно поиска конфигураций: убрать «серость» (стиль TextBox/IsEnabled), сделать читаемым.
- `Configuration Management/Views/ConfigTypesEditWindow.xaml` / `.xaml.cs` — главное окно списка: `ResizeMode="CanResize"` (или проверить текущий `CanResizeWithGrip`) — чтобы появилась кнопка максимизации; `MinWidth`/`MinHeight` оставить.
- `Configuration Management/Views/ConfigTypesEditWindow.Avalonia.cs` — то же для Avalonia (`CanResize`).
- `Configuration Management/Themes/DarkTheme.xaml`, `LightTheme.xaml`, `Controls.axaml` — если «серость» задаётся стилем TextBox в теме — выборочно поправить для этих окон (не глобально, чтобы не задеть остальные формы).
- `Configuration Management/Localization/Languages/ru.json`, `en.json` — при добавлении подсказок/подписей.

### 5.3 Тесты

- Юнит-тестов нет (WPF/Avalonia XAML). Прогон `WebLinkLocaleTests.cs`, `SettingsWindowXamlResourcesTests.cs` — убедиться, что ресурсы локализации не сломаны.
- Ручная проверка (обе платформы): поле URL видно без прокрутки; поля редактируются и не выглядят недоступными; окно списка можно развернуть на весь экран.

---

## Группа 6. Горизонтальный скрол — регрессы новой схемы: #309 (релиз A)

### 6.1 Корень (по логу из последней реплики)

Пользователь подтвердил: «Скрол стал нормальным». Осталось два регресса:
1. **Пустое место справа от колонок**: в логе `CM_COLUMNS`: `total=991,2` (сумма колонок), а `extent=1158,8`, `scrollable=408,0` при `viewport=750,8` — прокручиваемая область (`extent`) сильно больше суммы колонок; очевидно, ширина считается по максимальной желаемой ширине строки/заголовка (замер контента), а не по сумме видимых колонок.
2. **Автопрокрутка вправо при старте/удалении колонки**: `EnsureHorizontalReach` ([`MainWindow.Columns.cs:379`](Configuration%20Management/Views/MainWindow.Columns.cs:379)) или восстановление позиции прокрутки уводит полосу в крайнюю правую позицию.

### 6.2 Изменения

Файлы:
- `Configuration Management/Views/MainWindow.Columns.cs`:
  1. Ширину контента считать строго по сумме видимых колонок (`ListMinWidthCalculator.Compute`, существующий `total`), а не по `ExtentWidth` живого ScrollViewer; в [`SyncHeaderWidthWithList`](Configuration%20Management/Views/MainWindow.Columns.cs:636) целевая ширина заголовка = `max(total, viewport)` (без «хвоста» от extent) — продолжение подхода PLAN-0.3.9.298 (A1), при необходимости повторить для области списка, чтобы `extent ≈ max(total, viewport)`.
  2. `EnsureHorizontalReach`: не вызывать безусловный `ScrollToHorizontalOffset(ExtentWidth)` на старте и при изменении набора колонок; докручивать только если пользовательская правая граница контента действительно недостижима, и с минимальным сдвигом (не в самый конец).
  3. Проверить `UpdateTreeMinWidth` (строки 785+) и путь `presenter.MinWidth` — не растягивает ли презентер контент за сумму колонок (если да — ограничить по `total`).
- `Configuration Management/Views/MainWindow.Scroll.cs` — [`OnTreeScroll_ScrollChanged`](Configuration%20Management/Views/MainWindow.Scroll.cs:247): при старте/перезаполнении не восстанавливать позицию «вправо»; сохранять предыдущую горизонтальную позицию и применять её после смены набора колонок.
- `Configuration Management/Views/MainWindow.Avalonia.Columns.cs` (`SyncListWidthToViewport`), `MainWindow.Avalonia.Scroll.cs` — те же правила для Avalonia.
- `Configuration Management/Views/ListMinWidthCalculator.cs` — проверить `Compute`: сумма должна включать только видимые колонки (скрытые — исключены, см. ColumnVis), без добавления `sbw` в сумму контента (полоса добавляется во viewport-расчёт, а не в content).

### 6.3 Тесты

- `ConfigurationManagement.Tests/ListMinWidthCalculatorTests.cs` — новые/правка: сумма колонок без лишнего слагаемого («хвоста»); скрытая колонка не влияет на сумму.
- Ручная проверка WPF и Avalonia: сузить окно → полоса появилась; достижима последняя колонка; справа от последней колонки нет пустого места (`extent ≈ сумма колонок`); при старте и после удаления колонки позиция не прыгает вправо.

---

## Группа 7. Создание серверной базы: #305 (релиз A)

### 7.1 Корни (по последней реплике)

1. В предупреждении «Выбранная версия платформы 8.3.27 отличается от версии 8.5.1 …» **не указан порт** — сравнение серверов выполняется без учёта порта: [`SameServer`](Configuration%20Management/Services/CreateInfobaseService.cs:308) (комментарий: «порт srv:1541 и регистр не мешают»), эвристика [`GetIncompatibleExistingVersion`](Configuration%20Management/Services/CreateInfobaseService.cs:270) находит базы на «localhost» независимо от порта.
2. «Сервер СУБД» не подставляется при повторном открытии окна создания: сохранение — [`SaveLastDbServer`](Configuration%20Management/Services/CreateInfobaseService.cs:389) (вызов на строке 133 после успешного создания), восстановление — [`RestoreLastDbServer`](Configuration%20Management/Views/CreateInfobaseWindow.xaml.cs:606) (Avalonia: [`CreateInfobaseWindow.Avalonia.cs:912`](Configuration%20Management/Views/CreateInfobaseWindow.Avalonia.cs:912)). Цепочка на практике теряется.

### 7.2 Изменения

Файлы:
- `Configuration Management/Services/CreateInfobaseService.cs`:
  1. `SameServer(a, b)`: сравнивать сервер с учётом порта: если у обеих сторон порт явно задан и отличается → не равны; если порт не задан хотя бы у одной → равны (fallback). Разобрать `server:port` через [`ParseServerPort`](Configuration%20Management/Services/CreateInfobaseService.cs:230).
  2. `GetIncompatibleExistingVersion` — использовать новое сравнение; возвращать не только версию, но и полный адрес найденной базы (сервер+порт) для текста предупреждения.
  3. `SaveLastDbServer` — проверить сохранение в `AppSettings` (`LastDbServer`/`LastDbPort`, [`Models/AppSettings.cs:130`](Configuration%20Management/Models/AppSettings.cs:130)) и `_repository.SaveSettings`; убедиться, что сохраняется именно введённое значение (не порт 1С вместо порта СУБД).
- `Configuration Management/Views/CreateInfobaseWindow.xaml.cs`, `CreateInfobaseWindow.Avalonia.cs`:
  1. В тексте предупреждения о различии версий выводить адрес сервера с портом: «базы на сервере localhost:1541 работают с версией 8.5.1…».
  2. `RestoreLastDbServer` — проверить источник значений (настройки могли не перечитаться после создания: убедиться, что окно читает свежие настройки при открытии, а не кэш из старого экземпляра).
- `Configuration Management/Localization/Languages/ru.json`, `en.json` — формат сообщения предупреждения с портом.

### 7.3 Тесты

- `ConfigurationManagement.Tests/CreateInfobaseDbServerStringTests.cs` — новые:
  1. `SameServer_WithPorts_EqualWhenNoPortSpecified` — «localhost» ≡ «localhost:1541».
  2. `SameServer_WithDifferentPorts_NotEqual` — «localhost:1541» ≠ «localhost:1545».
  3. `SameServer_WithSamePort_Equal` — «localhost:1541» ≡ «localhost:1541».
- Тест на текст предупреждения, если формат выносится в чистую функцию (например, `BuildVersionMismatchMessage(platform, server, port, foundVersion)`).
- Ручная проверка: создать клиент-серверную базу → повторно открыть окно → «Сервер СУБД» подставлен; при разных портах на сервере — предупреждение показывает порт.

---

## Группа 8. Анализ функционала связывания базы: #345 (релиз A, БЕЗ изменений кода)

### 8.1 Суть задачи

Issue — вопрос «нужен ли теперь функционал связи базы с типовой, если есть колонка "Имя конфигурации"?». Решение принимается сообща, в код без решения не вносить. Задача релиза A — **анализ** и подготовка материала для решения.

### 8.2 Область анализа (файлы только для чтения)

- `Configuration Management/Views/ConfigUpdateLinkWindow.xaml.cs`, `.Avalonia.cs` — окно «Связать с конфигурацией» (issue #322): пишет `UpdateConfigCode`, `UpdateUrlOverride`, `UpdateUrlSegment` ([`Infobase.cs:331`](Configuration%20Management/Models/Infobase.cs:331)).
- `Configuration Management/Views/MainWindow.Tree.cs`, `MainWindow.Avalonia.Tree.cs` — подменю «Обновление и связь» (пункт 1974–1975 в Avalonia): кнопки «Связать с конфигурацией», «Список типовых конфигураций», «Проверка обновлений».
- `Configuration Management/Views/UpdateCheckWindow.xaml.cs` (`FindLinkedConfig`, строка 127), `.Avalonia.cs` (строка 168) — зависимость F9 (#323) от явной связи: «Явное связывание (UpdateConfigCode) — приоритет. Если связи нет, но свойства конфигурации базы определены (вкладка „Платформа“) — строим каталог релизов из них».
- `Configuration Management/Services/ConfigTypeMatcher.cs` — сопоставление по имени конфигурации (issue #322/#323).
- `Configuration Management/Services/ConfigUpdateService.cs`, `Configuration Management/Services/BuiltInConfigTypes.cs` — каталог релизов по нику.
- `Configuration Management/ViewModels/ActualReleasesViewModel.cs` — окно «Актуальные релизы»: использует ли явную связь.
- `Configuration Management/ViewModels/MaintenanceCenterViewModel.cs` (строка 319–321) — результат проверки по `UpdateConfigCode`.

### 8.3 Результат анализа (материал для решения)

1. Таблица мест использования «Связать с конфигурацией» и поля `UpdateConfigCode`/`UpdateUrlOverride`/`UpdateUrlSegment` (кто пишет/читает).
2. Проверка зависимости: работает ли F9/#323 и «Актуальные релизы» без явной связи (только по «Имени конфигурации» + свойствам вкладки «Платформа»); где явная связь является **единственным** способом получить каталог релизов (например, когда имя конфигурации базы ≠ имени типовой).
3. Варианты с последствиями:
   - **Оставить как есть** (рекомендуется по умолчанию до проверки п.2);
   - **Упростить**: убрать кнопку «Связать» из контекстного меню, оставить автоматическое определение по «Имени конфигурации» (+ ручной ввод ников в окне «Типовые конфигурации»);
   - **Удалить**: полный отказ от `UpdateConfigCode` — оценить риск для #323 и существующих данных пользователей (миграция настроек).
4. Рекомендация и перечень вопросов пользователю. **Изменения кода — только после решения сообща** (в релизе B или отдельной задачей).

---

## Версия, CHANGELOG, README, комментарии в issues (релиз A, 0.3.9.300)

### Версия

Файл: `Configuration Management/Configuration Management.csproj` (строки 62–65):
- `Version` = `0.3.9.300`, `AssemblyVersion` = `0.3.9.300`, `FileVersion` = `0.3.9.300`, `InformationalVersion` = `0.3.9.300`.

### CHANGELOG.md

Секция `## [0.3.9.300] — 2026-10-03` над `0.3.9.299`, по образцу прошлых записей (см. `CHANGELOG.md`):
- **Инспектор процессов (#342)**: выделение строки сохраняется при автообновлении списка (по PID), кнопка «Завершить процесс» работает после обновления.
- **Выделение после контекстного меню (#340)**: новый механизм — клик, закрывший меню, не перевыбирает строку; выбор применяется однократно по данным реального MouseDown.
- **Типовые конфигурации (#321)**: окно правки редакции выше (поле URL видно), поля не «серые», главному окну добавлена кнопка максимизации.
- **Горизонтальный скрол (#309)**: прокручиваемая область равна сумме видимых колонок (без пустого хвоста), позиция не уводится вправо при старте и удалении колонки.
- **Создание серверной базы (#305)**: сравнение серверов учитывает порт, в предупреждении о версии платформы указывается полный адрес; «Сервер СУБД» восстанавливается при повторном открытии окна.
- **Серверы 1С (#324)**: парсер «cluster list» понимает формат вывода «ключ : значение» (новые версии rac) — кластеры отображаются в мониторе.
- **Связывание базы (#345)**: выполнен анализ функционала, итоги и варианты — в комментарии к issue (решение сообща).

### README.md

- Бейдж версии `Версия-0.3.9.299` → `Версия-0.3.9.300`.

### Комментарии в issues (не закрывать!)

Черновики в `publish/`: `comment-342-0.3.9.300.md`, `comment-340-0.3.9.300.md`, `comment-321-0.3.9.300.md`, `comment-309-0.3.9.300.md`, `comment-305-0.3.9.300.md`, `comment-324-0.3.9.300.md`, `comment-345-0.3.9.300.md` (анализ + вопросы). Issues остаются открытыми до подтверждения.

---

## Сборка и релиз (релиз A, выполняется после реализации и прогона тестов)

По прецеденту `publish/release_body_0.3.9.297.md`:
1. Windows/WPF single-file: `powershell -NoProfile -ExecutionPolicy Bypass -File "Configuration Management\build-windows-single-file.ps1"` → `dist/win-x64/ConfigurationManagement.exe`.
2. Linux/Avalonia single-file (кросс-сборка): `powershell -NoProfile -ExecutionPolicy Bypass -File "Configuration Management\build-linux-single-file.ps1"` → `dist/linux-x64/ConfigurationManagement`.
3. .deb-пакет: `python publish/build_deb_win_0.3.9.270.py` (версия из csproj) → `package/linux/deb/out/configuration-management_0.3.9.300_amd64.deb`.
4. Проверки: PE/ELF-magic, `FileVersion/ProductVersion = 0.3.9.300`, smoke `--help` (код 0), контроль .deb по образцу `publish/check_deb_win_0.3.9.297.py` (параметризовать версией).
5. Артефакты в `publish/out-0.3.9.300/` + `SHA256SUMS.txt`; `publish/release_body_0.3.9.300.md`; релиз GitHub `v0.3.9.300`.
6. Публикация комментариев в issues #342/#340/#321/#309/#305/#324/#345 (GH API/gh CLI), issues не закрывать.

---

## Порядок реализации (рекомендуемый, релиз A)

1. **#324** (парсер rac) — изолированный, чисто юнит-тесты, быстрая победа.
2. **#342** (выделение в инспекторе) — изолированный, тестируемый.
3. **#305** (создание серверной базы) — чистые функции `SameServer`/текст предупреждения + проверка цепочки сохранения.
4. **#309** (горизонтальный скрол) — WPF затем Avalonia, ручная проверка.
5. **#321** (окно типовых) — XAML-правки, обе платформы.
6. **#340** (выделение после меню) — самый рискованный: новый механизм дедупликации, обязательная ручная проверка WPF и Linux/Avalonia.
7. **#345** — анализ (чтение кода + материал), может идти параллельно любому пункту.
8. Версия 0.3.9.300, CHANGELOG, README, черновики комментариев.
9. Полный прогон `ConfigurationManagement.Tests`; сборка/релиз (раздел выше) — отдельной задачей.

После подтверждения пользователем результатов релиза A и доступа к порталу 1С — релиз B по разделу 1 (#334/#330/#323 + Platform85), версия 0.3.9.301.

---

## Сводка затрагиваемых файлов (релиз A)

Код:
- `Configuration Management/ViewModels/ProcessInspectorViewModel.cs` — #342.
- `Configuration Management/Views/ProcessInspectorWindow.xaml`, `.xaml.cs`, `ProcessInspectorWindow.Avalonia.cs` — #342 (проверка привязки).
- `Configuration Management/Views/MainWindow.Events.cs`, `MainWindow.Hotkeys.cs`, `MainWindow.Avalonia.Events.cs`, `MainWindow.Avalonia.Hotkeys.cs`, `MainWindow.Avalonia.Tree.cs` — #340.
- `Configuration Management/Services/BatchSelectionHelper.cs` — #340 (helper дедупликации).
- `Configuration Management/Services/RacOutputParser.cs`, `RacClient.cs` — #324.
- `Configuration Management/Views/EditionEditWindow.xaml`, `.xaml.cs`, `EditionEditWindow.Avalonia.cs`, `ConfigTypeEditWindow.Avalonia.cs`, `ConfigTypesEditWindow.xaml`, `.xaml.cs`, `ConfigTypesEditWindow.Avalonia.cs`, `Configuration Management/Themes/*` — #321.
- `Configuration Management/Views/MainWindow.Columns.cs`, `MainWindow.Scroll.cs`, `MainWindow.Avalonia.Columns.cs`, `MainWindow.Avalonia.Scroll.cs`, `ListMinWidthCalculator.cs` — #309.
- `Configuration Management/Services/CreateInfobaseService.cs`, `Models/AppSettings.cs`, `Views/CreateInfobaseWindow.xaml.cs`, `CreateInfobaseWindow.Avalonia.cs` — #305.
- `Configuration Management/Localization/Languages/ru.json`, `en.json` — сообщения.

Тесты:
- `ConfigurationManagement.Tests/ProcessInspectorParsingTests.cs` (или новый `ProcessInspectorSelectionTests.cs`) — #342.
- `ConfigurationManagement.Tests/BatchSelectionHelperTests.cs` — #340.
- `ConfigurationManagement.Tests/RacOutputParserTests.cs`, `RacClientTests.cs` — #324.
- `ConfigurationManagement.Tests/ListMinWidthCalculatorTests.cs` — #309.
- `ConfigurationManagement.Tests/CreateInfobaseDbServerStringTests.cs` — #305.
- Прогон без регрессий: `ServerMonitorViewModelTests.cs`, `ClusterImportViewModelTests.cs`, `WebLinkLocaleTests.cs`, `SettingsWindowXamlResourcesTests.cs`.

Документация/релиз:
- `Configuration Management/Configuration Management.csproj` — версия 0.3.9.300.
- `CHANGELOG.md`, `README.md`, `publish/comment-*.md` (черновики), `publish/release_body_0.3.9.300.md`, `publish/out-0.3.9.300/` — на этапе релиза.

Код для релиза B (0.3.9.301, план в разделе 1): `OneCUpdatesService.cs`, `OneCPlatformCatalogParser.cs`, `PlatformUpdateService.cs`, `IPlatformUpdateService.cs`, `PlatformDownloadViewModel.cs`, `PlatformUpdateViewModel.cs`, окна `PlatformDownloadWindow`/`PlatformUpdateWindow`/`UpdateCheckWindow` (WPF+Avalonia), `ConfigUpdateService.cs`, `BuiltInConfigTypes.cs`, локализация; тесты `OneCUpdatesUrlTests`, `OneCPlatformCatalogParserTests`, `PlatformUpdateServiceTests`, `PlatformDownloadTests`, `PlatformDownloadViewModelTests`, `PlatformUpdateViewModelTests`, `UpdateCheckCatalogTests`, `BuiltInConfigTypesTests`.

---

## Критерии приёмки (релиз A)

- **#342**: в инспекторе процессов после 5-секундного автообновления выбранная строка сохраняет выделение; «Завершить процесс» завершает выбранный процесс; если процесс завершён — выделение корректно снимается; тесты зелёные.
- **#340**: клик по другой строке после контекстного меню оставляет строку активной (WPF и Linux/Avalonia); мультивыделение и обычный клик работают как раньше; тесты `BatchSelectionHelperTests` зелёные.
- **#324**: монитор серверов показывает кластеры из реального вывода `cluster list` (key-value формат); тесты `RacOutputParserTests` зелёные.
- **#321**: поле URL в окне редакции видно; поля не выглядят недоступными; кнопка максимизации работает на обеих платформах.
- **#309**: extent ≈ сумме видимых колонок (нет пустого места справа); при старте и удалении колонки позиция не уходит вправо; скролл по-прежнему достигает последней колонки.
- **#305**: предупреждение о версии платформы содержит адрес сервера с портом; сравнение серверов учитывает порт; «Сервер СУБД» подставляется при повторном открытии окна.
- **#345**: в комментарий к issue опубликован анализ и варианты решения; код не менялся.
- Релиз: 3 артефакта (exe, linux-x64, deb) с контрольными суммами; комментарии «исправлено в версии 0.3.9.300»; issues открыты.

## Риски

- **#340 — самый рискованный пункт** (3 неудачные попытки). Новый подход меняет модель обработки клика: возможны побочные эффекты на двойной клик (запуск базы) и drag&drop. Обязательно: детальный ручной сценарий на WPF и Linux/Avalonia, при необходимости откат к прежнему поведению точечно (helper вместо полного удаления старого кода).
- **#309**: изменение ширины контента может вернуть «недостижимость последней колонки» (было до 0.3.9.298). Балансировать: полоса и достижимость — приоритетнее «идеального» extent; критерий из лога `CM_COLUMNS_TRACE=1` (одна запись за сессию).
- **#305**: смена семантики `SameServer` может изменить поведение предупреждения для пользователей без порта — проверено тестами; цепочка сохранения зависит от порядка SaveSettings/перечитывания настроек окна — проверить вручную на обеих платформах.
- **#342**: восстановление выделения по PID при частом перезапуске процесса может «перепрыгнуть» на новый процесс с тем же PID — допустимо (PID уникален в момент опроса), документировать в тесте.
- **#324**: вывод rac может отличаться между версиями платформы (8.3 vs 8.5) — парсер поддерживает оба формата; тесты покрывают оба.
- **Двуплатформенность**: правки #340/#309 — обе платформы (WPF и Avalonia); #342/#305/#324 — common/VM + проверка окон обеих платформ; #321 — обе платформы (XAML и код Avalonia).

---

## Группа 9. Результат анализа #345 (реализован в Задаче 3; код НЕ менялся)

Материал для решения «нужен ли функционал связи базы с типовой конфигурацией, если есть
колонка „Имя конфигурации"» (issue #345). Код не изменялся — только чтение и карта использований.

### 9.1 Карта использований «Связать с конфигурацией» и полей связи

Поля связи ИБ — [`Infobase.cs:331`](Configuration%20Management/Models/Infobase.cs:331) (`UpdateConfigCode`,
`UpdateUrlOverride`, `UpdateUrlSegment`):

| № | Место (кто) | Операция | Как влияет на работу |
|---|-------------|----------|----------------------|
| 1 | [`ConfigUpdateLinkWindow.xaml.cs:328`](Configuration%20Management/Views/ConfigUpdateLinkWindow.xaml.cs:328), `.Avalonia.cs:365` | Пишет все три поля при сохранении окна «Связать с конфигурацией» | Единственная точка ЗАПИСИ. Помимо ИБ обновляет совпадение по имени в списке (все базы с тем же `ConfigurationName` — строки 353–356 / 391–394) |
| 2 | [`UpdateCheckWindow.xaml.cs:127`](Configuration%20Management/Views/UpdateCheckWindow.xaml.cs:127), `.Avalonia.cs:168` (`FindLinkedConfig`) | Читает `UpdateConfigCode` → находит `OneCConfigType` по коду | F9/#323: явная связь имеет ПРИОРИТЕТ над автоопределением по имени (строки 76–81: если `FindLinkedConfig()` вернул null, а `ConfigurationName` заполнено — подбирается `ConfigTypeMatcher.FindByInfobaseName`) |
| 3 | [`UpdateCheckWindow.xaml.cs:83`](Configuration%20Management/Views/UpdateCheckWindow.xaml.cs:83), `.Avalonia.cs:124` | `BuildUpdateUrl(config, edition, UpdateUrlOverride, UpdateUrlSegment)` | URL каталога релизов: override/персональный ник базы имеют приоритет над ником типовой |
| 4 | [`UpdateCheckWindow.xaml.cs:246`](Configuration%20Management/Views/UpdateCheckWindow.xaml.cs:246), `.Avalonia.cs:289` | `UpdateCheckCache[UpdateConfigCode] = result` | Кэш результатов проверки индексируется по коду связи |
| 5 | [`MaintenanceCenterViewModel.ResolveUpdateResult`](Configuration%20Management/ViewModels/MaintenanceCenterViewModel.cs:317) | Читает `UpdateConfigCode` из кэша | Центр обслуживания и HTML-отчёт показывают результат проверки по коду связи |
| 6 | [`MainWindow.Tree.cs`](Configuration%20Management/Views/MainWindow.Tree.cs) / [`MainWindow.Avalonia.Tree.cs:1988`](Configuration%20Management/Views/MainWindow.Avalonia.Tree.cs:1988) | Подменю «Обновление и связь»: «Связать с конфигурацией», «Список типовых конфигураций», «Проверка обновлений» (F9) | Точки входа UI |

Не используют явную связь:
- [`ActualReleasesViewModel`](Configuration%20Management/ViewModels/ActualReleasesViewModel.cs:54) / окно «Актуальные релизы» (ALT+F9): строки строятся по объектам `OneCConfigType` напрямую (каталог/ник конфигурации), поля связи ИБ не читаются и не пишутся.
- Автоопределение по «Имени конфигурации»: [`ConfigTypeMatcher.FindByInfobaseName`](Configuration%20Management/Services/ConfigTypeMatcher.cs:67) (точное имя → точный сегмент URL → вхождение имени типовой в имя базы → вхождение имени базы в имя типовой, выбор самого длинного кандидата) — используется в F9 как fallback и, вероятно, в других местах сопоставления.

### 9.2 Зависимость F9/#323 и «Актуальных релизов» от явной связи

1. **F9 / окно «Проверка обновлений» (#323)** — НЕ зависит от явной связи:
   - цепочка: `FindLinkedConfig()` (по `UpdateConfigCode`) → если null → `ConfigTypeMatcher.FindByInfobaseName(..., ConfigurationName)` → `BuildUpdateUrl(...)`.
   - Если у базы заполнены свойства вкладки «Платформа» (`ConfigurationName` + версия), каталог релизов строится по автоматически подобранной типовой конфигурации БЕЗ явной связи. Явная связь нужна только когда автоопределение не срабатывает или нужен персональный ник/ручная ссылка.
2. **«Актуальные релизы» (ALT+F9)** — полностью не зависят от явной связи: работают по общему списку типовых конфигураций.
3. **Где явная связь — ЕДИНСТВЕННЫЙ способ получить каталог**:
   - имя конфигурации базы ≠ имени/сегмента/подстроки типовой (например, пользовательская конфигурация «Моя бухгалтерия» поверх типовой «Бухгалтерия предприятия» — автоопределение может найти «Бухгалтерию», но это не факт и может быть ошибочно);
   - персональный ник базы (`UpdateUrlSegment`) или ручная ссылка (`UpdateUrlOverride`), когда база живёт на отдельном каталоге релизов (частная/отраслевая поставка);
   - кэш результатов (`UpdateCheckCache` по коду) — при удалении связи кэш остаётся «висеть» под старым кодом (косметика).

### 9.3 Варианты решения и последствия

- **Оставить как есть (рекомендуется по умолчанию)**: автоопределение по имени уже работает как fallback; явная связь нужна для «крайних» случаев (п. 9.2.3). Затрат нет, регрессов нет. Рекомендация: подтвердить ручным сценарием на реальной базе, что F9 работает без явной связи (автоопределение + свойства вкладки «Платформа»).
- **Упростить (убрать кнопку «Связать» из меню, оставить автоопределение + ручной ввод ников в «Типовых конфигурациях»)**: риск — потерять персональный ник/ручную ссылку для баз с отдельными каталогами релизов; кнопка «Связать» также применяет связь ко ВСЕМ базам с тем же `ConfigurationName` (удобство пакетной привязки). Сокращает UI, но требует миграции существующих данных связи (или их игнорирования).
- **Удалить полностью (`UpdateConfigCode`/`UpdateUrlOverride`/`UpdateUrlSegment`)**: риск — регресс F9 для баз, где имя ≠ имени типовой; потеря кэша результатов (ключ кода); миграция настроек существующих пользователей; объём правок в окнах, кэше, HTML-отчёте и MaintenanceCenter. Не рекомендуется без явного запроса.

### 9.4 Рекомендация и вопросы пользователю

**Рекомендация: оставить как есть.** Автоопределение по «Имени конфигурации» уже покрывает основной сценарий (fallback в F9), а явная связь закрывает «крайние» случаи и является единственным механизмом персонального ника/ручной ссылки. Дальнейшие шаги — только после решения сообща.

Вопросы пользователю:
1. Наблюдается ли на практике случай, когда F9 не находит каталог релизов при заполненном «Имени конфигурации» (без явной связи)? Если да — какой именно (имя базы vs имя типовой)?
2. Используются ли персональный ник базы / ручная ссылка (поля `UpdateUrlSegment`/`UpdateUrlOverride`) в реальных базах?
3. Есть ли потребность в пакетной привязке нескольких баз к одной конфигурации помимо текущего механизма (применение ко всем базам с тем же именем)?