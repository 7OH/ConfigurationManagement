## 0.3.9.315 — флаги trace.json, стабилизация выделения, вход portal.1c.ru (#323 #340 #347)

Релиз объединяет цикл **0.3.9.313–0.3.9.315** (три кластера: A — вход, B — выделение, C — диагностика).

### 0.3.9.313 — программный вход на portal.1c.ru (issue #323)

**Исправлено.** Вход фактически был успешным (`Вход: POST status=200`, `<title>Личные данные</title>`), но код объявлял `AuthFailed` «в теле форма входа». Общий корень с #330/#334 — правки только в общем сервисе
[`OneCUpdatesService.cs`](Configuration%20Management/Services/OneCUpdatesService.cs):

- страница личного кабинета
  ([`DetectPersonalAreaPage`](Configuration%20Management/Services/OneCUpdatesService.cs)) проверяется
  **раньше** детектора формы входа
  ([`LooksLikeLoginForm`](Configuration%20Management/Services/OneCUpdatesService.cs)) — кабинет
  с execution-формой смены аккаунта и JS-подсказкой «Неверный логин или пароль» больше не даёт
  ложного `AuthFailed`;
- повтор исходного запроса с `service` после успешного входа;
- капча и диагностика title сохранены.

### 0.3.9.314 — выделение строки после закрытия контекстного меню (issue #340)

**Исправлено.** По второму реальному `trace.json` (0.3.9.311) MouseDown по строке приходит в дерево
**до** закрытия меню, а не повторной доставкой после: ни один штатный путь не запускал стабилизацию
`IsSelected`, и переработка контейнеров виртуализацией сбрасывала выделение:

- новый чистый предикат
  [`ShouldStabilizeForClickPrecedingMenuClose`](Configuration%20Management/Services/BatchSelectionHelper.cs)
  (окно `MenuClosePrecedingClickWindowMs = 500` мс);
- «последний обычный клик по строке» запоминается в `ApplySelection` WPF
  ([`MainWindow.Events.cs`](Configuration%20Management/Views/MainWindow.Events.cs)) и в туннельной
  фазе `PointerPressed` Avalonia
  ([`MainWindow.Avalonia.Events.cs`](Configuration%20Management/Views/MainWindow.Avalonia.Events.cs));
- при закрытии контекстного меню дерева, если снимок клика не записан, запускается отложенная
  стабилизация `EnsureSelectionStable(reason="clickBeforeMenuClose")` (WPF и Avalonia);
  стабилизация идемпотентна и не трогает мультивыделение;
- существующий предикат `ShouldStabilizeAfterMenuClose` и диагностика меню НЕ изменялись.

### 0.3.9.315 — trace.json: JSON-конфиг отладочных флагов (issue #347)

**Изменено.** Файл **`trace.json`** рядом с настройками больше НЕ является JSONL-журналом — он стал
JSON-конфигом отладочных флагов `{"version":1,"CM_COLUMNS":false,"CM_MENUCLOSE":false,
"CM_MENUCLICK":false,"CM_REDIRECT":false}`. По умолчанию все флаги выключены — логи закрытых тикетов
перестают «капать» у всех пользователей, диагностика включается по наставлению из тикета:

- новый сервис
  [`TraceFlags`](Configuration%20Management/Services/TraceFlags.cs) с чистым парсером/форматтером
  [`TraceFlagsFormat`](Configuration%20Management/Services/TraceFlagsFormat.cs): конфиг создаётся при
  старте (обе платформы), флаг читается с mtime-кэшем — включается **без перезапуска приложения**;
  повреждённый JSON трактуется как «все выключены», регистр имён безразличен, неизвестные флаги
  игнорируются;
- **миграция старого журнала**: при первом старте `trace.json`-JSONL (0.3.9.308–0.3.9.314)
  переименовывается в **`trace_menuclose_legacy.json`** (история сохраняется), затем создаётся конфиг;
- журнал событий меню
  ([`MenuCloseTrace`](Configuration%20Management/Services/MenuCloseTrace.cs)) пишется ТОЛЬКО при
  `CM_MENUCLOSE=true` (псевдоним `CM_MENUCLICK`) и переехал в **`trace_menuclose.jsonl`**;
  стабилизация выделения 0.3.9.314 работает всегда;
- диагностика колонок — флаг `CM_COLUMNS` ([`MainWindow.Columns.cs`](Configuration%20Management/Views/MainWindow.Columns.cs));
- INFO-диагностика редиректов/входа портала 1С — флаг `CM_REDIRECT`
  ([`OneCUpdatesService.cs`](Configuration%20Management/Services/OneCUpdatesService.cs));
- env-переменные остались только override включения; тесты: новый
  [`TraceFlagsTests.cs`](ConfigurationManagement.Tests/TraceFlagsTests.cs) (+8),
  обновлён [`MenuCloseTraceFormatTests.cs`](ConfigurationManagement.Tests/MenuCloseTraceFormatTests.cs) (+3).

Полный набор `dotnet test` зелёный (**1823**), сборка Windows Release и кросс-сборка Linux без ошибок.

**Подробности** — в [CHANGELOG.md](../../CHANGELOG.md).

### Файлы для установки

| Платформа | Файл | Контрольная сумма (SHA-256) |
|---|---|---|
| Windows (WPF, single-file) | `ConfigurationManagement.exe` | `b61ab6bb36bc716574ac7c3bf28c0860ebc3489914bf8beb5bd4b795b645118e` |
| Linux (Avalonia, single-file) | `ConfigurationManagement-linux-x64` | `71cda4d055bf71fc6363c24a6c32fbe73ee543c45442d32967e69f8d99b9203d` |
| Linux (.deb) | `configuration-management_0.3.9.315_amd64.deb` | `cf27535b0234b55c32fa320340f2a0e9b6f5db9088362e4be4e6331c9209ea6d` |

Полный набор и `SHA256SUMS.txt` — в архиве ниже (attachments).