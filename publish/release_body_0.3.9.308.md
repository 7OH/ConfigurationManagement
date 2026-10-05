# Управление конфигурациями 1С — v0.3.9.308

Дата сборки: 2026-10-05. Версия в csproj: **0.3.9.308** (`Version`/`AssemblyVersion`/`FileVersion`/`InformationalVersion`).

Коммиты: `fda021d` (0.3.9.307), `5a616d8` (0.3.9.308). Полная история — [CHANGELOG.md](https://github.com/sivatorov/ConfigurationManagement/blob/main/CHANGELOG.md).

## Исправлено

### Снятие выделения после мультивыделения ([#340](https://github.com/sivatorov/ConfigurationManagement/issues/340)) — девятая попытка, версия 0.3.9.308

1. **Файл диагностики создаётся при каждом старте**: новый `MenuCloseTrace.EnsureStarted()` пишет startup-запись (версия/платформа/ОС/каталог данных) вне зависимости от событий меню; основной файл — **`trace.json`** рядом с настройками приложения (Windows: `%APPDATA%\ConfigurationManagement\`, Linux: `~/.config/ConfigurationManagement/`), legacy `menuclose_trace.json` от 0.3.9.306 продолжает дописываться при наличии ([`MenuCloseTrace.cs`](Configuration%20Management/Services/MenuCloseTrace.cs), [`MenuCloseTraceFormat.cs`](Configuration%20Management/Services/MenuCloseTraceFormat.cs)).
2. **Безусловные записи `MenuOpened`/`MenuClosed`** при каждом открытии/закрытии контекстного меню (WPF: `OnContextMenuOpened`/`OnContextMenuClosed`; Avalonia: `ContextMenu.IsOpenProperty.Changed`; для дерева — метка закрытия `_lastMenuCloseTick`) ([`MainWindow.Hotkeys.cs`](Configuration%20Management/Views/MainWindow.Hotkeys.cs), [`MainWindow.Avalonia.Events.cs`](Configuration%20Management/Views/MainWindow.Avalonia.Events.cs)).
3. **Стабилизация `IsSelected` для любого обычного клика** в окне ~1,5 с после закрытия контекстного меню дерева: чистый предикат `BatchSelectionHelper.ShouldStabilizeAfterMenuClose` — прежний признак опирался на успешную запись снимка и не срабатывал при закрытии меню по ESC/кликом мимо строки, из-за чего переработка контейнеров (Recycling) сбрасывала подсветку ([`BatchSelectionHelper.cs`](Configuration%20Management/Services/BatchSelectionHelper.cs), [`MainWindow.Events.cs`](Configuration%20Management/Views/MainWindow.Events.cs), зеркально Avalonia).
4. Документировано соглашение о файле трассировки в README и комментарии к issue (`trace.json`; включать вручную не нужно).

Тесты: новый `MenuCloseTraceFormat.ResolveFileName` (4 теста) и предикат `ShouldStabilizeAfterMenuClose` (5 тестов). Полный набор `dotnet test` зелёный: **1773**; кросс-сборка Linux (`dotnet build -p:BuildLinux=true`) — без ошибок.

### Программный вход на portal.1c.ru ([#323](https://github.com/sivatorov/ConfigurationManagement/issues/323), [#330](https://github.com/sivatorov/ConfigurationManagement/issues/330), [#334](https://github.com/sivatorov/ConfigurationManagement/issues/334)) — пятая итерация, версия 0.3.9.307

1. **Устранён «мгновенный Success» входа**: ранний выход `TryLoginPortalAsync` по имени cookie заменён честной проверкой «живой» сессии — пробный GET по исходному URL операции: если ответ 2xx и не страница входа — сессия жива и лимит попыток не тратится; если мертва — cookie портала снимаются и выполняется полный вход (GET формы → POST → цепочка редиректов) ([`OneCUpdatesService.cs`](Configuration%20Management/Services/OneCUpdatesService.cs)).
2. **Повторный вход при 302 после «успешного» входа**: вместо одноразового флага `loginTried` — счётчик попыток в рамках одного вызова (`MaxLoginAttemptsPerOperation = 2`) со свежей формой (новый `execution`/`lt`); маркер `retryAfterLoginStill302` остаётся диагностическим, при исчерпании повторов возвращается честный `AuthRequired`/`AuthFailed`/`LoginLimitReached`.
3. **`HasPortalSessionCookie()` ужесточён по атрибутам**: cookie учитывается только с доменом `.1c.ru` и `Path=/` (или покрывающим корень) — cookie-заглушки WAF/CDN сессией портала больше не считаются.
4. **Новый `ClearPortalCookies()`** снимает cookie хостов `login.1c.ru`/`releases.1c.ru` перед повторным входом (без влияния на Basic Auth в заголовках).
5. Диагностика: инвентаризация cookie контейнера в начале каждого входа и результат пробной проверки живой сессии (`alive=True/False`) — по журналу видно, какая cookie присутствовала и почему принято решение.

Тесты: 5 новых сценариев в [`OneCUpdatesLoginFlowTests.cs`](ConfigurationManagement.Tests/OneCUpdatesLoginFlowTests.cs). Полный набор `dotnet test` зелёный: **1764**; кросс-сборка Linux — без ошибок.

## Установка

**Windows:** запустите `ConfigurationManagement.exe` — self-contained single-file, .NET не требуется.

**Linux:** сделайте бинарник исполняемым и запустите:
`chmod +x ConfigurationManagement && ./ConfigurationManagement`

или установите deb-пакет: `sudo dpkg -i configuration-management_0.3.9.308_amd64.deb`.

## Скачать

- Windows (self-contained single-file): [ConfigurationManagement.exe](https://github.com/sivatorov/ConfigurationManagement/releases/download/v0.3.9.308/ConfigurationManagement.exe)
- Linux x64 (single-file): [ConfigurationManagement](https://github.com/sivatorov/ConfigurationManagement/releases/download/v0.3.9.308/ConfigurationManagement)
- Debian/Ubuntu: [configuration-management_0.3.9.308_amd64.deb](https://github.com/sivatorov/ConfigurationManagement/releases/download/v0.3.9.308/configuration-management_0.3.9.308_amd64.deb)

AppImage в этом релизе не публикуется (отсутствует tooling сборки в окружении Windows — как и в 0.3.9.306).

### SHA-256

```
e9d2a00b50ab5a526dfdb2834b5c50fa2588cbc88c8a4df63f700e19f7d17c47  ConfigurationManagement.exe
053623fa4a1d1deab7d6af2296b41ad1d6c402ab32095453fe6772c5d28b1c9c  ConfigurationManagement
ac65bbad943e6c16bade0405642f47fd15e27a9f2cbb339b11d0005ea28c1bd6  configuration-management_0.3.9.308_amd64.deb