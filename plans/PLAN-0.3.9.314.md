# PLAN 0.3.9.314 — Кластер B: #340 «Снятие выделения после контекстного меню» (девятая итерация)

- Режим: **architect** — план; код НЕ изменяется до одобрения.
- Текущая версия: **0.3.9.312**. Целевая версия кластера: **0.3.9.314**.
- Предыдущие попытки: 0.3.9.277/291/299/300/302/304/305/306/308/311 (восемь итераций исправления + две диагностических).

---

## 1. Диагноз (по второму реальному trace.json, комментарий 23, 2026-10-05T18:34Z, версия 0.3.9.311)

```
Activated: openMenusCount=0, timeSinceMenuCloseMs=1106531     ← меню давно не закрывалось
MouseDown: x=12.8, y=319.41, target=null, snapshot=False     ← клик мимо строки (промах)
MouseUp:   x=12.8, y=319.41, target=null, snapshot=False
MenuOpened: menuId=ContextMenu, isTreeMenu=True               ← правый клик открыл меню
MouseDown: x=119.2, y=281.81, target=966d0df4…, snapshot=False, redelivery=False, pinned=False
                                                              ← ЛЕВЫЙ КЛИК ПО СТРОКЕ ДОШЁЛ ДО ДЕРЕВА
MenuClosed: menuId=ContextMenu, isTreeMenu=True
MenuClosedCursor: x=119,2, y=281,8, overTreeRow=True          ← меню закрыто кликом по строке
Deactivated: openMenusCount=0, timeSinceMenuCloseMs=2172
```

Ключевые факты (отличие от предположений предыдущих итераций):
1. **MouseDown по строке приходит в дерево ДО закрытия меню** (`MenuClosed` через ~555 мс), а не повторной доставкой ПОСЛЕ (как предполагала стратегия 0.3.9.304–308). В этом окружении WPF не «проглатывает» клик попапом: штатная ветка `OnInfobaseTree_PreviewMouseLeftButtonDown` применяет выбор (строка становится активной) ещё при открытом меню.
2. **Снимок клика НЕ записан**: `snapshot=False`. `TryApplyTreeClickAfterMenuClosed` не записал снимок, потому что guard `Mouse.LeftButton != Pressed` — к моменту `OnContextMenuClosed` левая кнопка уже отпущена (MouseUp до дерева не дошёл, но глобальное состояние мыши — Released). Строки `TryApply: snapshot=…` в логе НЕТ.
3. **Повторной доставки нет**: `redelivery=False`, второй MouseDown после MenuClosed отсутствует.
4. **Стабилизация НЕ запущена**: предикат `ShouldStabilizeAfterMenuClose` требует «обычный клик в окне ~1,5 с ПОСЛЕ закрытия меню» либо «снимок присутствовал». Здесь клик был ДО закрытия меню и снимка нет → `EnsureSelectionStable` не вызвана ни разу (в логе нет `EnsureStableStart`/`Dump500ms`).
5. Итог: строка выбрана штатно, но после закрытия меню виртуализация (`VirtualizingStackPanel`, `Recycling`) перерабатывает контейнеры, `IsSelected` теряется, а восстанавливать некому — отсюда «сначала строка активна, через мгновение выделение пропадает».

**Вывод**: недостающее звено — стабилизация `IsSelected` для клика по строке, который произошёл НЕПОСРЕДСТВЕННО ПЕРЕД закрытием меню дерева (когда снимок по guard-цепочке не пишется, а повторной доставки нет).

---

## 2. Изменения

### 2.1 Чистая логика

Файл: [`Configuration Management/Services/BatchSelectionHelper.cs`](../Configuration%20Management/Services/BatchSelectionHelper.cs)

1. Новая константа `MenuClosePrecedingClickWindowMs = 500` — окно «клик предшествовал закрытию меню».
2. Новый чистый предикат:
   `ShouldStabilizeForClickPrecedingMenuClose(long lastPlainClickTick, long menuCloseTick, long windowMs)`:
   - `lastPlainClickTick > 0` (был обычный клик по строке дерева);
   - `menuCloseTick >= lastPlainClickTick` (закрытие не раньше клика);
   - `menuCloseTick - lastPlainClickTick <= windowMs` (клик непосредственно перед закрытием).
   Возвращает true — стабилизацию нужно запустить по цели последнего клика.
3. Старый предикат `ShouldStabilizeAfterMenuClose` НЕ меняется (используется штатным путём для кликов после закрытия).

### 2.2 WPF

4. [`Views/MainWindow.Events.cs`](../Configuration%20Management/Views/MainWindow.Events.cs), ветка обычного клика (`ApplySelection`, ~строка 781):
   - после `ApplySelection` сохранить «последний обычный клик по строке дерева» в поле `_lastPlainTreeClick = (Environment.TickCount, infobase, isPinnedSection)`.
   - Существующий вызов `EnsureSelectionStable` по `ShouldStabilizeAfterMenuClose` — оставить.
5. [`Views/MainWindow.Hotkeys.cs`](../Configuration%20Management/Views/MainWindow.Hotkeys.cs), `OnContextMenuClosed` (меню дерева):
   - если после `TryApplyTreeClickAfterMenuClosed` снимок клика НЕ записан (`_menuCloseClickSnapshot is null`) И `BatchSelectionHelper.ShouldStabilizeForClickPrecedingMenuClose(_lastPlainTreeClick?.Tick, _lastMenuCloseTick, …)` — запланировать `Dispatcher.BeginInvoke(Input)` → `EnsureSelectionStable(lastClick.Base, lastClick.IsPinned, reason: "clickBeforeMenuClose")`. Отложенный запуск нужен, чтобы компоновка после закрытия попапа устаканилась; сама стабилизация идемпотентна, доводит до сходимости (15 проходов / 1,5 с) и не трогает мультивыделение.
   - Очищать `_lastPlainTreeClick` после использования либо при следующем клике (перезаписывается).
   - Диагностика: при запоминании клика — запись `LastPlainClick: target=…, tick=…`; при запуске стабилизации причина `clickBeforeMenuClose` попадёт в `EnsureStableStart` (поле reason уже есть).

### 2.3 Avalonia/Linux (зеркально)

6. [`Views/MainWindow.Avalonia.Events.cs`](../Configuration%20Management/Views/MainWindow.Avalonia.Events.cs):
   - в `OnRowPointerPressed` (штатный выбор) сохранять `_lastPlainTreeClick`;
   - в `ContextMenu.IsOpenProperty.Changed` (закрытие меню дерева) — тот же предикат и `Dispatcher.UIThread.Post` → стабилизация.
7. Убедиться, что `EnsureSelectionStable`/`SelectRowByData` уже используют секцию строки (есть, 0.3.9.304/306) — изменений не требуется.

### 2.4 Запрос пользователю (комментарий после релиза)

8. Повторить сценарий 10+ раз (мультивыделение → ПКМ → левый клик по другой строке, и простой вариант без мультивыделения) на Windows и Linux; приложить `trace.json` — в нём должна появиться цепочка `MouseDown(snapshot=False)` → `MenuClosed` → `EnsureStableStart(reason=clickBeforeMenuClose)` → проходы → `Dump500ms`, а выделение оставаться на строке.

---

## 3. Тесты

Файл: [`ConfigurationManagement.Tests/BatchSelectionHelperTests.cs`](../ConfigurationManagement.Tests/BatchSelectionHelperTests.cs)

Новые сценарии:
1. `ShouldStabilizeForClickPrecedingMenuClose_WithinWindow_True` — клик за 200 мс до закрытия → true.
2. `ShouldStabilizeForClickPrecedingMenuClose_BeyondWindow_False` — клик за 3 с до закрытия → false.
3. `ShouldStabilizeForClickPrecedingMenuClose_NoClick_False` — lastPlainClickTick = 0 → false.
4. `ShouldStabilizeForClickPrecedingMenuClose_MenuClosedBeforeClick_False` — закрытие раньше клика → false.
5. Регресс существующих: `ShouldStabilizeAfterMenuClose_*`, `Stabilization_DoesNotTouchBatchSelection`, `DecideSelectionRestore_*`, `ShouldRetryRestore_*` — без изменений, полный набор зелёный.

Полный прогон `dotnet test`; кросс-сборка Linux `dotnet build -p:BuildLinux=true`.

---

## 4. Файлы

| Файл | Изменение |
|---|---|
| [`Configuration Management/Services/BatchSelectionHelper.cs`](../Configuration%20Management/Services/BatchSelectionHelper.cs) | Константа окна + предикат `ShouldStabilizeForClickPrecedingMenuClose` |
| [`Configuration Management/Views/MainWindow.Events.cs`](../Configuration%20Management/Views/MainWindow.Events.cs) | Сохранение `_lastPlainTreeClick` в ветке обычного клика |
| [`Configuration Management/Views/MainWindow.Hotkeys.cs`](../Configuration%20Management/Views/MainWindow.Hotkeys.cs) | Запуск стабилизации `clickBeforeMenuClose` в `OnContextMenuClosed` |
| [`Configuration Management/Views/MainWindow.Avalonia.Events.cs`](../Configuration%20Management/Views/MainWindow.Avalonia.Events.cs) | Зеркально для Avalonia |
| [`ConfigurationManagement.Tests/BatchSelectionHelperTests.cs`](../ConfigurationManagement.Tests/BatchSelectionHelperTests.cs) | 4 новых сценария предиката |
| [`Configuration Management/Configuration Management.csproj`](../Configuration%20Management/Configuration%20Management.csproj) | Версия → 0.3.9.314 (4 поля) |
| `CHANGELOG.md`, `README.md` | Запись о версии |

---

## 5. Риски

| Риск | Влияние | Митигация |
|---|---|---|
| Оконный стек (попапы, виртуализация) юнит-тестами не покрывается | Критерий «выделение остаётся» — только пользователь | Точная инструкция 10+ повторов; диагностика причины `clickBeforeMenuClose` в trace.json |
| Причина на самом деле в другом звене (например, модель/сброс IsSelected) | Выделение продолжит пропадать | Новая трасса покажет цепочку; следующая итерация по фактам |
| Стабилизация начнёт «перевыбирать» строку после обычных кликов | Ложное вмешательство | Предикат строго ограничен окном 500 мс до закрытия меню + идемпотентность `DecideSelectionRestore`; Ctrl/Shift исключены |
| Конфликт с #347 (общие файлы трассировки) | Нет — #347 идёт следующим кластером (0.3.9.315) | Порядок версий зафиксирован в сводном плане |

---

## 6. Критерии приёмки

1. `dotnet test` зелёный; кросс-сборка Linux без ошибок.
2. В trace.json после сценария: `MouseDown (snapshot=False)` → `MenuClosed` → `MenuClosedCursor(overTreeRow=True)` → `EnsureStableStart(reason=clickBeforeMenuClose)` → проходы → `Dump500ms`.
3. Ручная проверка пользователя: мультивыделение → ПКМ → левый клик по другой строке — строка остаётся активной (10+ повторов, Windows и Linux).
4. Релиз v0.3.9.314, комментарий в #340 после релиза (issue не закрывать).