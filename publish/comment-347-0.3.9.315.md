Исправлено в версии **0.3.9.315** (Windows/WPF и Linux/Avalonia; issue #347 «Сказ о trace.json» — механизм отладочных флагов).

**Что было.**

`trace.json` рядом с настройками был JSONL-журналом событий меню/кликов, который писался **безусловно** — логи закрытых тикетов продолжали «капать» у всех пользователей (ширина колонок, события меню), а чтобы снять диагностику редиректов/входа портала (#323/#330/#334), приходилось ждать следующего обновления.

**Что сделано.**

1. **`trace.json` стал конфигом отладочных флагов**, а не журналом:
   ```json
   {
     "version": 1,
     "CM_COLUMNS": false,
     "CM_MENUCLICK": false,
     "CM_MENUCLOSE": false,
     "CM_REDIRECT": false
   }
   ```
   По умолчанию **все флаги выключены**. Файл создаётся сам при каждом старте рядом с настройками; ваш прежний журнал `trace.json` при первом запуске 0.3.9.315 будет переименован в **`trace_menuclose_legacy.json`** (история сохраняется).
2. **Флаг включается правкой файла без перезапуска приложения** — конфиг перечитывается при изменении файла (по времени последней записи). Просто замените `false` на `true` у нужного флага и сохраните файл.
3. **`CM_COLUMNS=true`** — включает диагностику колонок списка (стартовый дамп и изменения ширины). Раньше она включалась env `CM_COLUMNS_TRACE=1` — эта переменная осталась только как способ включения «на лету».
4. **`CM_MENUCLOSE=true`** — включает журнал событий меню/кликов дерева: `MenuOpened`/`MenuClosed`/`MenuClosedCursor`, клики (`MouseDown`/`MouseUp`, `PointerPressed`/`PointerReleased`), активации окна, `TryApply`/`Fallback`/`Dump500ms`/`EnsureStableStart`/`EnsureStable`, а также `LastPlainClick` и стабилизацию `clickBeforeMenuClose` (0.3.9.314). Журнал теперь пишется в отдельный файл **`trace_menuclose.jsonl`** (~1 МБ, круговое усечение). Сама **стабилизация выделения работает всегда** — под флаг ушли только записи в лог.
5. **`CM_MENUCLICK=true`** — отладка именно правого клика/открытия контекстного меню (по тексту тикета); в этой версии работает как псевдоним `CM_MENUCLOSE`.
6. **`CM_REDIRECT=true`** — включает INFO-диагностику редиректов и входа на portal.1c.ru (`[Updates] Редирект…`, `Вход запущен…`, инвентаризация cookie, POST-диагностика) — единый флаг для проверки #323/#330/#334. Итоговые предупреждения и ошибки (WARN/ERROR) пишутся в общий журнал **всегда**, даже при выключенном флаге.

**Как включить флаг (пошагово).**

1. Установите **0.3.9.315** и запустите приложение один раз (чтобы создался `trace.json`).
2. Найдите файл `trace.json`:
   - Windows: `%APPDATA%\ConfigurationManagement\`;
   - Linux: `~/.config/ConfigurationManagement/`;
   - в портативном режиме — каталог данных рядом с exe.
3. Откройте его в любом текстовом редакторе (Блокнот/gedit/VS Code) и замените `false` на `true` у нужного флага, например для журнала меню:
   ```json
   {
     "version": 1,
     "CM_COLUMNS": false,
     "CM_MENUCLICK": false,
     "CM_MENUCLOSE": true,
     "CM_REDIRECT": false
   }
   ```
4. Сохраните файл. Перезапуск **не нужен** — приложение подхватит флаг при следующем обращении (например, при следующем открытии/закрытии меню). Чтобы выключить — верните `false` и сохраните.

Быстрый способ без правки файла (только включить): запустить приложение с переменной окружения `CM_MENUCLOSE=1` (или `CM_REDIRECT=1`, `CM_COLUMNS=1`, прежнее `CM_COLUMNS_TRACE=1`). Выключить через окружение нельзя — только конфигом.

**Как проверить.**

- Для журнала меню: включите `CM_MENUCLOSE`, повторите сценарий правый клик → левый клик по другой строке 10+ раз, закройте приложение и пришлите **`trace_menuclose.jsonl`** (ожидаемая цепочка: `MouseDown`/`PointerPressed … snapshot=False` → `MenuClosed` → `MenuClosedCursor(overTreeRow=True)` → `LastPlainClick: target=…` → `EnsureStableStart(reason=clickBeforeMenuClose)` → проходы → `Dump500ms`). Если после правки файла записи не появились — сначала проверьте, что `trace.json` лежит именно в каталоге настроек, и перезапустите приложение.
- Для редиректов: включите `CM_REDIRECT` и выполните «Проверку обновлений» — в общем журнале приложения (`logs/`) появятся строки `[Updates] Редирект…`/`[Updates] Вход запущен…`. Ошибки входа видны и без флага.

**Тесты.** Новый [`TraceFlagsTests.cs`](https://github.com/sivatorov/ConfigurationManagement/blob/main/ConfigurationManagement.Tests/TraceFlagsTests.cs) (+8: дефолты при отсутствии файла, регистронезависимый разбор, битый JSON без исключения, игнорирование неизвестных флагов, сериализация дефолтов, миграция JSONL → legacy, конфиг не переименовывается, перечитывание по mtime); обновлён [`MenuCloseTraceFormatTests.cs`](https://github.com/sivatorov/ConfigurationManagement/blob/main/ConfigurationManagement.Tests/MenuCloseTraceFormatTests.cs) (+3: имя журнала `trace_menuclose.jsonl`, конфиг `trace.json`, резервное `trace_menuclose_legacy.json`; legacy `menuclose_trace.json` продолжает дописываться). Полный набор `dotnet test` зелёный (**1823**, 0 не пройдено); кросс-сборка Linux (`dotnet build -p:BuildLinux=true`) без ошибок.