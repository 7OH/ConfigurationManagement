Исправлено в версии **0.3.9.300** (Windows/WPF и Linux/Avalonia).

**Что было.**

По диагностике `CM_COLUMNS`: сумма видимых колонок `total=991,2`, а прокручиваемая область `extent=1158,8` — справа от последней колонки оставался пустой «хвост» (~170 px). Ширина контента считалась по живой ширине ScrollViewer (`ExtentWidth`, замер контента), а не по сумме видимых колонок. Кроме того, при старте и после удаления колонки полоса автоматически уводилась в крайнюю правую позицию (`EnsureHorizontalReach` → безусловная докрутка `ScrollToHorizontalOffset(ExtentWidth)`).

**Что сделано.**

1. [`Views/MainWindow.Columns.cs`](Configuration%20Management/Views/MainWindow.Columns.cs): ширина контента/заголовка считается строго по сумме видимых колонок ([`ListMinWidthCalculator.Compute`](Configuration%20Management/Views/ListMinWidthCalculator.cs)), целевая ширина = `max(total, viewport)` — без «хвоста» от живого `ExtentWidth`; прокручиваемая область теперь `extent ≈ max(total, viewport)`, пустого места справа от последней колонки нет.
2. `EnsureHorizontalReach` больше не вызывает безусловную докрутку вправо на старте и при изменении набора колонок — полоса остаётся на месте, докрутка выполняется только при необходимости.
3. [`Views/MainWindow.Scroll.cs`](Configuration%20Management/Views/MainWindow.Scroll.cs): при смене набора колонок сохраняется предыдущая горизонтальная позиция.
4. То же самое для Avalonia (`MainWindow.Avalonia.Columns.cs` — `SyncListWidthToViewport`, `MainWindow.Avalonia.Scroll.cs`).

**Тесты** ([`ListMinWidthCalculatorTests.cs`](ConfigurationManagement.Tests/ListMinWidthCalculatorTests.cs)): сумма колонок без лишнего слагаемого, скрытая колонка не влияет на сумму.

**Как проверить:** обновитесь до **0.3.9.300**, сузьте окно так, чтобы колонки списка баз не помещались, — внизу появляется горизонтальная полоса; докрутите её до последней колонки: колонки достижимы, справа от последней нет пустого места; при старте и после удаления колонки позиция не прыгает вправо.