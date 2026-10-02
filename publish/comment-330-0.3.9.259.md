Исправлено в версии **0.3.9.259** (Windows/WPF; Linux/Avalonia не затронута — там автопрокрутка уже
шла через `Dispatcher.UIThread.Post`).

Окно «Скачивание версии платформы 1С» больше не падает при открытии с ошибкой
`InvalidOperationException` «Вызывающий поток не может получить доступ к данному объекту».

**Причина.** `PlatformDownloadViewModel.AppendLog` уведомляет UI (`OnPropertyChanged(LogText)`)
из фоновых задач — `LoadCatalogAsync`/`DownloadAsync` используют `ConfigureAwait(false)`, поэтому
обработчик `PropertyChanged` окна выполнялся на фоновом потоке. Окно
([`Views/PlatformDownloadWindow.xaml.cs`](Configuration%20Management/Views/PlatformDownloadWindow.xaml.cs))
вызывало `LogBox.ScrollToEnd()` прямо в этом обработчике — WPF бросал `Dispatcher.VerifyAccess`
при каждом обновлении журнала, и окно не открывалось.

**Как исправлено.** Автопрокрутка журнала перенесена в UI-поток:
`Dispatcher.BeginInvoke(new Action(() => LogBox?.ScrollToEnd()))` + защита `?.` на случай закрытия
окна до исполнения отложенного вызова. Контракт ViewModel не менялся.

**Тесты.** Добавлен регрессионный тест: `AppendLog` из `Task.Run` поднимает уведомление
без исключений ([`PlatformDownloadViewModelTests`](ConfigurationManagement.Tests/PlatformDownloadViewModelTests.cs)).
Полный набор `dotnet test` зелёный (1474), кросс-сборка Linux без ошибок.

**Как проверить в 0.3.9.259.**

1. Установите 0.3.9.259 на Windows.
2. Откройте «Скачивание версии платформы 1С» (при наличии сети каталог загрузится, при её
   отсутствии в журнал напишется сообщение об ошибке каталога).
3. Окно не падает, журнал прокручивается к последней строке; «Скачать»/«Установить» работают
   как раньше.