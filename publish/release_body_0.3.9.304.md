# Управление конфигурациями 1С — v0.3.9.304

Дата сборки: 2026-10-04. Версия в csproj: **0.3.9.304** (`Version`/`AssemblyVersion`/`FileVersion`/`InformationalVersion`).

## Исправлено

### 0.3.9.303 — программный вход на portal.1c.ru ([#334](https://github.com/sivatorov/ConfigurationManagement/issues/334), [#330](https://github.com/sivatorov/ConfigurationManagement/issues/330), [#323](https://github.com/sivatorov/ConfigurationManagement/issues/323))

Общий корень «Превышен лимит попыток входа (лимит 3)» / «Редирект 302» / «Требуется вход» при автообновлении платформы, скачивании версии платформы и проверке обновлений — CAS-авторизация на `portal.1c.ru`:

1. **«Фантомный успех»**: любой ответ 2xx после POST входа считался успехом; при неверном логине CAS возвращает HTTP 200 с телом формы входа (`execution`/`lt`) без сессионной cookie → следующий запрос снова 302 → повторный вход ×3 → лимит. Теперь успех подтверждается содержимым тела (`LooksLikeLoginForm`): 200 с формой входа — честный `AuthFailed` с анонимизированной диагностикой.
2. **Счётчик попыток не сбрасывался при успехе** (служба — singleton): сброс во всех ветках успеха + ранний выход по сессионной cookie (`HasPortalSessionCookie`, JSESSIONID/TGC/session_id).
3. **Цикл 302→вход→302 внутри одного вызова**: не более одной попытки входа за вызов (`loginTried` в `SendWithAuthAsync`).
4. **Страница входа при HTTP 200 не распознавалась**: `FetchPageCoreAsync`/`CheckForUpdatesAsync` проверяют тело и возвращают понятную ошибку авторизации.
5. **Понятное сообщение о лимите**: новый текст с советом проверить учётные данные ИТС, автоматический сброс через ~10 минут, отдельный ключ локализации `LoginLimitReached`.

Ключевые файлы: [`OneCUpdatesService.cs`](Configuration%20Management/Services/OneCUpdatesService.cs), [`PlatformUpdateService.cs`](Configuration%20Management/Services/PlatformUpdateService.cs), [`PlatformCatalogResult.cs`](Configuration%20Management/Models/PlatformCatalogResult.cs), [`PlatformUpdateViewModel.cs`](Configuration%20Management/ViewModels/PlatformUpdateViewModel.cs), [`PlatformDownloadViewModel.cs`](Configuration%20Management/ViewModels/PlatformDownloadViewModel.cs), [`ru.json`](Configuration%20Management/Localization/Languages/ru.json), [`en.json`](Configuration%20Management/Localization/Languages/en.json); тесты [`OneCUpdatesLoginFlowTests.cs`](ConfigurationManagement.Tests/OneCUpdatesLoginFlowTests.cs) (7 новых + 2 обновлённых сценария).

### 0.3.9.304 — снятие выделения после контекстного меню ([#340](https://github.com/sivatorov/ConfigurationManagement/issues/340))

Шестая попытка, новая стратегия **«штатный выбор + однократный fallback + стабилизация IsSelected»** (WPF и Avalonia):

- **Штатный путь выбора восстановлен**: в `TryApplyTreeClickAfterMenuClosed` выбор больше не применяется синхронно — фиксируется только клик (снимок + флаг `_menuClosePendingApply` + целевая база/секция); повторная доставка MouseDown в дерево обрабатывается штатно (`ClearBatchSelection` + `ApplySelection` по живому контейнеру) и снимает флаг.
- **Fallback** через `Dispatcher.BeginInvoke(DispatcherPriority.Input)`: если к моменту исполнения флаг ещё взведён — выбор применяется по данным (`SelectTreeRowByData`); идемпотентен и не вмешивается при перевыборе пользователем.
- **Стабилизация `EnsureSelectionStable`**: одноразовая подписка на `LayoutUpdated` (до 3 срабатываний или ~200 мс); при расхождении `SelectedInfobase` и `IsSelected` контейнера выбор восстанавливается по данным. Метод никогда не вызывает `ClearBatchSelection`/`ToggleBatchSelection` — мультивыделение не затрагивается.
- **Снимок** записывается только для простого левого клика БЕЗ модификаторов (`ShouldRecordMenuCloseSnapshot`) — Ctrl/Shift-клики уходят штатной логике мультивыделения.
- **Avalonia** — зеркально: убрано подавление повторной доставки (`OnTreeMenuCloseClickDedup_PointerPressed`), выбор применяет штатный `OnRowPointerPressed` контрола; fallback по данным + стабилизация через `Dispatcher.UIThread.Post`/`LayoutUpdated`; новый `LeveledTreeView.FindRowForData` ищет контейнер с учётом секции («Закреплённые» vs обычный список).

Ключевые файлы: [`MainWindow.Hotkeys.cs`](Configuration%20Management/Views/MainWindow.Hotkeys.cs), [`MainWindow.Events.cs`](Configuration%20Management/Views/MainWindow.Events.cs), [`MainWindow.Tree.cs`](Configuration%20Management/Views/MainWindow.Tree.cs), [`MainWindow.Avalonia.Events.cs`](Configuration%20Management/Views/MainWindow.Avalonia.Events.cs), [`LeveledTreeView.Avalonia.cs`](Configuration%20Management/Controls/LeveledTreeView.Avalonia.cs), [`BatchSelectionHelper.cs`](Configuration%20Management/Services/BatchSelectionHelper.cs); тесты [`BatchSelectionHelperTests.cs`](ConfigurationManagement.Tests/BatchSelectionHelperTests.cs) (4 новых сценария).

**Тесты.** Новые/дополненные: [`OneCUpdatesLoginFlowTests.cs`](ConfigurationManagement.Tests/OneCUpdatesLoginFlowTests.cs) (0.3.9.303), [`BatchSelectionHelperTests.cs`](ConfigurationManagement.Tests/BatchSelectionHelperTests.cs) (0.3.9.304). Полный набор `dotnet test` зелёный: **1721**; кросс-сборка Linux (`dotnet build -p:BuildLinux=true`) без ошибок. Платформы: **Windows (WPF)** и **Linux (Avalonia)**.

## Установка

**Windows:** запустите `ConfigurationManagement.exe` — self-contained single-file, .NET не требуется.

**Linux:** сделайте бинарник исполняемым и запустите:
`chmod +x ConfigurationManagement-linux-x64 && ./ConfigurationManagement-linux-x64`

или установите deb-пакет: `sudo dpkg -i configuration-management_0.3.9.304_amd64.deb`.

## Скачать

- Windows (self-contained single-file): [ConfigurationManagement.exe](https://github.com/sivatorov/ConfigurationManagement/releases/download/v0.3.9.304/ConfigurationManagement.exe)
- Linux x64 (single-file): [ConfigurationManagement-linux-x64](https://github.com/sivatorov/ConfigurationManagement/releases/download/v0.3.9.304/ConfigurationManagement-linux-x64)
- Debian/Ubuntu: [configuration-management_0.3.9.304_amd64.deb](https://github.com/sivatorov/ConfigurationManagement/releases/download/v0.3.9.304/configuration-management_0.3.9.304_amd64.deb)
- Контрольные суммы: [SHA256SUMS.txt](https://github.com/sivatorov/ConfigurationManagement/releases/download/v0.3.9.304/SHA256SUMS.txt)

### SHA-256

```
2c87d2e8fda6876f23183c1061bab76d5c6d71009f6e88502e090aeaa281c188  ConfigurationManagement.exe
d2c2e499358c57ec5d872ed7f86de7fb8c1b7a40cbd685d086cc14f658fefea3  ConfigurationManagement-linux-x64
c02774276e67b4823b428fe5d741c05bf4b9a78ea3fd0e9370330ecf43e0db7b  configuration-management_0.3.9.304_amd64.deb