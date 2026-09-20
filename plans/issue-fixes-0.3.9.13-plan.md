# Технический план исправлений — релиз 0.3.9.13

- Репозиторий: `sivatorov/ConfigurationManagement`
- Версия-цель: **0.3.9.13** (предыдущая — 0.3.9.12)
- Рабочая директория: `f:/ya/Yandex.Disk/h/Configuration_Management`
- Основание: `plans/issues-analysis-2026-09-20.md`, `issues_selected.md`, `issues_open_latest.json`
- Правило отбора: все три issues подходят (последний комментарий — от пользователя `7OH`), источник истины — последние комментарии `7OH`.
- Важно: правки для **обеих платформ** (WPF/Windows и Avalonia/Linux). Пользователь работает на Windows/WPF, поэтому WPF-ветка приоритетна и для #261 нужен перенос Avalonia-подхода в WPF.
- Принцип: устранять корневую причину, а не замазывать симптом; избегать лишних пересчётов раскладки и пересинхронизаций.

---

## Общее правило реализации

1. Каждую правку вносить парой: WPF-файл и его Avalonia-аналог (`*.Avalonia.*.cs`), где это применимо.
2. Не менять поведение при отсутствии изменений: чем меньше лишних `UpdateLayout()`, `BringIntoView`, `ReorderGridColumns`, `SyncHeaderWidthWithList` — тем меньше рывков и мусора.
3. Любая правка прокрутки должна сопровождаться замером на живом окне (см. критерии проверки каждого issue).
4. Обновить `VersionInfo.cs` до `0.3.9.13`, добавить `_release/0.3.9.13.md` и строку в `CHANGELOG.md`.
5. Перед закрытием issue — подтверждение `7OH` (по правилам проекта закрытие вручную после подтверждения автора).

---

## #261 — «ESC и открытая подсказка»

### Суть
При открытой подсказке первый ESC должен закрывать подсказку, повторный — сворачивать/закрывать окно.
В `0.3.9.12` исправление внесено **только в Avalonia**, WPF-ветка не тронута; пользователь на Windows/WPF — баг остаётся.

### Файлы для изменения (WPF, приоритетно)
- [`Views/MainWindow.Hotkeys.cs`](Configuration%20Management/Views/MainWindow.Hotkeys.cs) — переписать `CloseOpenToolTips()` и добавить трекинг открытого тултипа.
- [`Views/MainWindow.xaml.cs`](Configuration%20Management/Views/MainWindow.xaml.cs) — регистрация класс-обработчиков `ToolTip.OpenedEvent`/`ClosedEvent` в конструкторе.

### Файлы для изменения (Avalonia — паритет/проверка)
- [`Views/MainWindow.Avalonia.cs`](Configuration%20Management/Views/MainWindow.Avalonia.cs), [`Views/MainWindow.Avalonia.Hotkeys.cs`](Configuration%20Management/Views/MainWindow.Avalonia.Hotkeys.cs) — механизм уже внесён в 0.3.9.12 (класс-обработчик `ToolTip.IsOpenProperty.Changed` + `_openToolTipOwner` + `ToolTip.SetIsOpen`). Здесь только убедиться, что он корректен и не регрессировал.

### Что именно менять
Перенести Avalonia-подход в WPF детерминированным способом:

1. В `MainWindow` (WPF) добавить поле:
   `private ToolTip? _openToolTip;`

2. В конструкторе (рядом с прочими подписками) зарегистрировать класс-обработчики на **тип `ToolTip`** (аналог `AddClassHandler<Control>` в Avalonia):
   ```csharp
   EventManager.RegisterClassHandler(typeof(ToolTip), ToolTip.OpenedEvent,
       new RoutedEventHandler(OnToolTipOpened));
   EventManager.RegisterClassHandler(typeof(ToolTip), ToolTip.ClosedEvent,
       new RoutedEventHandler(OnToolTipClosed));
   ```
   - `OnToolTipOpened`: `_openToolTip = sender as ToolTip;`
   - `OnToolTipClosed`: `if (ReferenceEquals(_openToolTip, sender)) _openToolTip = null;`

3. Переписать `CloseOpenToolTips()`:
   - Основной путь: если `_openToolTip is { } tip` → `tip.IsOpen = false; _openToolTip = null; return true;`
   - Оставить текущий обход дерева + `CloseToolTipByMouseOrFocus()` как резервный путь (harmless), чтобы не потерять покрытие для тултипов, не отражённых в `_openToolTip`.
   - Существующую логику в `TryCloseToolTip` (обход `ToolTipService`) можно сохранить в резервном пути.

4. Обработчик ESC в `Window_PreviewKeyDown` уже вызывает `CloseOpenToolTips()` первым (строка ~268) — порядок не менять.

### Порядок действий
1. Добавить поле `_openToolTip`.
2. Зарегистрировать класс-обработчики в конструкторе WPF.
3. Добавить обработчики `OnToolTipOpened`/`OnToolTipClosed`.
4. Переписать `CloseOpenToolTips()` (основной путь через `_openToolTip`, резерв — прежний).
5. Собрать WPF-версию и проверить.

### Как избежать побочных эффектов
- Класс-обработчик на `typeof(ToolTip)` процесс-глобален, но `MainWindow` один — конфликтов нет; захвата экземпляра в статике нет (утечки не будет).
- Закрываем только `ToolTip`, не трогаем `ContextMenu`/попапы других типов.
- Оставить guard для поля ввода `InlineTagBox` (первый ESC там — отмена ввода, не закрытие тултипа/окна).
- Модальные окна не затрагиваются: их ESC обрабатывает `ModalWindowBase`/система, обработчик главного окна на них не срабатывает в момент их фокуса.

### Критерии готовности (проверка)
- Открыть любую подсказку в главном окне → первый ESC закрывает только её, окно на месте.
- Повторный ESC сворачивает окно в трей (если включено).
- Проверить тултипы: у строк базы (в т.ч. при рециклинге/скролле), заголовка колонок, правой панели, элементов верхней панели.
- Обе платформы: Windows/WPF и Linux/Avalonia.
- Подтверждение `7OH`.

---

## #255 — «Проблемы построения списка баз»

### Суть
Рывки/прыжки списка и подсветки, изменение размера полосы прокрутки, перескок вниз при долистывании до нижней папки, ложный горизонтальный скролл при запуске, рост памяти при длительной прокрутке. Диагноз (ksv47): многократный пересчёт раскладки на каждую материализацию строки при `ScrollUnit="Pixel"` + `VirtualizationMode="Recycling"` + переменной высоте строк.

### Файлы для изменения (WPF)
- [`Views/MainWindow.Columns.cs`](Configuration%20Management/Views/MainWindow.Columns.cs) — `OnInfobaseRowGrid_Loaded`, `OnGroupRowGrid_Loaded`, `ReorderGridColumns`, `ApplyColumnOrder`, `GetTreeScrollContentPresenter`, `UpdateTreeMinWidth`.
- [`Views/MainWindow.Events.cs`](Configuration%20Management/Views/MainWindow.Events.cs) — обработчики Loaded сеток строк (дополнительно к Columns.cs), стартовые выравнивания.
- [`Views/MainWindow.Scroll.cs`](Configuration%20Management/Views/MainWindow.Scroll.cs) — `GetTreeScrollViewer`/`GetTreeScrollContentPresenter` (кэш), `ScrollListByWheel`, `OnTreeScroll_ScrollChanged`.
- [`Views/MainWindow.Tree.cs`](Configuration%20Management/Views/MainWindow.Tree.cs) — `ScrollSelectedIntoView`, `ReferenceRowHeight`, `OnMainTree_RequestBringIntoView`.
- [`Controls/LeveledTreeView.cs`](Configuration%20Management/Controls/LeveledTreeView.cs) — кэш внутреннего `ScrollContentPresenter` через `OnApplyTemplate`.
- [`Views/MainWindow.xaml`](Configuration%20Management/Views/MainWindow.xaml) — только если примем решение по стабилизации Extent (п. 6).

### Файлы для изменения (Avalonia — паритет)
- [`Views/MainWindow.Avalonia.Columns.cs`](Configuration%20Management/Views/MainWindow.Avalonia.Columns.cs),
  [`Views/MainWindow.Avalonia.Events.cs`](Configuration%20Management/Views/MainWindow.Avalonia.Events.cs),
  [`Views/MainWindow.Avalonia.Scroll.cs`](Configuration%20Management/Views/MainWindow.Avalonia.Scroll.cs),
  [`Views/MainWindow.Avalonia.Tree.cs`](Configuration%20Management/Views/MainWindow.Avalonia.Tree.cs),
  [`Controls/LeveledTreeView.Avalonia.cs`](Configuration%20Management/Controls/LeveledTreeView.Avalonia.cs).

### Что именно менять (корневые причины)

1. **Полностью исключить работу по колонкам на каждую материализацию строки** (сейчас ранний выход есть, но перед ним всё равно строятся `BuildColumnLayout()` + массивы/`List`/`HashSet` и обход детей — мусор и CPU на каждую строку, источник роста gen2 и рывков):
   - В `OnInfobaseRowGrid_Loaded`/`OnGroupRowGrid_Loaded`: пропускать `ReorderGridColumns` целиком, если колонки сетки уже приведены к порядку. Использовать attached-флаг «порядок применён» на самом `Grid` (при `Recycling` экземпляр сетки переиспользуется, флаг сохраняется).
   - В `ApplyColumnOrder()` (изменение порядка колонок в настройках): сбросить флаг на всех найденных сетках (`FindRowGrids`) и вызвать `ReorderGridColumns` на существующих строках.
   - Убедиться, что при старте порядок применяется один раз для каждой строки (через флаг), а не на каждый `Loaded`.

2. **Исправить `ReferenceRowHeight()` fallback** — вероятная причина «перескока вниз» у нижней папки:
   - Сейчас при отсутствии реализованных строк баз возвращается `ViewportHeight` (`MainWindow.Tree.cs`, строка ~448) → шаг прокрутки = целый вьюпорт за клавишу → «прыжок вниз».
   - Fallback должен быть **стабильной высотой одной строки** (константа высоты заголовка группы/строки базы, напр. 26–34 px), а не высотой вьюпорта. Либо брать ActualHeight реально реализованного контейнера группы.
   - Проверить путь `bottom = top + ReferenceRowHeight()` для узла-группы: цель — показать заголовок группы (одну строку), не поддерево.

3. **Убрать `item.UpdateLayout()` из горячего пути `ScrollSelectedIntoView`** (строка ~393):
   - При автоповторе клавиши «вниз» синхронная раскладка выполняется десятки раз за проход → «рывки» и продолжение после отпускания.
   - Вызывать `UpdateLayout()` только когда контейнер только что реализован/не измерен (флаг или сравнение `IsMeasureValid`/`IsArrangeValid`), либо полагаться на меры, полученные в `DeferredScrollSelectedIntoView`.

4. **Кэшировать `ScrollContentPresenter`** (аналог кэша `ScrollViewer`, уже есть в `MainWindow.Scroll.cs`):
   - В `GetTreeScrollContentPresenter()` не делать `FindVisualChild<ScrollContentPresenter>` на каждый вызов; кэшировать рядом с `_treeScrollViewer`, сбрасывать на `Unloaded`.
   - В `LeveledTreeView` реализовать захват внутреннего `ScrollContentPresenter` в `OnApplyTemplate` (штатно, без полных обходов).

5. **Ложный горизонтальный скролл при запуске**:
   - `UpdateTreeMinWidth()` суммирует ширины **всех** `ColumnDefinition` заголовка. Если скрытые колонки всё ещё имеют ненулевую ширину в определениях, сумма переоценивает реальную потребность → появляется ненужная горизонтальная полоса.
   - Суммировать только **видимые** колонки (учесть состояние видимости через конвертер/`ActualWidth` скрытых = 0), оставляя жёсткую границу по факту отображаемых колонок.
   - Продолжить сброс горизонтального offset дерева в ноль (механизм уже есть в `MainWindow.Scroll.cs`).

6. **Изменение размера полосы прокрутки при листании** — корень в `ScrollUnit="Pixel"` + переменной высоте строк + `Recycling`: Extent/Viewport переоцениваются по реализованным детям на каждом шаге.
   - Первично: убедиться, что нет обратной связи «прокрутка → правка ширин/MinWidth → раскладка → ScrollChanged». Текущий gate (реакция только на `ViewportWidthChange`/`ViewportHeightChange`, не на `ExtentWidthChange`) уже это закрывает — замерить, что триггер не возобновляется.
   - Если симптом остаётся: рассмотреть стабилизацию Extent — **фиксированную высоту строк** либо `VirtualizingPanel.ScrollUnit="Item"` (рекомендация ksv47). Это меняет внешний вид строк с тегами — решение согласовать с автором. Оформить отдельным шагом с проверкой на живом окне.

7. **`OnMainTree_RequestBringIntoView`** (`e.Handled = true` безусловно):
   - Определить замером/логированием, гасит ли `Handled` штатную прокрутку внутреннего `ScrollViewer`. Если внутренняя прокрутка всё равно выполняется — на одно нажатие приходятся две конкурирующие прокрутки (штатная + ручная в `ScrollSelectedIntoView`).
   - Если подтвердится дублирование — разделить механизмы: либо полностью довериться ручной прокрутке и не давать штатной срабатывать, либо наоборот. Оставить один источник истины.

### Рост памяти
- Первопричина — мусор (gen2), а не утечка: пересоздание сотен `BindingExpression` на рецикл строки + по-строчные выделения колонок (п. 1) + принудительные раскладки (п. 3).
- Правки пп. 1, 3, 4 существенно снижают выделения. Подтвердить отсутствие утечки замером тренда после полной GC.

### Порядок действий
1. Сначала устранить по-строчный `ReorderGridColumns` (п. 1) — самый большой источник CPU/мусора.
2. Исправить `ReferenceRowHeight()` fallback (п. 2) — перескок вниз у папок.
3. Убрать `UpdateLayout()` из горячего пути (п. 3) и закэшировать presenter (п. 4).
4. Поправить `UpdateTreeMinWidth` по видимым колонкам (п. 5).
5. Замерить полосу прокрутки (п. 6) и `RequestBringIntoView` (п. 7); применить стабилизацию Extent, если требуется.
6. Прогнать обе платформы.

### Как избежать побочных эффектов
- Изменение порядка колонок в настройках должно по-прежнему немедленно применять новый порядок к заголовку и всем существующим строкам (сброс флага в `ApplyColumnOrder`).
- Смена компактного режима, поиск/фильтр, раскрытие групп продолжают вызывать `QueueHeaderAlign` — не убирать эти точки.
- Не менять видимое поведение по умолчанию без подтверждения автора (п. 6 — фикс. высота/`ScrollUnit=Item`).
- Убедиться, что сброс горизонтали не ломает горизонтальную прокрутку заголовка (`DbHeaderScroll`), которая ведёт внешний заголовок.

### Критерии готовности (проверка)
- Плавная прокрутка мышью и клавишами, без рывков и «прыжков» подсветки.
- Размер вертикальной полосы прокрутки стабилен во время листания.
- Нет «перескока вниз» при долистывании до групп/папок.
- Нет ложного горизонтального скролла при запуске.
- Память (частная, по диспетчеру задач): непрерывный скролл ~30 сек не растёт неограниченно; после GC тренд возвращается к базе.
- Подтверждение `7OH`.

---

## #252 — «Прокрутка списка после правки свойств»

### Суть
После сохранения свойств («Да») список скачет вверх/вниз со смещением; при закрытии без сохранения («Нет») список тоже скачет. Если ничего ключевого не изменилось — позицию пересчитывать вообще не нужно.

### Файлы для изменения (WPF)
- [`Views/MainWindow.Scroll.cs`](Configuration%20Management/Views/MainWindow.Scroll.cs) — `RememberTreeScroll`, `RestoreTreeScrollAfterCancel`, `RestoreTreeScrollAfterRebuild`.
- [`Views/MainWindow.Tree.cs`](Configuration%20Management/Views/MainWindow.Tree.cs) — `RevealAndSelectAfterRebuild`, `QueueScrollSelectedIntoView`/`DeferredScrollSelectedIntoView`.
- [`ViewModels/MainViewModel.Commands.cs`](Configuration%20Management/ViewModels/MainViewModel.Commands.cs) — место вызова `TreeModalOpening`/`TreeModalClosed` (если нужен guard «ничего не изменилось»).

### Файлы для изменения (Avalonia — паритет)
- [`Views/MainWindow.Avalonia.Scroll.cs`](Configuration%20Management/Views/MainWindow.Avalonia.Scroll.cs),
  [`Views/MainWindow.Avalonia.Tree.cs`](Configuration%20Management/Views/MainWindow.Avalonia.Tree.cs),
  [`ViewModels/MainViewModel.Avalonia.cs`](Configuration%20Management/ViewModels/MainViewModel.Avalonia.cs).

### Корневые причины и что менять

1. **«Нет»: гонка восстановления с отложенной прокруткой** (главная причина, что «Нет» по-прежнему скачет):
   - WPF `RestoreTreeScrollAfterCancel` выполняется **синхронно** внутри `TreeModalClosed` (в отличие от Avalonia, где используется `Dispatcher.UIThread.Post(..., Background)`).
   - До этого в очереди мог стоять `DeferredScrollSelectedIntoView` (поставленный при клике, которым открыли свойства). Он исполняется позже (priority `Loaded`) и перетирает восстановленную позицию → «скачет вверх/вниз» после «Нет».
   - **Фикс WPF:**
     - В `RememberTreeScroll()` (при `TreeModalOpening`) сбросить отложенную прокрутку: `_scrollQueued = false; _scrollTargetData = null;`.
     - `RestoreTreeScrollAfterCancel()` выполнять отложенно, позже любых отложенных прокруток: `Dispatcher.BeginInvoke(..., DispatcherPriority.ApplicationIdle)` (или `Loaded`), как в Avalonia.
   - В Avalonia убедиться, что на открытие модального окна также снимаются отложенные прокрутки, если есть аналогичная очередь.

2. **«Да»: не пересчитывать позицию, когда ничего ключевого не изменилось**:
   - В `RestoreTreeScrollAfterRebuild` уже есть ветка `groupUnchanged` → восстановление точного offset. Добавить условие: если группа не изменилась **и** запомненная верхняя видимая строка (`_treeScrollAnchorData`) после пересборки всё ещё является верхней видимой строкой (`GetTopVisibleRowData()`), **не выполнять** `ScrollToVerticalOffset` и не делать `BringIntoView` — позиция уже корректна (инвариант пользователя «просто не надо позиции пересчитывать»).
   - Убедиться, что в `RevealAndSelectAfterRebuild` при `restoreScroll:false` (первый, Loaded проход) не вызывается ничего, что двигало бы позицию (уже так), а на `restoreScroll:true` (ApplicationIdle) `RestoreTreeScrollAfterRebuild` выполняется **после** всех `BringIntoView`.

3. **Избегать принудительной раскладки**:
   - `RestoreTreeScrollAfterCancel`/`RestoreTreeScrollAfterRebuild` вызывают `MainTree.UpdateLayout()` — это форсирует полную раскладку в середине последовательности восстановления и может само сдвигать позицию.
   - Вызывать `UpdateLayout()` только если реально нужно измерить (например, в `DeferredScrollSelectedIntoView`), а в чистых restore-путях либо не вызывать, либо отложить до ApplicationIdle.

4. **«Нет» при нулевых изменениях**:
   - Если на момент закрытия окна свойств правок не было (dialog.Result идентичен исходному по позиционно-значимым полям: `Group`, видимость/отборы), событие `TreeModalClosed` может вообще не инициировать позиционирование (пользователь прямо просит не пересчитывать позицию). Решение — либо guard в ViewModel (не инвокать `TreeModalClosed` без изменений), либо в `RestoreTreeScrollAfterCancel` сравнивать сохранённый offset с текущим и не двигать, если они совпадают с допуском.

### Порядок действий
1. Снять отложенную прокрутку при `TreeModalOpening` (WPF; проверить Avalonia).
2. Перевести `RestoreTreeScrollAfterCancel` на отложенное исполнение позже отложенных прокруток (паритет с Avalonia).
3. Добавить в `RestoreTreeScrollAfterRebuild` guard «ничего не изменилось → не позиционировать».
4. Убрать/минимизировать `UpdateLayout()` из restore-путей.
5. Прогнать обе платформы; собрать 0.3.9.13.

### Как избежать побочных эффектов
- Не сломать восстановление выделения/фокуса после пересборки: guard «ничего не изменилось» касается только прокрутки, не `ApplySelection`/фокуса.
- Отмена отложенной прокрутки на `TreeModalOpening` не должна ломать обычную навигацию стрелками (очередь переставляется при каждом выборе).
- Восстановление позиции при «Да» должно по-прежнему работать при реальном изменении группы/видимости строк (ветка по `_treeScrollAnchorData`).

### Критерии готовности (проверка)
- Сохранение свойств («Да»): список остаётся на прежней позиции, строка на месте, без скачков вверх/вниз.
- Закрытие без сохранения («Нет») без изменений: позиция не меняется, список не скачет.
- «Нет» после изменения только не влияющих на позицию полей: позиция сохраняется.
- Обе платформы: WPF и Avalonia.
- Подтверждение `7OH`.

---

## Зависимости и порядок внедрения по проекту

1. #255 (пп. 1–4: колонки, ReferenceRowHeight, UpdateLayout, кэш presenter) — фундамент, влияет и на #252.
2. #252 (снятие очереди прокрутки при открытии окна свойств + отложенный restore + guard).
3. #261 (перенос Avalonia-подхода в WPF).
4. Сборка обеих платформ, ручные проверки по критериям, `VersionInfo.cs` → 0.3.9.13, `_release/0.3.9.13.md`, `CHANGELOG.md`.
5. Опубликовать сборки, запросить подтверждение `7OH` по всем трём issues, закрыть после подтверждения.

## Сводка изменений по файлам

| Issue | Файл (WPF) | Файл (Avalonia) | Суть |
|---|---|---|---|
| #261 | `Views/MainWindow.Hotkeys.cs`, `Views/MainWindow.xaml.cs` | `Views/MainWindow.Avalonia.Hotkeys.cs` (проверка) | Трекинг открытого тултипа + детерминированное закрытие по ESC |
| #255 | `Views/MainWindow.Columns.cs`, `Events.cs`, `Scroll.cs`, `Tree.cs`, `Controls/LeveledTreeView.cs`, `Views/MainWindow.xaml` | `Views/MainWindow.Avalonia.{Columns,Events,Scroll,Tree}.cs`, `Controls/LeveledTreeView.Avalonia.cs` | Исключение по-строчной пересборки колонок, стабильный fallback высоты, убрать UpdateLayout, кэш presenter, мин-ширина по видимым колонкам, стабилизация Extent |
| #252 | `Views/MainWindow.Scroll.cs`, `Tree.cs`, `ViewModels/MainViewModel.Commands.cs` | `Views/MainWindow.Avalonia.Scroll.cs`, `...Tree.cs`, `ViewModels/MainViewModel.Avalonia.cs` | Снятие отложенной прокрутки при открытии окна свойств, отложенный restore при «Нет», guard «ничего не изменилось» |