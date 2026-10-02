Исправлено в версии **0.3.9.289** (Windows/WPF и Linux/Avalonia).

**Что добавлено:** рекурсивная свертка/развертка ТОЛЬКО текущей ветки дерева групп — без затрагивания соседних веток.

**Как работает:**

1. **Мышью — Ctrl+клик по группе.** Удерживая Ctrl, кликните по строке группы: ветка (сама группа + все её подгруппы рекурсивно) сворачивается, если была развёрнута, и наоборот. Текущая строка и выделение при этом **не меняются** ([`MainWindow.Events.cs`](Configuration%20Management/Views/MainWindow.Events.cs), логика — в ViewModel: `ToggleGroupBranch`).

2. **Клавиатура — Ctrl+Alt++ / Ctrl+Alt+-.** Развернуть / свернуть ветку под курсором (выбранная группа либо группа, в которой находится выбранная база). Регистрируются обе раскладки: основная (`+`/`-`) и цифровой блок (NumPad `+`/`-`) ([`MainWindow.Hotkeys.cs`](Configuration%20Management/Views/MainWindow.Hotkeys.cs), [`MainWindow.Avalonia.Hotkeys.cs`](Configuration%20Management/Views/MainWindow.Avalonia.Hotkeys.cs)).
   - Почему не Ctrl+Plus/Ctrl+Minus: эти сочетания уже заняты масштабом строк списка (issue #303), а Ctrl+Shift+Plus/Minus — «развернуть/свернуть всё» (issue #160).

3. **Логика ветки в ViewModel обеих платформ**: `ExpandBranchOf` / `CollapseBranchOf` / `ToggleGroupBranch` — рекурсивно применяют состояние к узлу и его потомкам; соседние ветки и родительский узел не трогаются. Состояние свёрнутости сохраняется в настройках (WPF пересобирает набор свёрнутых групп, Avalonia ведёт его автоматически через отслеживание `IsExpanded`).

**Тесты.** [`GroupNodeViewModelTests`](ConfigurationManagement.Tests/GroupNodeViewModelTests.cs): `BranchCollapse_AffectsOnlyOwnSubtree` и `BranchExpand_AffectsOnlyOwnSubtree` — свёртка/развёртка ветки не меняет соседние ветки и родителя. Полный набор `dotnet test` зелёный (1606), кросс-сборка Linux (`dotnet build -p:BuildLinux=true`) без ошибок.

**Как проверить:**

1. Установите **0.3.9.289** ([релиз v0.3.9.289](https://github.com/sivatorov/ConfigurationManagement/releases/tag/v0.3.9.289)).
2. Кликните по группе с подгруппами, удерживая **Ctrl** — ветка развернётся/свернётся, курсор останется на месте.
3. Встаньте на группу (или базу внутри группы) и нажмите **Ctrl+Alt++** / **Ctrl+Alt+-** — развернётся/свернётся только эта ветка.