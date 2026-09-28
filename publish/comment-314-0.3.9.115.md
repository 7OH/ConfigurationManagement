Исправлено в версии **0.3.9.115** (Windows/WPF и Linux/Avalonia).

**Что было не так.**

При выделении строки в списке подсвечивалась её копия в узле «Закреплённые»
(«дублирование» выделения), а при выделении одной закреплённой базы подсвечивались
все закреплённые — вместо одной строки «горел» весь узел.

**Причина.**

Windows/WPF — [`ViewModels/MainViewModel.Theme.cs`](Configuration%20Management/ViewModels/MainViewModel.Theme.cs):
закреплённая база попадала в дерево дважды ОДНИМ И ТЕМ ЖЕ экземпляром `Infobase`
(узел «Закреплённые» + собственная группа). Штатный выбор TreeView синхронизируется
по данным, а восстановление выбора после пересборки дерева искало контейнер по данным
и всегда находило первую копию — строку в «Закреплённых» (они идут первыми), поэтому
подсветка «уезжала»/дублировалась на закрепления, а «Закреплённые» как узел выглядели
выделенными целиком.

Linux/Avalonia — аналогичная проблема была устранена ранее (0.3.9.70, issue #301)
обёрткой `PinnedInfobaseItem` (уникальные данные у каждой строки); поведение проверено
и остаётся корректным.

**Как исправлено.**

Windows/WPF — перенесён тот же механизм, что уже работает в Linux/Avalonia:

- Новая обёртка [`ViewModels/PinnedInfobaseItem.cs`](Configuration%20Management/ViewModels/PinnedInfobaseItem.cs) —
  уникальный объект данных каждой строки узла «Закреплённые».
- [`ViewModels/GroupNodeViewModel.cs`](Configuration%20Management/ViewModels/GroupNodeViewModel.cs)
  — `PopulateItems` кладёт в узел «Закреплённые» обёртки (в обычные группы базы
  попадают как раньше).
- [`Views/MainWindow.xaml`](Configuration%20Management/Views/MainWindow.xaml) — строка
  обёртки рисуется тем же шаблоном строки базы, но через `ContentControl` с реальной
  базой (`Base`): привязки, кнопки, теги и контекстное меню строки работают как раньше.
- Разворачивание обёртки до реальной базы во всех точках, где читается DataContext
  строки: клики (левый/правый), `OnMainTree_SelectedItemChanged`, поиск контейнера по
  данным и «Найти в списке», клавиатурная навигация, DnD, команды тегов,
  `FindFirstInfobaseItem` ([`Views/MainWindow.Tree.cs`](Configuration%20Management/Views/MainWindow.Tree.cs) —
  helper `UnwrapInfobase`, [`Views/MainWindow.Events.cs`](Configuration%20Management/Views/MainWindow.Events.cs),
  [`Views/MainWindow.DragDrop.cs`](Configuration%20Management/Views/MainWindow.DragDrop.cs),
  [`Views/MainWindow.Tags.cs`](Configuration%20Management/Views/MainWindow.Tags.cs),
  [`Views/MainWindow.Columns.cs`](Configuration%20Management/Views/MainWindow.Columns.cs)).

Теперь клик по строке в списке и по строке в «Закреплённых» подсвечивает ровно одну
строку; команды правой панели и контекстного меню работают по реальной базе.
Мультивыделение Ctrl+Click, Shift-диапазон, «Найти в списке» (#285) и клавиатурная
навигация не изменены.

**Как проверить.**

1. Закрепите базу (пин) → она появится в узле «Закреплённые» и в своей группе.
2. Выделите строку в списке: подсвечена только эта строка, копия в «Закреплённых» — нет.
3. Выделите строку в «Закреплённых»: подсвечена только она, другие закреплённые — нет.
4. Правый клик/команды правой панели по строке «Закреплённых» применяются к этой базе.
5. Ctrl+клик по 2–3 базам, Shift-диапазон, «Найти в списке» и навигация стрелками
   работают как раньше.