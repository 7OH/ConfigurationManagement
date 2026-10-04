# Управление конфигурациями 1С — v0.3.9.305

Дата сборки: 2026-10-04. Версия в csproj: **0.3.9.305** (`Version`/`AssemblyVersion`/`FileVersion`/`InformationalVersion`).

## Исправлено

### Создание серверной базы ([#305](https://github.com/sivatorov/ConfigurationManagement/issues/305))

1. **Потеря «Сервера СУБД» после успешного создания устранена**: сохранение настроек главного окна (`MainViewModel.SaveSettings`) больше не перезаписывает файл настроек «с нуля» — выполняется мутация загруженного объекта, поэтому поля внешних писателей (`LastCreateDbServer`/`LastCreateDbPort`) сохраняются (WPF; Avalonia такой проблемы не имела).
2. **Запоминается последний использованный тип базы** («Файловая»/«Клиент-серверная»): новое поле `AppSettings.LastCreateDbType` (`"File"`/`"ClientServer"`, пустое значение трактуется как `"File"`); сервер/порт/тип сохраняются и после успешного создания, и при закрытии окна; при повторном открытии окна подставляются оба значения (обе платформы).

Ключевые файлы: [`MainViewModel.Launch.cs`](Configuration%20Management/ViewModels/MainViewModel.Launch.cs), [`AppSettings.cs`](Configuration%20Management/Models/AppSettings.cs), [`CreateInfobaseService.cs`](Configuration%20Management/Services/CreateInfobaseService.cs), [`CreateInfobaseWindow.xaml.cs`](Configuration%20Management/Views/CreateInfobaseWindow.xaml.cs), [`CreateInfobaseWindow.Avalonia.cs`](Configuration%20Management/Views/CreateInfobaseWindow.Avalonia.cs); тесты [`CreateInfobaseDbServerStringTests.cs`](ConfigurationManagement.Tests/CreateInfobaseDbServerStringTests.cs).

### Снятие выделения после мультивыделения ([#340](https://github.com/sivatorov/ConfigurationManagement/issues/340)) — седьмая попытка

- **Диагностика `CM_MENUCLOSE_TRACE=1`**: полная последовательность событий клика, закрывшего контекстное меню (снимок → MouseUp → MouseDown → fallback → стабилизация → контрольный дамп через 500 мс) — при сохранении бага пользователь может прислать полный лог одного воспроизведения.
- **Стабилизация `IsSelected` вызывается во всех путях применения клика**, в т.ч. при сброшенном снимке до повторной доставки MouseDown (путь C).
- **Стабилизация доводится до сходимости** (до 10 проходов / 1 с) и восстанавливает выбор для видимой, но нереализованной виртуализацией строки; проверка реализованных строк в `SelectionMatchesTarget` уточнена.
- **Сброс pending-состояния при деактивации окна** — только после фактического закрытия контекстных меню.

Ключевые файлы: [`MainWindow.Events.cs`](Configuration%20Management/Views/MainWindow.Events.cs), [`MainWindow.Hotkeys.cs`](Configuration%20Management/Views/MainWindow.Hotkeys.cs), [`MainWindow.Tree.cs`](Configuration%20Management/Views/MainWindow.Tree.cs), [`MainWindow.xaml.cs`](Configuration%20Management/Views/MainWindow.xaml.cs), [`MainWindow.Avalonia.Events.cs`](Configuration%20Management/Views/MainWindow.Avalonia.Events.cs); тесты [`BatchSelectionHelperTests.cs`](ConfigurationManagement.Tests/BatchSelectionHelperTests.cs).

### Программный вход на portal.1c.ru ([#323](https://github.com/sivatorov/ConfigurationManagement/issues/323), [#330](https://github.com/sivatorov/ConfigurationManagement/issues/330), [#334](https://github.com/sivatorov/ConfigurationManagement/issues/334)) — третья итерация

1. **Пошаговая диагностика входа** (причина запуска входа, статусы GET/POST, имена полей формы без значений, атрибут `action`, результат цепочки редиректов, наличие сессионной cookie) — при сохранении проблемы следующий релиз сможет точно указать причину.
2. **POST формы входа отправляется на атрибут `action` формы** (ранее — всегда на URL GET).
3. **Распознавание изменённой формы входа** (OAuth/JS-челлендж) с понятным сообщением `FormUnavailable`.
4. **В окнах проверки обновлений / обновления платформы / скачивания** — имя используемой учётной записи ИТС, кнопки «Открыть login.1c.ru в браузере» и «Учётные данные ИТС…», уточнённые тексты ru/en.

Ключевые файлы: [`OneCUpdatesService.cs`](Configuration%20Management/Services/OneCUpdatesService.cs), [`PlatformUpdateService.cs`](Configuration%20Management/Services/PlatformUpdateService.cs), [`UpdateCheckWindow.xaml`](Configuration%20Management/Views/UpdateCheckWindow.xaml), [`PlatformUpdateWindow.xaml`](Configuration%20Management/Views/PlatformUpdateWindow.xaml), [`PlatformDownloadWindow.xaml`](Configuration%20Management/Views/PlatformDownloadWindow.xaml), [`ru.json`](Configuration%20Management/Localization/Languages/ru.json), [`en.json`](Configuration%20Management/Localization/Languages/en.json); тесты [`OneCUpdatesLoginFlowTests.cs`](ConfigurationManagement.Tests/OneCUpdatesLoginFlowTests.cs), [`PlatformUpdateServiceTests.cs`](ConfigurationManagement.Tests/PlatformUpdateServiceTests.cs), [`UpdateCheckCatalogTests.cs`](ConfigurationManagement.Tests/UpdateCheckCatalogTests.cs).

**Тесты.** Полный набор `dotnet test` зелёный: **1740**; кросс-сборка Linux (`dotnet build -p:BuildLinux=true`) без ошибок. Платформы: **Windows (WPF)** и **Linux (Avalonia)**.

## Установка

**Windows:** запустите `ConfigurationManagement.exe` — self-contained single-file, .NET не требуется.

**Linux:** сделайте бинарник исполняемым и запустите:
`chmod +x ConfigurationManagement-linux-x64 && ./ConfigurationManagement-linux-x64`

или установите deb-пакет: `sudo dpkg -i configuration-management_0.3.9.305_amd64.deb`.

## Скачать

- Windows (self-contained single-file): [ConfigurationManagement.exe](https://github.com/sivatorov/ConfigurationManagement/releases/download/v0.3.9.305/ConfigurationManagement.exe)
- Linux x64 (single-file): [ConfigurationManagement-linux-x64](https://github.com/sivatorov/ConfigurationManagement/releases/download/v0.3.9.305/ConfigurationManagement-linux-x64)
- Debian/Ubuntu: [configuration-management_0.3.9.305_amd64.deb](https://github.com/sivatorov/ConfigurationManagement/releases/download/v0.3.9.305/configuration-management_0.3.9.305_amd64.deb)
- Контрольные суммы: [SHA256SUMS.txt](https://github.com/sivatorov/ConfigurationManagement/releases/download/v0.3.9.305/SHA256SUMS.txt)

### SHA-256

```
4e9d171625988faaf6d34b30ee3fba94c3f1ec3b2f7d3117fb91928ee0b7e3ec  ConfigurationManagement.exe
5846aa47cb8bf3688a4244f0ba12df59f8e6878b71fd9829a8afee4e20b5d716  ConfigurationManagement-linux-x64
2a1ce5314560ab586cd68dab4d086395f2a5bc6c841c8b3916f35cc6b7989b7c  configuration-management_0.3.9.305_amd64.deb