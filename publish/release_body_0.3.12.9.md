# v0.3.12.9 — Автопроверка обновлений платформы при открытии окна (issue #363)

## Что улучшено (Windows/WPF и Linux/Avalonia)

Окно «Обновление платформы 1С» (Ctrl+F9) при открытии **сразу запускает проверку**
каталога версий на `releases.1c.ru` ([`PlatformUpdateWindow.xaml.cs`](https://github.com/sivatorov/ConfigurationManagement/blob/main/Configuration%20Management/Views/PlatformUpdateWindow.xaml.cs),
[`PlatformUpdateWindow.Avalonia.cs`](https://github.com/sivatorov/ConfigurationManagement/blob/main/Configuration%20Management/Views/PlatformUpdateWindow.Avalonia.cs)).

Раньше окно открывалось пустым до нажатия кнопки «Проверить обновления». Теперь
список установленных и доступных версий заполняется автоматически. Защита от
повторного запуска — существующий флаг `IsBusy` в `PlatformUpdateViewModel.CheckUpdatesAsync`.

Образец — уже работающая автозагрузка каталога в окне «Скачивание версии платформы 1С».

## Проверки

- `dotnet test` — **2302** passed, 0 failed.
- Сборки WPF (win-x64) и Linux (linux-x64) без ошибок.

## Ассеты

| Файл | Назначение |
|---|---|
| `ConfigurationManagement.exe` | Windows single-file self-contained (основной ассет автообновления) |
| `ConfigurationManagement-0.3.12.9-win-x64.zip` | ZIP-архив exe |
| `ConfigurationManagement-linux-x64` | Linux single-file (Avalonia) |
| `configuration-management_0.3.12.9_amd64.deb` | Debian/Ubuntu пакет |
| `SHA256SUMS.txt` | Контрольные суммы |
