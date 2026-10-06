# Результат сборки 0.3.9.314 (issue #340)

Дата сборки: 2026-10-06 (UTC+3, Europe/Moscow)
Версия csproj: `0.3.9.314` (`Version`, `AssemblyVersion`, `FileVersion`, `InformationalVersion`)
Среда: Windows 11, .NET SDK 10.0.401 (>= 10.0.400), кросс-сборка Linux из Windows

## Что сделано (кластер B, issue #340 «Снятие выделения после контекстного меню», девятая итерация)

Файлы изменений:

| Файл | Изменение |
|---|---|
| `Configuration Management/Services/BatchSelectionHelper.cs` | Константа `MenuClosePrecedingClickWindowMs = 500` + чистый предикат `ShouldStabilizeForClickPrecedingMenuClose` |
| `Configuration Management/Views/MainWindow.Events.cs` | Сохранение «последнего обычного клика по строке» (`_lastPlainTreeClick`) в ветке `ApplySelection` + запись `LastPlainClick` в trace.json |
| `Configuration Management/Views/MainWindow.Hotkeys.cs` | Поле `_lastPlainTreeClick`; в `OnContextMenuClosed` — запуск `EnsureSelectionStable(reason="clickBeforeMenuClose")` отложенно (Input), если снимок не записан и клик ≤500 мс до закрытия меню дерева |
| `Configuration Management/Views/MainWindow.Avalonia.Events.cs` | Зеркально: поле `_lastPlainTreeClick`, запись в туннельной фазе `PointerPressed`, запуск стабилизации в `ContextMenu.IsOpenProperty.Changed` (`Dispatcher.UIThread.Post`) |
| `ConfigurationManagement.Tests/BatchSelectionHelperTests.cs` | +4 новых сценария предиката; существующие — регресс без изменений |
| `Configuration Management/Configuration Management.csproj` | Версия → `0.3.9.314` (4 поля) |
| `CHANGELOG.md`, `README.md` | Запись 0.3.9.314, бейдж версии, цепочка `clickBeforeMenuClose` в описании trace.json |

Детали реализации (по плану `plans/PLAN-0.3.9.314.md`):

1. **Диагноз по второму реальному trace.json (0.3.9.311, комментарий 23):** MouseDown по строке
   приходит в дерево ДО закрытия меню (`MenuClosed` ~555 мс позже), а не повторной доставкой ПОСЛЕ.
   Снимок клика не записывается (`snapshot=False`): к моменту `OnContextMenuClosed` левая кнопка уже
   отпущена (guard `Mouse.LeftButton != Pressed`). Повторной доставки «хвоста» нет (`redelivery=False`).
   Стабилизация не запускается ни одним штатным путём — переработка контейнеров виртуализацией
   (`VirtualizingStackPanel`, `Recycling`) сбрасывает `IsSelected` «через мгновение».
2. **Новый чистый предикат** `ShouldStabilizeForClickPrecedingMenuClose(lastPlainClickTick,
   menuCloseTick, windowMs)`: обычный клик был (`lastPlainClickTick > 0`), закрытие не раньше клика
   (`menuCloseTick >= lastPlainClickTick`) и клик непосредственно перед закрытием
   (`menuCloseTick - lastPlainClickTick <= windowMs`, окно 500 мс). Старый предикат
   `ShouldStabilizeAfterMenuClose` (клики ПОСЛЕ закрытия, окно ~1,5 с) НЕ изменён.
3. **WPF:** в ветке обычного клика (`OnInfobaseTree_PreviewMouseLeftButtonDown` → `ApplySelection`)
   запоминается `_lastPlainTreeClick = (Environment.TickCount, infobase, isPinnedSection)` с записью
   `LastPlainClick: target=…, tick=…`. В `OnContextMenuClosed` для меню дерева: если
   `_menuCloseClickSnapshot is null` и предикат истинен — `Dispatcher.BeginInvoke(Input)` →
   `EnsureSelectionStable(target, isPinnedSection, reason: "clickBeforeMenuClose")`; поле очищается
   после использования. Отложенный запуск даёт компоновке после закрытия попапа устаканиться;
   стабилизация идемпотентна (15 проходов / 1,5 с) и не трогает мультивыделение.
4. **Avalonia/Linux (зеркально):** запись `_lastPlainTreeClick` в туннельной фазе
   `OnTreeMenuCloseClickDedup_PointerPressed` (обычный левый клик без модификаторов по строке базы),
   запуск стабилизации в `OnTreeContextMenuIsOpenChanged` при закрытии меню дерева через
   `Avalonia.Threading.Dispatcher.UIThread.Post`.
5. **Трассировка 0.3.9.311 сохранена:** безусловные `MenuOpened`/`MenuClosed`/`MenuClosedCursor`,
   безусловные `MouseDown`/`MouseUp` и `PointerPressed`/`PointerReleased`, `TryApply`/`Fallback`/
   `Dump500ms`/`EnsureStableStart` — не изменялись. Добавлены только записи `LastPlainClick` и
   причина `clickBeforeMenuClose` в стартовой записи стабилизации. Подготовка к 0.3.9.315 (#347,
   перевод безусловной диагностики под флаги) НЕ выполнена — по плану это следующий кластер.

## Тесты

- `dotnet test` (полный прогон): **1813 пройдено, 0 не пройдено** (было 1809 в 0.3.9.313; +4 новых
  сценария `ShouldStabilizeForClickPrecedingMenuClose`: в окне 200 мс → true, граница 500 мс → true;
  за 3 с / за окном → false; без клика (0) → false; закрытие раньше клика → false). Существующие
  сценарии (идемпотентность `DecideSelectionRestore`, `Stabilization_DoesNotTouchBatchSelection`,
  `ShouldStabilizeAfterMenuClose_*`, `ShouldRetryRestore_*`, `IsSameClick_*`, диагностика
  `BuildClickTraceLine`/`FormatModifiers`) — без изменений, регресс подтверждён.
- Кросс-сборка Linux (`dotnet build -p:BuildLinux=true`): **0 ошибок** (6 предупреждений nullable,
  включая прежние; новое предупреждение CS8602 в `MainWindow.Avalonia.Events.cs` — в существующем
  коде дампа `Dump500ms` после `Dispatcher.UIThread.Post`, nullable-анализ поля `_tree`, к изменениям
  этого кластера не относится).

## Особенности среды сборки WPF (важно для следующих задач)

В этой рабочей копии (путь на `f:\Yandex.Disk\...` — синхронизируемый диск) WPF-сборка
(`net10.0-windows`) на ХОЛОДНОМ `obj` может падать с CS0103 `InitializeComponent` во временной
сборке `*_wpftmp.csproj`: `MarkupCompilePass1` генерирует `.g.cs` в `obj`, но
`GenerateTemporaryTargetAssembly` их не видит (задержка видимости свежесозданных файлов
overlay-драйвером). Обходные приёмы, которые применялись для полного прогона:

1. Сборка выполняется с `-p:BuildingInsideVisualStudio=true` (меняет поведение временной сборки WPF).
2. После очистки `obj`/`bin` основную WPF-сборку выполнять ДВАЖДЫ подряд: первая создаёт `.g.cs`
   (может завершиться ошибкой), вторая — инкрементально — находит их и проходит успешно.
3. Порядок: основной проект → тестовый проект → `dotnet test --no-build`
   (параллельная сборка через slnx может уходить в гонку за WPF-ref DLL `obj\...\ref\`).
4. Кросс-сборка Linux (`-p:BuildLinux=true`) использует общий `obj`; после неё перед следующей
   Windows/WPF-сборкой очищать `obj`/`bin` (иначе WPF temp-сборка ломается).

## Примечания

- Релиз v0.3.9.314 и публикация комментария в #340 — ОТДЕЛЬНАЯ задача; в этой задаче релиз не
  создавался, issue не закрывался, комментарий не публиковался (черновик —
  `publish/comment-340-0.3.9.314.md`).
- Коммит не выполнялся (без пуша).
- Сводный план версий 0.3.9.313–0.3.9.315 — `plans/PLAN-0.3.9.313-315.md`; порядок кластеров
  соблюдён: 0.3.9.315 (#347) идёт следующим и переведёт безусловную диагностику под флаги —
  изменения этого кластера трассировку не ломают.