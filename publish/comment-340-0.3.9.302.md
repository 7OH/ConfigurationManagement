Исправлено в версии **0.3.9.302**.

**Что было.**

Проблема сохранялась после четырёх попыток (0.3.9.277, 0.3.9.291, 0.3.9.299, 0.3.9.300). Причина — в самом паттерне «применить выбор отложенно + гасить повторную доставку клика»: выбор применялся через `Dispatcher.BeginInvoke` ПОСЛЕ обработки текущего сообщения, между «строка стала активной» и «выбор применён» оставалось окно, в которое вмешивались разметка/виртуализация; дедупликация «хвоста» клика была привязана к `DateTime.UtcNow`, а не к штампу времени события мыши, и при задержке/сдвиге позиции могла пропустить «хвост» — выбор применялся дважды. Дополнительно [`SelectTreeRowByData`](Configuration%20Management/Views/MainWindow.Tree.cs) не учитывал секцию строки — выделение могло «перепрыгивать» на дубль («Закреплённые» vs обычный список).

**Что сделано.**

Новый подход — никакой отложенной работы: выбор строки, которой закрыли контекстное меню, применяется **ровно один раз и синхронно** в самой ранней детерминированной точке по данным клика; повторная доставка «хвоста» гасится.

1. [`Views/MainWindow.Hotkeys.cs`](Configuration%20Management/Views/MainWindow.Hotkeys.cs): в `TryApplyTreeClickAfterMenuClosed` отложенное применение через `Dispatcher.BeginInvoke` удалено — в момент закрытия меню выбор применяется синхронно по данным клика (`ClearBatchSelection()` + `SelectTreeRowByData(...)`).
2. [`Views/MainWindow.Events.cs`](Configuration%20Management/Views/MainWindow.Events.cs): повторная доставка «хвоста» того же клика гасится снимком по штампу времени события мыши (`MouseButtonEventArgs.Timestamp`, единые часы `Environment.TickCount` на обеих платформах) и позиции; допуски остались настраиваемыми в [`BatchSelectionHelper.IsSameClick`](Configuration%20Management/Services/BatchSelectionHelper.cs).
3. [`Views/MainWindow.Tree.cs`](Configuration%20Management/Views/MainWindow.Tree.cs): `SelectTreeRowByData` учитывает секцию клика («Закреплённые» vs обычный список) — контейнер ищется в той же секции, выделение не перескакивает на дубль.
4. Linux/Avalonia ([`MainWindow.Avalonia.Events.cs`](Configuration%20Management/Views/MainWindow.Avalonia.Events.cs)): выбор применяется синхронно на первом `PointerPressed`, «хвост» гасится по `PointerEventArgs.Timestamp`; поведение идентично WPF.

**Тесты** ([`BatchSelectionHelperTests.cs`](ConfigurationManagement.Tests/BatchSelectionHelperTests.cs)): дедупликация по штампу времени события, выбор по данным с учётом секции строки.

**Как проверить:** обновитесь до **0.3.9.302**; выделите несколько строк → правый клик (контекстное меню) → левый клик по другой строке: новая строка остаётся активной, набор снят, ничего не пропадает «через мгновение». Простой кейс: контекстное меню по строке → клик по другой строке → строка остаётся активной. Закрытие меню по ESC и выбор пункта меню не меняют выделение; проверено на Windows/WPF и Linux/Avalonia.