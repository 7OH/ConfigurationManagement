Исправлено в версии **0.3.9.316** (Windows/WPF и Linux/Avalonia; десятая итерация по issue #340 «Снятие выделения после контекстного меню»).

**Что было.**

Разбор вашего нового лога на 0.3.9.315 (07:55:46Z) показал сценарий, в котором ни один штатный путь стабилизации не срабатывает:

```
MouseDown / LastPlainClick → MenuOpened → MenuClosed → MenuClosedCursor(overTreeRow=True)
```

1. **Второго `MouseDown` в логе нет** — клик, закрывший меню, был проглочен попапом и до дерева не дошёл (повторной доставки нет).
2. **`EnsureStableStart(reason=clickBeforeMenuClose)` не запустился**: клик, которым начата цепочка, был за ~1,8 с до закрытия меню — за пределами окна 500 мс предиката `ShouldStabilizeForClickPrecedingMenuClose`.
3. Итог: строка под курсором остаётся без выделения, а восстанавливать выбор некому.

**Что сделано.**

1. **Новый fallback-путь `MenuClosedOverRow`**: при закрытии меню дерева (`MenuClosed`) с курсором **над строкой дерева** (`MenuClosedCursor(overTreeRow=True)`) и **недавним обычным кликом без модификаторов** (окно `MenuCloseRecentMouseActivityWindowMs = 2000` мс) выполняется hit-test строки под курсором и восстановление выделения — [`EnsureSelectionStable(reason="menuClosedOverRow")`](https://github.com/sivatorov/ConfigurationManagement/blob/main/Configuration%20Management/Views/MainWindow.Hotkeys.cs) (Avalonia — симметрично в [`MainWindow.Avalonia.Events.cs`](https://github.com/sivatorov/ConfigurationManagement/blob/main/Configuration%20Management/Views/MainWindow.Avalonia.Events.cs)). Строка под курсором и есть цель проглоченного клика.
2. **Новый чистый предикат** [`ShouldRestoreSelectionForRowUnderCursor`](https://github.com/sivatorov/ConfigurationManagement/blob/main/Configuration%20Management/Services/BatchSelectionHelper.cs) решает, нужно ли восстановление. Исключения: курсор над пунктом меню (`overMenuItem=True` — выбор пункта строку не меняет), наличие снимка клика (работают штатные пути snapshot/A/C), отсутствие недавнего обычного клика (закрытие по ESC/программно), клик давно (за окном) или после закрытия (новое действие — обрабатывается штатно), а также **мультивыделение**: evidence — только обычный клик БЕЗ Ctrl/Shift, набор «для выделенных» не затрагивается.
3. **Прежние пути сохранены** (`TryApplyTreeClickAfterMenuClosed`, `clickBeforeMenuClose`, `ShouldStabilizeAfterMenuClose`) — новый fallback дополняет их, а не заменяет.
4. **Трассировка** под флагом `CM_MENUCLOSE` (журнал теперь — `logs/trace_menuclose.json`, см. #347): запись `MenuClosedOverRow: ran=true, target=…, pinned=…, selected=…, precedingClickTick=…`.

Ожидаемая цепочка в логе для каждого повтора:

```
MenuClosed → MenuClosedCursor(overTreeRow=True) → LastPlainClick: target=…, tick=…
→ MenuClosedOverRow: ran=true, target=… → EnsureStableStart(target=…, reason=menuClosedOverRow)
→ проходы EnsureStable: pass=1… → Dump500ms
```

**Как проверить.**

1. Установите **0.3.9.316** (Windows или Linux) и запустите приложение.
2. Включите флаг `CM_MENUCLOSE` в `trace.json` (каталог профиля, рядом с `settings.json` — подробности в #347); перезапуск не нужен.
3. Повторите сценарий **10+ раз подряд** с паузой ~2–3 секунды между повторами:
   - правый клик по строке (контекстное меню) → **левый клик по другой строке**;
   - вариант с мультивыделением: Ctrl/Shift → правый клик → левый клик по другой строке.
4. **Закройте приложение** и пришлите файл **`logs/trace_menuclose.json`** (Windows: `%APPDATA%\ConfigurationManagement\logs\`, Linux: `~/.config/ConfigurationManagement/logs/`). Ожидание: строка, по которой кликнули, остаётся активной (выделение не пропадает «через мгновение»), а в логе присутствует цепочка с `MenuClosedOverRow: ran=true` → `EnsureStableStart(… reason=menuClosedOverRow)`.

**Тесты.** +8 новых сценариев в [`BatchSelectionHelperTests.cs`](https://github.com/sivatorov/ConfigurationManagement/blob/main/ConfigurationManagement.Tests/BatchSelectionHelperTests.cs) для предиката `ShouldRestoreSelectionForRowUnderCursor`: курсор над строкой + недавний обычный клик → true (в т.ч. граница окна 2 с); курсор над пунктом меню → false; курсор вне строки → false; без недавнего клика → false; Ctrl/Shift-клик (мультивыделение) → false; клик давно (за окном) → false; снимок клика присутствует → false; клик после закрытия → false; регресс существующих сценариев (идемпотентность, стабилизация не трогает набор) без изменений. Полный набор `dotnet test` зелёный (**1846**, 0 не пройдено); кросс-сборка Linux (`dotnet build -p:BuildLinux=true`) без ошибок. Поведение оконного стека (попапы, виртуализация) юнит-тестами не покрывается — ждём подтверждения на вашей машине.

Версия **0.3.9.316** — исправление вошло в релиз **v0.3.9.316** (в [CHANGELOG](https://github.com/sivatorov/ConfigurationManagement/blob/main/CHANGELOG.md) изменения отражены секцией 0.3.9.316): [релиз v0.3.9.316](https://github.com/sivatorov/ConfigurationManagement/releases/tag/v0.3.9.316).