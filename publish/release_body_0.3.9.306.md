# Управление конфигурациями 1С — v0.3.9.306

Дата сборки: 2026-10-05. Версия в csproj: **0.3.9.306** (`Version`/`AssemblyVersion`/`FileVersion`/`InformationalVersion`).

## Исправлено

### Снятие выделения после мультивыделения ([#340](https://github.com/sivatorov/ConfigurationManagement/issues/340)) — восьмая попытка

1. **Постоянная трассировка `menuclose_trace.json` вместо env-переменной**: диагностика переведена с `CM_MENUCLOSE_TRACE=1` и `%TEMP%` на постоянную трассировку в файл **`menuclose_trace.json`** рядом с настройками приложения (Windows: `%APPDATA%\ConfigurationManagement\`, Linux: `~/.config/ConfigurationManagement/`); формат — JSON Lines, усечение ~512 КБ, стартовая запись с версией/платформой — для воспроизведения бага пользователю ничего включать не нужно, лог пишется всегда.
2. **Стабилизация `IsSelected` доработана**: «догоняющее» восстановление выбора для видимой, но нереализованной виртуализацией строки (~800 мс); окно стабилизации расширено до 15 проходов / 1,5 с; сброс pending-состояния при деактивации окна — только после grace-периода ~300 мс (кратковременная деактивация попапом меню больше не отменяет fallback).

Ключевые файлы: [`MenuCloseTrace.cs`](Configuration%20Management/Services/MenuCloseTrace.cs), [`MenuCloseTraceFormat.cs`](Configuration%20Management/Services/MenuCloseTraceFormat.cs), [`MainWindow.Tree.cs`](Configuration%20Management/Views/MainWindow.Tree.cs), [`MainWindow.Hotkeys.cs`](Configuration%20Management/Views/MainWindow.Hotkeys.cs), [`MainWindow.xaml.cs`](Configuration%20Management/Views/MainWindow.xaml.cs), [`MainWindow.Avalonia.Events.cs`](Configuration%20Management/Views/MainWindow.Avalonia.Events.cs), [`MainWindow.Avalonia.cs`](Configuration%20Management/Views/MainWindow.Avalonia.cs), [`BatchSelectionHelper.cs`](Configuration%20Management/Services/BatchSelectionHelper.cs); тесты [`MenuCloseTraceFormatTests.cs`](ConfigurationManagement.Tests/MenuCloseTraceFormatTests.cs), [`BatchSelectionHelperTests.cs`](ConfigurationManagement.Tests/BatchSelectionHelperTests.cs).

### Программный вход на portal.1c.ru ([#323](https://github.com/sivatorov/ConfigurationManagement/issues/323), [#330](https://github.com/sivatorov/ConfigurationManagement/issues/330), [#334](https://github.com/sivatorov/ConfigurationManagement/issues/334)) — четвёртая итерация

1. **Устранён «фантомный успех» входа**: ответ POST 200 без установки сессионной cookie портала больше НЕ считается успешным входом — повторный запрос каталога не запускается и лимит попыток (3 за сессию) не тратится впустую.
2. **Вход доводится до конца при JS/meta-refresh-редиректе в теле ответа** (CAS-цепочка до `security_check?ticket=…`).
3. **Расширенная диагностика в журнале без секретов**: `sessionCookie=true/false`, `contentType`/`bodyLength`/санитизированное превью тела, имена и флаги cookie из `Set-Cookie`, инвентаризация cookie контейнера, маркер `retryAfterLoginStill302`.

Ключевые файлы: [`OneCUpdatesService.cs`](Configuration%20Management/Services/OneCUpdatesService.cs); тесты [`OneCUpdatesLoginFlowTests.cs`](ConfigurationManagement.Tests/OneCUpdatesLoginFlowTests.cs) — 5 новых сценариев.

**Тесты.** Полный набор `dotnet test` зелёный: **1759**; кросс-сборка Linux (`dotnet build -p:BuildLinux=true`) — без ошибок. Платформы: **Windows (WPF)** и **Linux (Avalonia)**.

## Установка

**Windows:** запустите `ConfigurationManagement.exe` — self-contained single-file, .NET не требуется.

**Linux:** сделайте бинарник исполняемым и запустите:
`chmod +x ConfigurationManagement-linux-x64 && ./ConfigurationManagement-linux-x64`

или установите deb-пакет: `sudo dpkg -i configuration-management_0.3.9.306_amd64.deb`.

## Скачать

- Windows (self-contained single-file): [ConfigurationManagement.exe](https://github.com/sivatorov/ConfigurationManagement/releases/download/v0.3.9.306/ConfigurationManagement.exe)
- Linux x64 (single-file): [ConfigurationManagement-linux-x64](https://github.com/sivatorov/ConfigurationManagement/releases/download/v0.3.9.306/ConfigurationManagement-linux-x64)
- Debian/Ubuntu: [configuration-management_0.3.9.306_amd64.deb](https://github.com/sivatorov/ConfigurationManagement/releases/download/v0.3.9.306/configuration-management_0.3.9.306_amd64.deb)
- Контрольные суммы: [SHA256SUMS.txt](https://github.com/sivatorov/ConfigurationManagement/releases/download/v0.3.9.306/SHA256SUMS.txt)

AppImage в этом релизе не публикуется (отсутствует tooling сборки в окружении Windows — как и в 0.3.9.305).

### SHA-256

```
278bd12aa25a4a4321559d9c07006a834db6c0917a1bf347be5a5feecf6c1ac7  ConfigurationManagement.exe
01d616b1056bd6974f094f516106e56129c5d43d07bdf1e9b192ecf4ef89177e  ConfigurationManagement-linux-x64
7f8f1b485abb79fbccecb148c73bcc5526ac1dc41a6216c8ecc4c689c687cafa  configuration-management_0.3.9.306_amd64.deb