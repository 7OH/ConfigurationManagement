# Управление конфигурациями 1С — v0.3.9.302

Дата сборки: 2026-10-04. Версия в csproj: **0.3.9.302** (`Version`/`AssemblyVersion`/`FileVersion`/`InformationalVersion`).

## Исправлено

- **Инспектор процессов — сброс выделения и «Завершить процесс» ([#342](https://github.com/sivatorov/ConfigurationManagement/issues/342))**: выделение таблицы теперь связано с ViewModel двусторонней привязкой `SelectedItem` (WPF DataGrid и Avalonia ListBox) — раньше клик по строке не доходил до `SelectedRow`, поэтому каждые ~5 секунд выбор сбрасывался, а кнопка «Завершить процесс» находила «нет выделения». Восстановление после автообновления ведётся по составному ключу «PID + командная строка» (защита от переиспользования PID ОС) с двухфазным повтором через диспетчер UI и защитой выбора пользователя ([`ProcessInspectorViewModel.cs`](Configuration%20Management/ViewModels/ProcessInspectorViewModel.cs), [`ProcessInspectorWindow.xaml`](Configuration%20Management/Views/ProcessInspectorWindow.xaml), [`ProcessInspectorWindow.Avalonia.cs`](Configuration%20Management/Views/ProcessInspectorWindow.Avalonia.cs); тесты [`ProcessInspectorSelectionTests.cs`](ConfigurationManagement.Tests/ProcessInspectorSelectionTests.cs)).

- **Снятие выделения после мультивыделения/контекстного меню ([#340](https://github.com/sivatorov/ConfigurationManagement/issues/340))**: вместо отложенного применения выбора (`Dispatcher.BeginInvoke`), при котором выделение пропадало «через мгновение», выбор теперь применяется **синхронно** в момент закрытия меню по данным клика (WPF — в `TryApplyTreeClickAfterMenuClosed`, Avalonia — на первом `PointerPressed`), а повторная доставка «хвоста» клика гасится снимком по времени и позиции. Поиск контейнера при восстановлении учитывает секцию строки («Закреплённые» vs обычный список) — выделение не перескакивает на дубль ([`MainWindow.Hotkeys.cs`](Configuration%20Management/Views/MainWindow.Hotkeys.cs), [`MainWindow.Events.cs`](Configuration%20Management/Views/MainWindow.Events.cs), [`MainWindow.Tree.cs`](Configuration%20Management/Views/MainWindow.Tree.cs), [`MainWindow.Avalonia.Events.cs`](Configuration%20Management/Views/MainWindow.Avalonia.Events.cs), [`BatchSelectionHelper.cs`](Configuration%20Management/Services/BatchSelectionHelper.cs); тесты [`BatchSelectionHelperTests.cs`](ConfigurationManagement.Tests/BatchSelectionHelperTests.cs)).

- **Окно «Типовые конфигурации» — стиль полей ([#321](https://github.com/sivatorov/ConfigurationManagement/issues/321))**: поля окна правки редакции ([`EditionEditWindow.xaml`](Configuration%20Management/Views/EditionEditWindow.xaml)) и окна правки конфигурации ([`ConfigTypeEditWindow.xaml`](Configuration%20Management/Views/ConfigTypeEditWindow.xaml)) приведены к общему виду окон приложения — удалён локальный неявный стиль TextBox, поля используют общий `ModernTextBox` (скругление, акцентная рамка при наведении/фокусе).

- **Создание серверной базы — порт в предупреждении и сохранение «Сервера СУБД» ([#305](https://github.com/sivatorov/ConfigurationManagement/issues/305))**: сравнение серверов для предупреждения о несовместимой версии теперь учитывает порт из отдельного поля `ConnectionSettings.Port` (у баз он хранится отдельно, значение по умолчанию 1541); адрес найденной базы строится нормализатором без задвоения порта; текст предупреждения показывает ОБА адреса — введённый пользователем и адрес базы из списка. «Сервер СУБД» сохраняется не только после успешного создания, но и при закрытии окна (ввод → закрытие без создания больше не теряется) ([`CreateInfobaseService.cs`](Configuration%20Management/Services/CreateInfobaseService.cs), [`CreateInfobaseWindow.xaml.cs`](Configuration%20Management/Views/CreateInfobaseWindow.xaml.cs), [`CreateInfobaseWindow.Avalonia.cs`](Configuration%20Management/Views/CreateInfobaseWindow.Avalonia.cs); локализация ru/en; тесты [`CreateInfobaseDbServerStringTests.cs`](ConfigurationManagement.Tests/CreateInfobaseDbServerStringTests.cs)).

**Тесты.** Новые/дополненные: [`ProcessInspectorSelectionTests.cs`](ConfigurationManagement.Tests/ProcessInspectorSelectionTests.cs) (4 сценария), [`BatchSelectionHelperTests.cs`](ConfigurationManagement.Tests/BatchSelectionHelperTests.cs), [`CreateInfobaseDbServerStringTests.cs`](ConfigurationManagement.Tests/CreateInfobaseDbServerStringTests.cs) (9 сценариев). Полный набор `dotnet test` зелёный: **1710**; кросс-сборка Linux (`dotnet build -p:BuildLinux=true`) без ошибок.

## Установка

**Windows:** запустите `ConfigurationManagement.exe` — self-contained single-file, .NET не требуется.

**Linux:** сделайте бинарник исполняемым и запустите:
`chmod +x ConfigurationManagement-linux-x64 && ./ConfigurationManagement-linux-x64`

или установите deb-пакет: `sudo dpkg -i configuration-management_0.3.9.302_amd64.deb`.

## Скачать

- Windows (self-contained single-file): [ConfigurationManagement.exe](https://github.com/sivatorov/ConfigurationManagement/releases/download/v0.3.9.302/ConfigurationManagement.exe)
- Linux x64 (single-file): [ConfigurationManagement-linux-x64](https://github.com/sivatorov/ConfigurationManagement/releases/download/v0.3.9.302/ConfigurationManagement-linux-x64)
- Debian/Ubuntu: [configuration-management_0.3.9.302_amd64.deb](https://github.com/sivatorov/ConfigurationManagement/releases/download/v0.3.9.302/configuration-management_0.3.9.302_amd64.deb)
- Контрольные суммы: [SHA256SUMS.txt](https://github.com/sivatorov/ConfigurationManagement/releases/download/v0.3.9.302/SHA256SUMS.txt)

### SHA-256

```
0e55988de8094cafbb27273c3f3fbe00de94252c698d86f1ded0ca0882816c83  ConfigurationManagement.exe
593e30eb273ff1aba4e26e966f74982a7ab7a58a120f9a156a86b3edeacfa7bd  ConfigurationManagement-linux-x64
7c6942feee9a7209642beb5ab938a8332e9660da69ab1223d706e9b1777c153e  configuration-management_0.3.9.302_amd64.deb