Расширенная диагностика в версии **0.3.9.311** (Windows/WPF и Linux/Avalonia; issue #340, кластер B).

**Что было.**

В присланном `trace.json` на 0.3.9.308 (комментарий от 2026-10-05) оказались только startup-запись
и 4 записи `MenuOpened`/`MenuClosed` — **ни одного события клика**. Причина: трассировка мыши была
условной — `MouseDown` писался только при записанном снимке клика, а `TryApply`/`Fallback`/
`EnsureStable`/`Dump500ms` — только при срабатывании соответствующих веток стабилизации. Если
снимок не записывался (обычный клик вне окна стабилизации, Ctrl/Shift-клик, промах мимо строки),
клик по дереву вообще не оставлял следов в файле, и по такому логу нельзя определить, какое звено
рвётся.

**Что сделано в 0.3.9.311** — правки только диагностические, поведение выбора НЕ менялось:

1. **Клики по дереву пишутся безусловно.** `MouseDown`/`MouseUp` (WPF,
   [`MainWindow.Events.cs`](https://github.com/sivatorov/ConfigurationManagement/blob/main/Configuration%20Management/Views/MainWindow.Events.cs))
   и `PointerPressed`/`PointerReleased` (Avalonia,
   [`MainWindow.Avalonia.Events.cs`](https://github.com/sivatorov/ConfigurationManagement/blob/main/Configuration%20Management/Views/MainWindow.Avalonia.Events.cs))
   — при ЛЮБОМ клике, включая «пути без снимка»: координаты (`x`, `y`), модификаторы, `target`
   (база под курсором или `null` при промахе), `snapshot`, `redelivery`, `pinned`. Единый формат
   записи — чистый метод
   [`BuildClickTraceLine`](https://github.com/sivatorov/ConfigurationManagement/blob/main/Configuration%20Management/Services/BatchSelectionHelper.cs)
   + [`FormatModifiers`](https://github.com/sivatorov/ConfigurationManagement/blob/main/Configuration%20Management/Services/BatchSelectionHelper.cs)
   в [`BatchSelectionHelper.cs`](https://github.com/sivatorov/ConfigurationManagement/blob/main/Configuration%20Management/Services/BatchSelectionHelper.cs) —
   используется И WPF, И Avalonia, записи платформ симметричны.
2. **`Activated`/`Deactivated` окна записываются всегда** (прежде — только при pending-состоянии):
   число открытых меню и время с последнего закрытия меню дерева
   ([`MainWindow.xaml.cs`](https://github.com/sivatorov/ConfigurationManagement/blob/main/Configuration%20Management/Views/MainWindow.xaml.cs) /
   [`MainWindow.Avalonia.cs`](https://github.com/sivatorov/ConfigurationManagement/blob/main/Configuration%20Management/Views/MainWindow.Avalonia.cs)).
3. **`MenuClosedCursor`** при закрытии меню дерева
   ([`MainWindow.Hotkeys.cs`](https://github.com/sivatorov/ConfigurationManagement/blob/main/Configuration%20Management/Views/MainWindow.Hotkeys.cs)):
   координаты курсора, признак «курсор над строкой дерева» (hit-test), «над пунктом меню», фокус
   окна — видно, ЧЕМ именно закрыто меню (кликом по строке / кликом мимо / выбором пункта / ESC).
4. **Стабилизация**: стартовая запись `EnsureStableStart` (причина `snapshot`|`recentMenuClose`,
   целевая база) и поля каждого прохода — `SelectedInfobase`, `containerIsSelected`,
   `selectedByData`, `containerFound`
   ([`MainWindow.Tree.cs`](https://github.com/sivatorov/ConfigurationManagement/blob/main/Configuration%20Management/Views/MainWindow.Tree.cs) /
   [`MainWindow.Avalonia.Events.cs`](https://github.com/sivatorov/ConfigurationManagement/blob/main/Configuration%20Management/Views/MainWindow.Avalonia.Events.cs)).
5. **Лимит файла увеличен до 1 МБ** (круговое усечение, было 512 КБ) —
   [`MenuCloseTraceFormat.MaxFileBytes`](https://github.com/sivatorov/ConfigurationManagement/blob/main/Configuration%20Management/Services/MenuCloseTraceFormat.cs) —
   трассировка стала плотнее.

**Как воспроизвести — точная инструкция** (нужно, чтобы в логе появились события клика):

1. Установите **0.3.9.311** (Windows или Linux).
2. Запустите приложение и **не трогайте ничего** — дождитесь появления файла **`trace.json`**
   рядом с настройками (Windows: `%APPDATA%\ConfigurationManagement\`,
   Linux: `~/.config/ConfigurationManagement/`). Файл создаётся при старте сам, включать его нигде
   не нужно.
3. Воспроизведите сценарий **10+ раз подряд**: мультивыделение (Ctrl/Shift) → правый клик по
   любой строке (контекстное меню) → левый клик по **другой** строке дерева. Между повторами
   делайте паузу **~2–3 секунды**.
4. **Закройте приложение** (важно: записи сбрасываются в файл по ходу, но закрытие фиксирует
   последние события).
5. Пришлите **полный** `trace.json` — приложите к комментарию целиком (до 1 МБ файл усекается
   сам; если размер близок к лимиту — можно упаковать архивом, но лучше файл как есть).

Ожидаемая цепочка для каждого повтора: `MenuOpened` → `MenuClosed` + `MenuClosedCursor` →
`MouseDown` (или `PointerPressed`) с `target=<id>` → `EnsureStableStart` → проходы `EnsureStable` →
`Dump500ms`. Также укажите, в скольких из 10 повторов выделение пропало и в какой секции
(обычный список / «Закреплённые»). По этим данным определим звено, где рвётся выбор, и выпустим
исправление.

**Тесты.** Полный набор `dotnet test` зелёный — **1792** (0 не пройдено), из них **5 новых** для
#340: единый формат строки клика WPF/Avalonia, форматирование модификаторов, `target=null` при
промахе, новый лимит усечения 1 МБ, валидная JSON-сериализация полей события клика без секретов
([`BatchSelectionHelperTests.cs`](https://github.com/sivatorov/ConfigurationManagement/blob/main/ConfigurationManagement.Tests/BatchSelectionHelperTests.cs),
[`MenuCloseTraceFormatTests.cs`](https://github.com/sivatorov/ConfigurationManagement/blob/main/ConfigurationManagement.Tests/MenuCloseTraceFormatTests.cs)).

Версия **0.3.9.311** опубликована:
[CHANGELOG](https://github.com/sivatorov/ConfigurationManagement/blob/main/CHANGELOG.md),
[релиз v0.3.9.311](https://github.com/sivatorov/ConfigurationManagement/releases/tag/v0.3.9.311)
(assets: ConfigurationManagement.exe, ConfigurationManagement Linux x64, .deb).