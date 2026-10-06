Исправлено в версии **0.3.9.314** (Windows/WPF и Linux/Avalonia; девятая итерация по issue #340).

**Что было.**

По вашему второму `trace.json` (0.3.9.311, комментарий от 2026-10-05) стало видно недостающее звено:

```
MouseDown: x=119.2, y=281.81, target=966d0df4…, snapshot=False, redelivery=False, pinned=False
MenuClosed: menuId=ContextMenu, isTreeMenu=True
MenuClosedCursor: x=119,2, y=281,8, overTreeRow=True
```

1. **Левый клик по строке приходит в дерево ДО закрытия меню** (`MenuClosed` ~555 мс позже), а не
   повторной доставкой ПОСЛЕ (как предполагали предыдущие попытки). Штатная ветка
   `OnInfobaseTree_PreviewMouseLeftButtonDown` применяет выбор ещё при открытом меню — строка
   становится активной.
2. **Снимок клика НЕ записывается** (`snapshot=False`): к моменту `OnContextMenuClosed` левая кнопка
   уже отпущена (guard «кнопка нажата» не проходит), поэтому ни дедупликация «хвоста», ни fallback,
   ни штатная стабилизация по снимку не срабатывают.
3. **Повторной доставки нет** (`redelivery=False`) — второй MouseDown после закрытия меню не приходит.
4. Итог: строка выбрана штатно, но после закрытия меню виртуализация (`VirtualizingStackPanel`,
   `Recycling`) перерабатывает контейнеры, `IsSelected` теряется, а восстанавливать выбор некому —
   отсюда «сначала строка активна, через мгновение выделение пропадает».

**Что сделано** (план `plans/PLAN-0.3.9.314.md`):

1. **Новый чистый предикат**
   [`ShouldStabilizeForClickPrecedingMenuClose`](https://github.com/sivatorov/ConfigurationManagement/blob/main/Configuration%20Management/Services/BatchSelectionHelper.cs)
   + окно `MenuClosePrecedingClickWindowMs = 500` мс: обычный клик по строке был непосредственно
   (≤500 мс) перед закрытием контекстного меню дерева — стабилизацию нужно запускать по цели этого
   клика.
2. **«Последний обычный клик по строке»** запоминается в момент его применения: WPF — ветка
   `ApplySelection` в
   [`MainWindow.Events.cs`](https://github.com/sivatorov/ConfigurationManagement/blob/main/Configuration%20Management/Views/MainWindow.Events.cs)
   (единые часы `Environment.TickCount`, целевая база и секция строки); Avalonia/Linux — туннельная
   фаза `PointerPressed` в
   [`MainWindow.Avalonia.Events.cs`](https://github.com/sivatorov/ConfigurationManagement/blob/main/Configuration%20Management/Views/MainWindow.Avalonia.Events.cs).
3. **При закрытии меню дерева**, если снимок клика не записан и последний обычный клик был ≤500 мс до
   закрытия — запускается
   [`EnsureSelectionStable(reason="clickBeforeMenuClose")`](https://github.com/sivatorov/ConfigurationManagement/blob/main/Configuration%20Management/Views/MainWindow.Hotkeys.cs)
   отложенно (WPF — `Dispatcher.BeginInvoke(Input)`; Avalonia — `Dispatcher.UIThread.Post`), чтобы
   компоновка после закрытия попапа устаканилась. Стабилизация идемпотентна, доводит выбор до
   сходимости (15 проходов / 1,5 с), при перевыборе пользователем прекращается и **никогда не трогает
   мультивыделение** (Ctrl/Shift-клики исключены предикатом).
4. Существующая трассировка меню (`MenuOpened`/`MenuClosed`/`MenuClosedCursor`, безусловные записи
   кликов, `TryApply`/`Fallback`/`Dump500ms`/`EnsureStableStart`) сохранена; добавлены записи
   `LastPlainClick: target=…, tick=…` и причина `clickBeforeMenuClose` в стартовой записи
   стабилизации.

**Как проверить.**

1. Установите **0.3.9.314** (Windows или Linux) и запустите приложение. Рядом с настройками появится
   файл **`trace.json`** (создаётся сам, включать ничего не нужно).
2. Повторите сценарий **10+ раз подряд** с паузой ~2–3 секунды между повторами:
   - мультивыделение (Ctrl или Shift) → правый клик по любой строке (контекстное меню) → **левый клик
     по другой строке**;
   - и простой вариант без мультивыделения: правый клик по строке → меню → левый клик по другой
     строке.
3. **Закройте приложение** и пришлите полный `trace.json`. Ожидаемая цепочка для каждого повтора:
   ```
   MouseDown … (или PointerPressed) target=<id>, snapshot=False → MenuClosed →
   MenuClosedCursor(overTreeRow=True) → LastPlainClick: target=<id>, tick=…
   → EnsureStableStart(target=<id>, reason=clickBeforeMenuClose)
   → проходы EnsureStable: pass=1… → Dump500ms
   ```
   Выделение при этом должно оставаться на строке, по которой кликнули (не пропадать «через
   мгновение»).
4. Проверьте, что закрытие меню по ESC и выбор пункта меню по-прежнему НЕ меняют выделение, а
   Ctrl/Shift-клики при открытом меню работают как раньше (мультивыделение меняется штатно).

**Тесты.** +4 новых сценария предиката в
[`BatchSelectionHelperTests.cs`](https://github.com/sivatorov/ConfigurationManagement/blob/main/ConfigurationManagement.Tests/BatchSelectionHelperTests.cs)
(клик за 200 мс до закрытия → true; граница окна 500 мс → true; за 3 с/за окном → false; без клика →
false; закрытие раньше клика → false); регресс существующих (идемпотентность, нетронутое
мультивыделение) без изменений. Полный набор `dotnet test` зелёный (**1813**, 0 не пройдено);
кросс-сборка Linux (`dotnet build -p:BuildLinux=true`) без ошибок. Поведение оконного стека
(попапы, виртуализация) юнит-тестами не покрывается — ждём подтверждения на вашей машине.