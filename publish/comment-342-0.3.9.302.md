Исправлено в версии **0.3.9.302**.

**Что было.**

Фикс 0.3.9.300 (восстановление выделения по PID внутри ViewModel) проблему не решил: оказалось, что выделение таблицы вообще не связано с ViewModel. У WPF DataGrid ([`ProcessInspectorWindow.xaml`](Configuration%20Management/Views/ProcessInspectorWindow.xaml)) и Avalonia ListBox ([`ProcessInspectorWindow.Avalonia.cs`](Configuration%20Management/Views/ProcessInspectorWindow.Avalonia.cs)) был задан только `ItemsSource` — привязки `SelectedItem` к `SelectedRow` не было ни на одной платформе. Поэтому клик по строке не доходил до ViewModel: `SelectedRow` всегда оставался `null`, восстановление работало только в юнит-тестах, а в реальном окне каждые ~5 секунд (автообновление, `AutoRefreshIntervalMs = 5000`) выбор сбрасывался, и кнопка «Завершить процесс» показывала подсказку «Выберите процесс из списка».

**Что сделано.**

1. [`Views/ProcessInspectorWindow.xaml`](Configuration%20Management/Views/ProcessInspectorWindow.xaml): для DataGrid добавлена двусторонняя привязка `SelectedItem="{Binding SelectedRow, Mode=TwoWay}"`; аналогично в [`ProcessInspectorWindow.Avalonia.cs`](Configuration%20Management/Views/ProcessInspectorWindow.Avalonia.cs) `ListBox.SelectedItem` привязан к `SelectedRow` двусторонне — клик по строке теперь реально доходит до ViewModel.
2. [`ViewModels/ProcessInspectorViewModel.cs`](Configuration%20Management/ViewModels/ProcessInspectorViewModel.cs): восстановление после автообновления ведётся по составному ключу «PID + токен старта + командная строка», а не только по PID (защита от переиспользования PID операционной системой: процесс перезапустился — PID тот же, процесс другой). Если точного совпадения по ключу нет — fallback по PID, если и его нет — выделение снимается.
3. Двухфазное восстановление через UI-диспетчер: после установки `SelectedRow` выполняется отложенный повтор установки той же строки (страховка от виртуализации контейнеров WPF DataGrid / Avalonia ListBox), но только если строка с тем же ключом всё ещё в списке и пользователь за это время ничего не выбрал — выбор пользователя не перетирается.

**Тесты** ([`ProcessInspectorSelectionTests.cs`](ConfigurationManagement.Tests/ProcessInspectorSelectionTests.cs)): добавлены 4 теста — восстановление по составному ключу при переиспользовании PID, сброс при исчезновении строки, идемпотентность двухфазного восстановления, защита выбора пользователя.

**Как проверить:** обновитесь до **0.3.9.302**, откройте «Инспектор процессов», выделите строку и дождитесь автообновления (~5 с) — выделение сохраняется; «Завершить процесс» работает сразу после выделения и спустя 5+ секунд (показывается подтверждение, без подсказки «Выберите процесс»). Если процесс завершился извне — при следующем опросе выделение корректно снимется.