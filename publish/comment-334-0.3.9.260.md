Исправлено в версии **0.3.9.260** (Windows/WPF; Linux/Avalonia не затронута — там автопрокрутка уже
шла через `Dispatcher.UIThread.Post`).

Окно «Обновление платформы 1С» больше не падает при проверке обновлений с ошибкой
`InvalidOperationException` «Вызывающий поток не может получить доступ к данному объекту».

**Причина.** `PlatformUpdateViewModel.AppendLog` уведомляет UI (`OnPropertyChanged(LogText)`)
из фоновых задач — `CheckUpdatesAsync` использует `ConfigureAwait(false)`, поэтому
обработчик `PropertyChanged` окна выполнялся на фоновом потоке. Окно
([`Views/PlatformUpdateWindow.xaml.cs`](Configuration%20Management/Views/PlatformUpdateWindow.xaml.cs))
вызывало `LogBox.ScrollToEnd()` прямо в этом обработчике — WPF бросал `Dispatcher.VerifyAccess`
при каждом обновлении журнала, и окно падало во время проверки каталога версий.

**Как исправлено.** Автопрокрутка журнала перенесена в UI-поток:
`Dispatcher.BeginInvoke(new Action(() => LogBox?.ScrollToEnd()))` + защита `?.` на случай закрытия
окна до исполнения отложенного вызова. Контракт ViewModel не менялся.

**Про пустой ответ портала.** В момент падения `releases.1c.ru/project/Platform83` возвращал
пустую страницу — это отдельный внешний фактор, который падение НЕ вызывает: провайдер
([`PlatformUpdateService`](Configuration%20Management/Services/PlatformUpdateService.cs)) маппит
пустое тело/HTTP-ошибку в статус `NetworkError` без исключений, а окно показывает пользователю
понятное сообщение «Ошибка сети при обращении к сайту 1С» (журнал + уведомление, ключ
`PlatformUpdate.Error.NetworkError`). WARN-строки в журнале приложения — штатное поведение.

**Тесты.** Добавлены регрессионные тесты: `AppendLog` и запуск `CheckUpdatesAsync` из `Task.Run`
поднимают уведомления `PropertyChanged` без исключений; сценарий «пустой ответ провайдера» —
статус ошибки без проброса исключения
([`PlatformUpdateViewModelTests`](ConfigurationManagement.Tests/PlatformUpdateViewModelTests.cs)).
Полный набор `dotnet test` зелёный (1477), кросс-сборка Linux без ошибок.

**Как проверить в 0.3.9.260.**

1. Установите 0.3.9.260 на Windows.
2. Откройте «Обновление платформы 1С» (Ctrl+F9): при доступном портале каталог загрузится,
   при пустом ответе/ошибке сети в журнал и уведомление выведется «Ошибка сети при обращении
   к сайту 1С».
3. Окно не падает, журнал прокручивается к последней строке; «Проверить обновления»,
   «Скачать и установить» и «Только скачать» работают как раньше.