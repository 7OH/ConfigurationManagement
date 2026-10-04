Исправлено в версии **0.3.9.300**.

**Что было.**

[`ProcessInspectorViewModel.ApplyRows`](Configuration%20Management/ViewModels/ProcessInspectorViewModel.cs) при каждом автообновлении списка (раз в 5 секунд, `AutoRefreshIntervalMs = 5000`) безусловно сбрасывал выделение: после `Processes.Clear()` + `Add` выполнялось `SelectedRow = null`. В результате через 4–5 секунд выделенная строка «развыделялась», а кнопка «Завершить процесс» находила `SelectedRow == null` и молча возвращалась — казалось, что команда не работает.

**Что сделано.**

1. [`ViewModels/ProcessInspectorViewModel.cs`](Configuration%20Management/ViewModels/ProcessInspectorViewModel.cs): перед перезаполнением списка запоминается PID выбранного процесса (`SelectedRow?.Pid`); после обновления строка с тем же PID восстанавливается. Строки пересоздаются при каждом опросе, поэтому сравнение идёт по идентификатору процесса, а не по ссылке на объект.
2. Если процесса с сохранённым PID в новом списке нет (процесс завершился) — выделение снимается.
3. `KillSelected` без выделенной строки теперь показывает подсказку «Выберите процесс из списка» вместо молчаливого возврата.
4. Привязки `SelectedItem`/`SelectedRow` проверены на обеих платформах (DataGrid WPF, ListBox Avalonia) — восстановление через ViewModel достаточно, окна не менялись.

**Тесты** ([`ProcessInspectorSelectionTests.cs`](ConfigurationManagement.Tests/ProcessInspectorSelectionTests.cs)): `Refresh_PreservesSelectionByPid`, `Refresh_ClearsSelectionWhenProcessGone`, `KillSelected_NoSelection_ShowsHint`.

**Как проверить:** обновитесь до **0.3.9.300**, откройте «Инспектор процессов», выделите строку и дождитесь автообновления (~5 с) — выделение сохранится; «Завершить процесс» сработает по выбранной строке. Если процесс успел завершиться — выделение корректно снимется.