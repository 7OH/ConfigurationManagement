Добавлено в версии **0.3.9.116** (Windows/WPF и Linux/Avalonia).

**Что было.**

Назначение тега для мультивыделения («Для выделенных (N)…» → «Назначить тег») открывало
простое текстовое окно с одним полем ввода — можно было задать только один тег, выбора
из существующих не было.

**Как стало.**

Новое окно-чеклист [`Views/TagPickWindow.xaml`](Configuration%20Management/Views/TagPickWindow.xaml)
(WPF, + code-behind [`Views/TagPickWindow.xaml.cs`](Configuration%20Management/Views/TagPickWindow.xaml.cs))
и [`Views/TagPickWindow.Avalonia.cs`](Configuration%20Management/Views/TagPickWindow.Avalonia.cs)
(Linux/Avalonia, на `ModalWindowBase`):

- список **существующих тегов** с флажками — можно отметить сразу несколько (мультивыбор);
- счётчик «Выбрано: N»;
- поле ввода **нового тега** с кнопкой «Добавить» (Enter тоже добавляет) — новый тег сразу
  попадает в список и отмечается;
- кнопки **OK/Отмена**; пустой выбор (ничего не отмечено и новый тег не введён)
  трактуется как отмена — назначение не выполняется.

Источник списка: WPF — `AvailableTags` (уникальные теги всех баз), Avalonia —
`TagFilterItems.Select(t => t.Name)`.

**Как применяется.**

Новый VM-метод `AssignTagsToBatch(IEnumerable<string>)`
([`ViewModels/MainViewModel.BatchCommands.cs`](Configuration%20Management/ViewModels/MainViewModel.BatchCommands.cs)
и [`ViewModels/MainViewModel.Avalonia.BatchCommands.cs`](Configuration%20Management/ViewModels/MainViewModel.Avalonia.BatchCommands.cs)):
каждый отмеченный тег добавляется каждой базе набора — регистронезависимо и без дублей;
затем набор снимается (`ClearBatchSelection`), как и раньше. Точки вызова:
[`Views/MainWindow.Events.cs`](Configuration%20Management/Views/MainWindow.Events.cs)
(`OnBatchAssignTag_Click`) и [`Views/MainWindow.Avalonia.Tree.cs`](Configuration%20Management/Views/MainWindow.Avalonia.Tree.cs)
(`OnBatchAssignTagClick`).

**Как проверить.**

1. Выделите 2–3 базы через Ctrl+клик, контекстное меню → «Для выделенных (N)…» →
   «Назначить тег».
2. Отметьте несколько существующих тегов → OK: теги появляются у всех выбранных баз.
3. Введите новый тег в поле и нажмите «Добавить» или Enter → он добавится сразу
   отмеченным; OK применит его всем базам.
4. Оставьте всё пустым и нажмите OK → ничего не меняется (как отмена).
5. Повторите на Windows (WPF) и Linux (Avalonia).