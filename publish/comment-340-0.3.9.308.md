Исправлено в версии **0.3.9.308** (Windows/WPF и Linux/Avalonia).

**Что было.**

После мультивыделения (или просто правого клика) по строке → левый клик по другой
строке → строка на мгновение становится активной, а затем выделение пропадает.
Попытка диагностики в 0.3.9.306 не сработала: файл `menuclose_trace.json` не появлялся
вообще, потому что startup-запись выполнялась только внутри `MenuCloseTrace.Log()`, а
все вызовы `Log()` были условными (guard-цепочка `TryApplyTreeClickAfterMenuClosed`
включала проверку «кнопка мыши нажата», позиции в дереве, клик без модификаторов и т.д.).
События открытия/закрытия контекстного меню в трассировку не попадали вовсе — при
закрытии меню по ESC или кликом мимо строки записей не было ни одной. Плюс имя файла
(`menuclose_trace.json`) не совпало с ожидаемым вами (`trace.json`).

Поскольку снимок клика не записывался, стабилизация `IsSelected` (`EnsureSelectionStable`)
не запускалась, а переработка контейнеров виртуализацией (`VirtualizingStackPanel`,
`Recycling`) сбрасывала подсветку строки — отсюда «пропадает через мгновение».

**Как исправлено.**

1. **Файл диагностики создаётся при каждом старте.** Новый `MenuCloseTrace.EnsureStarted()`
   вызывается из конструктора/OnLoaded главного окна (обе платформы) и пишет startup-запись
   (версия, платформа, ОС, каталог данных) независимо от действий пользователя. Основное имя
   файла — **`trace.json`** (по вашему предложению), рядом с настройками приложения
   (Windows: `%APPDATA%\ConfigurationManagement\`, Linux: `~/.config/ConfigurationManagement\`,
   в портативном режиме — каталог данных рядом с exe). Если рядом уже существует legacy
   `menuclose_trace.json` (от 0.3.9.306) — журнал продолжает дописываться в него, чтобы не
   потерять историю диагностики.
   ([`MenuCloseTrace.cs`](Configuration%20Management/Services/MenuCloseTrace.cs),
   [`MenuCloseTraceFormat.cs`](Configuration%20Management/Services/MenuCloseTraceFormat.cs))
2. **Безусловные записи `MenuOpened`/`MenuClosed`** при каждом открытии/закрытии любого
   контекстного меню (WPF — класс-обработчики `OnContextMenuOpened`/`OnContextMenuClosed`;
   Avalonia — класс-обработчик `ContextMenu.IsOpenProperty.Changed`). При закрытии меню
   дерева дополнительно фиксируется метка времени `_lastMenuCloseTick`
   ([`MainWindow.Hotkeys.cs`](Configuration%20Management/Views/MainWindow.Hotkeys.cs),
   [`MainWindow.Avalonia.Events.cs`](Configuration%20Management/Views/MainWindow.Avalonia.Events.cs)).
3. **Стабилизация выделения расширена.** Чистый предикат
   `BatchSelectionHelper.ShouldStabilizeAfterMenuClose(...)` запускает стабилизацию
   `IsSelected` не только при наличии снимка клика, но и для **любого обычного клика без
   модификаторов** в окне ~1,5 с после закрытия контекстного меню дерева. WPF — ветка
   обычного клика в `OnInfobaseTree_PreviewMouseLeftButtonDown`; Avalonia — расширенный
   признак в `OnTreeMenuCloseClickDedup_PointerPressed` с отложенным запуском (чтобы не
   конкурировать со штатным применением выбора контролом). Ctrl/Shift-клики (мультивыделение)
   стабилизацией не затрагиваются
   ([`BatchSelectionHelper.cs`](Configuration%20Management/Services/BatchSelectionHelper.cs),
   [`MainWindow.Events.cs`](Configuration%20Management/Views/MainWindow.Events.cs)).
4. Соглашение о файле трассировки задокументировано в README.

**Как проверить.**

1. Обновитесь до **0.3.9.308** и запустите приложение. Рядом с настройками появится
   **`trace.json`** со startup-записью (создавать/включать файл вручную НЕ нужно).
2. Повторите сценарий: мультивыделение (Ctrl/Shift) **или просто ПКМ** по строке →
   левый клик по другой строке. В `trace.json` появятся записи
   `MenuOpened` → `MenuClosed` → `MouseDown`(или `PointerPressed`) → `Dump500ms`/`EnsureStable`.
3. Строка, по которой кликнули, остаётся активной; выделение НЕ пропадает «через мгновение».
   Повторите 10+ раз на Windows и Linux.

**Тесты.** Чистая логика покрыта юнит-тестами:
[`MenuCloseTraceFormatTests.cs`](ConfigurationManagement.Tests/MenuCloseTraceFormatTests.cs)
(выбор имени файла `trace.json` vs legacy `menuclose_trace.json`, формат startup-записи) и
[`BatchSelectionHelperTests.cs`](ConfigurationManagement.Tests/BatchSelectionHelperTests.cs)
(5 тестов предиката `ShouldStabilizeAfterMenuClose`: снимок, окно ~1,5 с, вне окна, без
закрытого меню, Ctrl-клик). Полный набор `dotnet test` зелёный; сборка Windows и кросс-сборка
Linux (`dotnet build -p:BuildLinux=true`) без ошибок. Поведение оконного стека (попапы,
виртуализация) юнит-тестами не покрывается — требует подтверждения на вашей машине.

Версия **0.3.9.308** опубликована:
[CHANGELOG](https://github.com/sivatorov/ConfigurationManagement/blob/main/CHANGELOG.md),
[релиз v0.3.9.308](https://github.com/sivatorov/ConfigurationManagement/releases/tag/v0.3.9.308)
(assets: ConfigurationManagement.exe, ConfigurationManagement Linux x64, .deb; релиз включает
также исправления входа на portal.1c.ru из 0.3.9.307 — #323/#330/#334).