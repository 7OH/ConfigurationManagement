# PLAN 0.3.9.311 — Кластер B: снятие выделения после закрытия контекстного меню (#340)

- Дата: 2026-10-05. Режим: **архитектор** — только технический разбор; код НЕ изменяется.
- Основание: сводный [`plans/PLAN-0.3.9.310-312.md`](PLAN-0.3.9.310-312.md), задача T2; вход — полный текст `publish/issue_340_full.md` (20 комментариев, последний 7OH 2026-10-05T11:58:02Z с содержимым `trace.json` на 0.3.9.308).
- Версия: **0.3.9.311** (микро-версия кластера B; комментарий в #340 после релиза, issue не закрывать).
- Проект двухплатформенный: WPF (`#if WINDOWS`) / Avalonia (`#if LINUX`); трассировка и чистая логика — общие (`MenuCloseTrace*`, `BatchSelectionHelper`).

---

## B1. Диагноз по коду

### B1.1 Что показал первый реальный trace.json (0.3.9.308, комментарий 20 7OH)

Файл создался (startup-запись работает) — это прогресс против 0.3.9.306. Содержимое:

```
{"event":"startup","data":{"version":"0.3.9.308","platform":"WPF","os":"Microsoft Windows 10.0.26300","appDataDir":"C:\Users\Semion\AppData\Roaming\ConfigurationManagement"}}
{"event":"log","data":{"message":"MenuOpened: menuId=ContextMenu, isTreeMenu=True"}}
{"event":"log","data":{"message":"MenuClosed: menuId=ContextMenu, isTreeMenu=True"}}
{"event":"log","data":{"message":"MenuOpened: menuId=ContextMenu, isTreeMenu=True"}}
{"event":"log","data":{"message":"MenuClosed: menuId=ContextMenu, isTreeMenu=True"}}
```

Выводы:

1. **Безусловные записи `MenuOpened`/`MenuClosed` работают** ([`MainWindow.Hotkeys.cs:718/730`](Configuration%20Management/Views/MainWindow.Hotkeys.cs:718)) — 4 записи за сессию 14:23–14:24 МСК.
2. **Нет НИ ОДНОЙ записи события клика** (`MouseDown`/`TryApply`/`Fallback`/`EnsureStable`/`Dump500ms`). Точки вызова (0.3.9.309):
   - [`MainWindow.Events.cs:656`](Configuration%20Management/Views/MainWindow.Events.cs:656) `MouseDown` — **только при `snapshotPresent`**;
   - [`MainWindow.Hotkeys.cs:836`](Configuration%20Management/Views/MainWindow.Hotkeys.cs:836) `TryApply` — только после длинной guard-цепочки `TryApplyTreeClickAfterMenuClosed` (мышь над деревом, левая кнопка нажата, клик без модификаторов, hit-test по строке);
   - `Fallback`/`EnsureStable`/`Dump500ms` — только при срабатывании соответствующих веток стабилизации.
3. **Интервалы записей**: MenuOpened 14:23:51 → MenuClosed 14:23:54 (3 c) → MenuOpened 14:23:57 → MenuClosed 14:23:59. Между закрытием и следующим открытием ~3 с. Если бы пользователь кликнул по строке дерева в окне стабилизации (1,5 с после MenuClosed, WPF: `ShouldStabilizeAfterMenuClose` → `EnsureSelectionStable`), в лог попали бы записи `EnsureStable: …`. Их нет.

Вывод B1.1: **присланная сессия НЕ содержит воспроизведения проблемного сценария** (клик по строке дерева после закрытия меню либо не выполнялся, либо был вне окна 1,5 с, либо с модификаторами). Данных о звене, где рвётся выделение, пока недостаточно. Дополнительно сам факт отсутствия записей клика — признак того, что трассировка событий клика слишком «условная»: при незаписанном снимке (путь C в терминологии 0.3.9.305) клик по строке вообще не оставляет следов в trace.json.

### B1.2 Точки, которых НЕ хватает в текущей трассировке

| Событие | Сейчас | Нужно (следующая итерация) |
|---|---|---|
| `MouseDown`/`PointerPressed` по дереву | только при `snapshotPresent` или `stabilizeRequested` | **безусловно** (координаты, targetId или промах, модификаторы, секция, hit-test результат) |
| `MouseUp`/`PointerReleased` по дереву | только при снимке | безусловно |
| `Deactivated`/`Activated` окна | только при pending/снимке | всегда (в т.ч. и при отсутствии снимка) |
| Стабилизация: старт | нет отдельной записи «запущена» | запись причины (snapshot / recentMenuClose) и target |
| Стабилизация: каждый проход | `pass=…, matches=…, containerRealized=…` — есть | добавить `SelectedInfobase.Id`, `container.IsSelected`, `IsVisible` |
| Итог через ~500 мс | `Dump500ms` только при снимке | при любом закрытии меню дерева |
| Внутреннее состояние пунктов меню | — | координаты мыши на момент MenuClosed (под строкой/над пунктом/мимо) |

### B1.3 Что делать — две опции

- **Опция B-план (рекомендуемая)**: выпустить 0.3.9.311 с расширенной безусловной трассировкой (B2) И одновременно опубликовать комментарий с точной инструкцией воспроизведения (B5.2). Следующий trace.json гарантированно покажет все звенья клика даже при «путях без снимка».
- Опция B-вопрос (без новой версии): только комментарий «воспроизведите сценарий на 0.3.9.309 и пришлите trace.json». Дешевле, но если у пользователя снимок снова не запишется (guard-цепочка не пройдена) — trace снова окажется пустым от событий клика, и мы потеряем итерацию. Принимаем только как fallback, если реализация 0.3.9.311 задержится.

---

## B2. Предлагаемые изменения (0.3.9.311)

| № | Файл | Что изменить |
|---|------|--------------|
| B-1 | [`Views/MainWindow.Events.cs`](Configuration%20Management/Views/MainWindow.Events.cs:656) (WPF) | В `OnInfobaseTree_PreviewMouseLeftButtonDown` писать `MouseDown` **безусловно** (до любых веток): координаты, targetId (`Unwrap(rowItem.DataContext)` или null при промахе), модификаторы, `snapshotPresent`, `isMenuCloseRedelivery`, секция (`IsPinnedSection`). В `OnInfobaseTree_PreviewMouseLeftButtonUp` — безусловная запись `MouseUp`. |
| B-2 | [`Views/MainWindow.Avalonia.Events.cs`](Configuration%20Management/Views/MainWindow.Avalonia.Events.cs:241) (Avalonia) | Зеркально B-1: безусловные `PointerPressed`/`PointerReleased` по дереву с теми же полями (координаты, target, modifiers, snapshot, redelivery, pinned). |
| B-3 | [`Views/MainWindow.xaml.cs:110`](Configuration%20Management/Views/MainWindow.xaml.cs:110) (WPF) | `Deactivated`/`Activated` — записывать **всегда** (без условия на pending/снимок): `openMenusCount`, `IsActive`, время с последнего `MenuClosed` (`timeSinceMenuCloseMs`). Аналогично Avalonia ([`MainWindow.Avalonia.cs:331`](Configuration%20Management/Views/MainWindow.Avalonia.cs:331)). |
| B-4 | [`Views/MainWindow.Hotkeys.cs:730`](Configuration%20Management/Views/MainWindow.Hotkeys.cs:730) (WPF) + Avalonia | В `OnContextMenuClosed` для меню дерева дополнительно записать: координаты курсора (`GetCursorPos`/эквивалент), флаг «курсор над строкой дерева» (hit-test по координатам), `IsKeyboardFocusWithin`. Это покажет, чем именно закрыто меню (кликом по строке / кликом мимо / ESC / выбором пункта). |
| B-5 | [`Views/MainWindow.Tree.cs:852`](Configuration%20Management/Views/MainWindow.Tree.cs:852) + [`MainWindow.Avalonia.Events.cs`](Configuration%20Management/Views/MainWindow.Avalonia.Events.cs:432) | В `EnsureSelectionStable`: стартовая запись `EnsureStableStart: причина=snapshot|recentMenuClose, target=…`; в каждый проход добавлять `SelectedInfobase={Id}`, `containerIsSelected={…}`; после `SelectTreeRowByData` — запись `selectedByData=true/false` и `containerFound`. |
| B-6 | [`Services/BatchSelectionHelper.cs`](Configuration%20Management/Services/BatchSelectionHelper.cs) | Новый чистый метод для единообразной сборки строки события клика (координаты, mods, target, path) — используется и WPF, и Avalonia, покрывается тестами. Сигнатуры существующих методов (`IsSameClick`, `ShouldRecordMenuCloseSnapshot`, `ShouldStabilizeAfterMenuClose`, `DecideSelectionRestore`) НЕ меняются. |
| B-7 | [`Services/MenuCloseTrace.cs`](Configuration%20Management/Services/MenuCloseTrace.cs) / [`MenuCloseTraceFormat.cs`](Configuration%20Management/Services/MenuCloseTraceFormat.cs) | Без изменения API. При необходимости увеличить лимит кругового усечения с 512 КБ до 1 МБ (трассировка стала плотнее): только константа `MaxFileBytes` + тесты формата. |
| B-8 | README | Обновить раздел диагностики `trace.json`: перечень событий и смысл полей (`MouseDown`/`PointerPressed` теперь пишутся безусловно). |
| B-9 | Локализация | Не требуется (трассировка — только журнал). |

Примечание B-1/B-2: безусловная запись **не меняет логику выбора** — `Log()` внутри трассы пишет в файл без влияния на обработчики. Отдельное внимание: запись выполняется в UI-потоке и не должна ронять приложение (уже обёрнута try/catch в `MenuCloseTrace.Log`).

---

## B3. Юнит-тесты

- [`MenuCloseTraceFormatTests.cs`](ConfigurationManagement.Tests/MenuCloseTraceFormatTests.cs):
  - `MaxFileBytes` — обновить тесты усечения под новый лимит (если B-7 принят);
  - `FormatClickEventFields_ValidJson` — сериализация строки события клика (поля: x, y, modifiers, target, snapshot, redelivery, pinned) валидна и не содержит секретов.
- [`BatchSelectionHelperTests.cs`](ConfigurationManagement.Tests/BatchSelectionHelperTests.cs):
  - `BuildClickTraceLine_WpfAndAvalonia_SameShape` — единый формат (B-6);
  - регресс существующих тестов предикатов/дедупликации — сигнатуры не меняются.
- Прогон полного набора `dotnet test`; кросс-сборка Linux. Оконное поведение по-прежнему проверяется пользователем (B5.2).

---

## B4. Риски и fallback

| Риск | Влияние | Митигация / fallback |
|------|---------|----------------------|
| Пользователь снова не воспроизведёт сценарий (меню откроет/закроет без клика по строке) | trace без событий клика | Точная инструкция B5.2 + записи координат курсора на MenuClosed (B-4): будет видно, куда указывала мышь; просьба выполнить сценарий 10+ раз. |
| Баг воспроизводится, но trace покажет, что стабилизация срабатывала и выбор применялся | Гипотезы A/B/C (повторная доставка, виртуализация, деактивация) исчерпаны | По журналу определить конкретное звено: `EnsureStableStart/…/Dump500ms` (последовательность и поля) — следующая итерация по фактам, а не гипотезам. |
| Клик по строке вообще не доходит до дерева (попап съедает) | Нет записей MouseDown в дереве | Именно это покажет безусловный лог (B-1): если `MouseDown` нет, а `MenuClosed`+координаты над строкой есть — попап съел клик, меняем стратегию применения выбора (не трогая дедупликацию). |
| Расширенная трассировка разрастётся | Файл быстро усекается | Увеличение лимита до 1 МБ (B-7) + события редки (только меню/клики). |
| Различия платформ (WPF/Avalonia) | Записи асимметричны | Зеркальные правки; единый формат через B-6; ручная проверка на обеих платформах. |

---

## B5. Критерии готовности

### B5.1 Технические

- `dotnet test` зелёный (включая B3); кросс-сборка Linux без ошибок.
- `trace.json` при старте создаётся; `MouseDown`/`PointerPressed` и `MouseUp`/`PointerReleased` по дереву пишутся безусловно; `Deactivated`/`Activated` — всегда.
- При закрытии меню дерева записываются координаты курсора и признак «над строкой».
- Стабилизация пишет стартовую запись с причиной и итоговые поля (`SelectedInfobase`, `containerIsSelected`).

### B5.2 Ручные (пользователь 7OH — публикуются в комментарии к #340 после релиза 0.3.9.311)

1. Установить 0.3.9.311; запустить; убедиться, что `%APPDATA%\ConfigurationManagement\trace.json` появился со startup-записью.
2. **Воспроизвести сценарий минимум 10 раз**: выделить несколько строк (Ctrl/Shift) → ПКМ по любой строке (контекстное меню) → ЛКМ по ДРУГОЙ строке дерева → пауза 2–3 сек → зафиксировать, исчезло ли выделение.
3. Повторить **простой кейс** (без мультивыделения): ПКМ по строке → ЛКМ по другой строке → пауза.
4. Закрыть приложение и прислать **полный** `trace.json` (файл рядом с настройками; можно без старой истории — записи последней сессии достаточно).
5. Указать в комментарии: сколько раз воспроизведён баг из 10, и в каких секциях (обычный список / «Закреплённые»).

Что именно ожидается увидеть в trace (конкретные точки):
- для каждого повтора — `MenuOpened` → `MenuClosed` (+координаты/над строкой) → `MouseDown` (или `PointerPressed`) с `target=<id>` → `EnsureStableStart` → проходы `EnsureStable` → `Dump500ms` с итоговым `selectedItemId`;
- если баг воспроизводится — цепочка, где `selectedItemId` в `Dump500ms` отличается от `target` или `containerIsSelected=false`.

---

## B6. Перечень затрагиваемых файлов

- [`Configuration Management/Views/MainWindow.Events.cs`](Configuration%20Management/Views/MainWindow.Events.cs) — B-1.
- [`Configuration Management/Views/MainWindow.Avalonia.Events.cs`](Configuration%20Management/Views/MainWindow.Avalonia.Events.cs) — B-2, B-5.
- [`Configuration Management/Views/MainWindow.xaml.cs`](Configuration%20Management/Views/MainWindow.xaml.cs) / [`MainWindow.Avalonia.cs`](Configuration%20Management/Views/MainWindow.Avalonia.cs) — B-3.
- [`Configuration Management/Views/MainWindow.Hotkeys.cs`](Configuration%20Management/Views/MainWindow.Hotkeys.cs) — B-4.
- [`Configuration Management/Views/MainWindow.Tree.cs`](Configuration%20Management/Views/MainWindow.Tree.cs) — B-5.
- [`Configuration Management/Services/BatchSelectionHelper.cs`](Configuration%20Management/Services/BatchSelectionHelper.cs) — B-6.
- [`Configuration Management/Services/MenuCloseTraceFormat.cs`](Configuration%20Management/Services/MenuCloseTraceFormat.cs) — B-7 (опционально).
- [`ConfigurationManagement.Tests/MenuCloseTraceFormatTests.cs`](ConfigurationManagement.Tests/MenuCloseTraceFormatTests.cs), [`ConfigurationManagement.Tests/BatchSelectionHelperTests.cs`](ConfigurationManagement.Tests/BatchSelectionHelperTests.cs) — B3.
- README — B-8.

---

## B7. Порядок передачи в задачу-исполнитель (code)

1. Реализация строго по B-1…B-9; версия csproj → `0.3.9.311` (4 поля); CHANGELOG `## [0.3.9.311]`; README.
2. `dotnet test` + кросс-сборка Linux.
3. Сборка артефактов и релиз — по шаблону сводного плана (T9/T10).
4. Публикация комментария в **#340** (черновик `publish/comment-340-0.3.9.311.md`): что расширено в трассировке, точная инструкция B5.2, ссылка на релиз. Issue не закрывать.