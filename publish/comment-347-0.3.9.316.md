Исправлено в версии **0.3.9.316** (Windows/WPF и Linux/Avalonia; issue #347 «Сказ о trace.json» — три замечания к 0.3.9.315).

**Что было.**

1. **`trace.json` писался в одну строку** — без переносов строк и отступов: читать и править его вручную было неудобно.
2. **Гейт `CM_COLUMNS` не срабатывал при явном `false`**: записи `CM_COLUMNS: total=…` продолжали появляться в общем логе, хотя в конфиге стояло `"CM_COLUMNS": false`. Причина — env-переменная `CM_COLUMNS_TRACE=1`, оставшаяся с 0.3.9.305, перекрывала явный `false` из файла.
3. **Расположение файлов**: конфиг лежал в корне каталога данных (`%APPDATA%\ConfigurationManagement\` / `~/.config/ConfigurationManagement/`), а не рядом с `settings.json` в каталоге профиля; журнал событий меню назывался `trace_menuclose.jsonl` — «буква l в конце лишняя».

**Что сделано.**

1. **Pretty-print.** `trace.json` сериализуется с переносами строк и отступами (JSON `WriteIndented`) и стабильным порядком ключей — `version`, `CM_COLUMNS`, `CM_MENUCLICK`, `CM_MENUCLOSE`, `CM_REDIRECT`:
   ```json
   {
     "version": 1,
     "CM_COLUMNS": false,
     "CM_MENUCLICK": false,
     "CM_MENUCLOSE": false,
     "CM_REDIRECT": false
   }
   ```
   Парсер остаётся устойчивым к обоим видам записи (компактной и форматированной) — это покрыто тестом round-trip.

2. **Жёсткий гейт `CM_COLUMNS`.** Явное `false` в `trace.json` выключает диагностику колонок **ВСЕГДА**, даже если установлена env `CM_COLUMNS_TRACE=1` (или `CM_COLUMNS=1`). Переменная окружения осталась только запасным способом **включения** — если флаг в конфиге отсутствует либо равен `true`. Проверены все пути записи в [`MainWindow.Columns.cs`](https://github.com/sivatorov/ConfigurationManagement/blob/main/Configuration%20Management/Views/MainWindow.Columns.cs) (`LogColumnsDiagnostics`, стартовый дамп `allowStartupDump`) — всё под единым гейтом `ColumnsTraceEnabled`, безусловных веток нет. Записей `CM_COLUMNS: total=…` при `false` больше нет ни в постоянном логе, ни в стартовом дампе.

3. **Перенос файлов.**
   - Конфиг **`trace.json`** теперь живёт в каталоге данных **активного профиля** — рядом с `settings.json` (`%APPDATA%\ConfigurationManagement\profiles\<Id>\` на Windows, `~/.config/ConfigurationManagement/profiles/<Id>/` на Linux; в legacy-режиме без профиля — корень каталога данных). При переходе с 0.3.9.315 существующий файл мигрирует автоматически ([`TraceFlags.SetProfileDataDirectory`](https://github.com/sivatorov/ConfigurationManagement/blob/main/Configuration%20Management/Services/TraceFlags.cs)); до выбора профиля при старте конфиг создаётся в корне и затем переносится после инициализации профилей.
   - Журнал событий меню переехал в папку логов **`AppDataDirectory/logs`** и переименован в **`trace_menuclose.json`** («буква l в конце лишняя») — резолюция пути в [`MenuCloseTrace`](https://github.com/sivatorov/ConfigurationManagement/blob/main/Configuration%20Management/Services/MenuCloseTrace.cs)/[`MenuCloseTraceFormat`](https://github.com/sivatorov/ConfigurationManagement/blob/main/Configuration%20Management/Services/MenuCloseTraceFormat.cs). Legacy `menuclose_trace.json` от 0.3.9.306 продолжает дописываться, если уже существует (непрерывность диагностики).

**Как проверить.**

1. Установите **0.3.9.316** (Windows или Linux) и запустите приложение один раз.
2. Найдите **`trace.json`** — теперь он в каталоге профиля рядом с `settings.json`:
   - Windows: `%APPDATA%\ConfigurationManagement\profiles\<Id>\trace.json`;
   - Linux: `~/.config/ConfigurationManagement/profiles/<Id>/trace.json`;
   - legacy-режим без профиля — корень каталога данных.
   Файл записан **с переносами строк и отступами**; прежний файл из корня `AppDataDirectory` (от 0.3.9.315) перенесён автоматически.
3. Проверьте гейт `CM_COLUMNS`: установите `"CM_COLUMNS": false` и сохраните файл (перезапуск не нужен). Даже если в системе осталась env `CM_COLUMNS_TRACE=1` — в общем журнале приложения **нет** записей `CM_COLUMNS: total=…` (ни при работе со списком, ни в стартовом дампе). Смените на `true` — записи появляются.
4. Проверьте журнал меню: включите `CM_MENUCLOSE`, повторите сценарий правый клик → левый клик по другой строке — записи пишутся в **`logs\trace_menuclose.json`** (Windows: `%APPDATA%\ConfigurationManagement\logs\`, Linux: `~/.config/ConfigurationManagement/logs/`).

**Тесты.** +5 новых сценариев в [`TraceFlagsTests.cs`](https://github.com/sivatorov/ConfigurationManagement/blob/main/ConfigurationManagement.Tests/TraceFlagsTests.cs): pretty-print round-trip (сериализованный дефолт содержит переводы строк/отступы и корректно разбирается обратно), явный `false` в файле + env=1 → выключено, флаг `true` → включено, нет файла + env=1 → включено, миграция конфига из корня в каталог профиля; обновлены [`MenuCloseTraceFormatTests.cs`](https://github.com/sivatorov/ConfigurationManagement/blob/main/ConfigurationManagement.Tests/MenuCloseTraceFormatTests.cs) — новое имя журнала `trace_menuclose.json`, новый каталог (logs), резолюция путей. Полный набор `dotnet test` зелёный (**1831**, 0 не пройдено); кросс-сборка Linux (`dotnet build -p:BuildLinux=true`) без ошибок.

Версия **0.3.9.316** — исправление вошло в релиз **v0.3.9.316** (в [CHANGELOG](https://github.com/sivatorov/ConfigurationManagement/blob/main/CHANGELOG.md) изменения отражены секцией 0.3.9.316): [релиз v0.3.9.316](https://github.com/sivatorov/ConfigurationManagement/releases/tag/v0.3.9.316).