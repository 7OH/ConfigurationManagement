# PLAN 0.3.9.298 — исправление issues #343 (виснет при старте), #344 (имена файлов сценариев), #309 (горизонтальный скрол)

Дата: 2026-10-03. Текущая версия: **0.3.9.297** (коммит 53eb4e7). Следующая микро-версия: **0.3.9.298**.
Двуплатформенный проект: Windows/WPF (`#if WINDOWS`, `*.xaml`/`*.xaml.cs`) и Linux/Avalonia (`#if LINUX`, `*.Avalonia.cs`). Common-логика (сервисы, модели, ViewModel) — общая.

**Ограничения реализации:** только код + юнит-тесты; issues не закрывать, ничего не коммитить. Реализация выполняется другой задачей в code-режиме строго по этому плану.

**Политика версии:** одна микро-версия **0.3.9.298** на весь пакет правок (обоснование — см. раздел 4.0). Сборка выпуска (артефакты Windows/Linux, .deb, релиз GitHub, комментарии в issues) — вне этой задачи, шаги описаны в разделе 6.

---

## 1. Issue #343 «3.9.296 виснет при старте» — корень цикла CM_COLUMNS и зависания

### 1.1 Диагноз по фактическому логу (скопирован из issue #343 целиком)

Записи идут с периодом ~0,1–0,5 с бесконечно, `rows=0` (дерево ещё строится во время «загрузки избранного»). Ключевые факты из парных строк лога:

```
… total=1023,2, sumActualHeader=1023,2, content=0,0, presenterMin=1023,2, viewport=1040,0, extent=1120,2, scrollable=80,2, vSb=Visible, panel=VirtualizingStackPanel
… total=1040,0, sumActualHeader=1023,2, content=0,0, presenterMin=1040,0, viewport=1023,2, extent=1120,2, scrollable=97,0, vSb=Visible, panel=StackPanel
… total=1023,2, … viewport=1040,0, … panel=StackPanel
… total=1023,2, … viewport=1040,0, … panel=VirtualizingStackPanel   (и так далее)
```

Наблюдения:

1. **`panel=` чередуется** между `VirtualizingStackPanel` и `StackPanel` каждые 1–2 строки → [`ApplyTreePanelStrategy`](Configuration%20Management/Views/MainWindow.Columns.cs:901) **флипает** стратегию панели туда-обратно. Каждое переключение вызывает [`MainTree.InvalidateMeasure()`](Configuration%20Management/Views/MainWindow.Columns.cs:929) (полный перезамер дерева) и [`LogColumnsDiagnostics(...)`](Configuration%20Management/Views/MainWindow.Columns.cs:930) (одна запись лога). Это и есть и источник разрастания лога до 700 МБ, и причина «виснет»: при большом дереве повторные смены ItemsPanelTemplate пересоздают контейнеры строк на каждый цикл, а цикл живёт бесконечно на UI-потоке.
2. **`total`/`presenterMin` осциллируют ровно на 16,8 = `sbw`** (ширина вертикального скроллбара): состояние A — `total=1023,2, presenterMin=1023,2, viewport=1040,0`; состояние B — `total=1040,0, presenterMin=1040,0, viewport=1023,2`. Значит условие флипа [`needHorizontal = total > viewport + 1`](Configuration%20Management/Views/MainWindow.Columns.cs:905) попеременно истинно/ложно, потому что `total` и `viewport` меняются антикоррелированно ровно на `sbw`.
3. **Механизм осцилляции — связка трёх мест:**
   - [`UpdateTreeMinWidth`](Configuration%20Management/Views/MainWindow.Columns.cs:785): «Эксперимент B» (строки 833–845) добавляет `sbw` к `effective`, когда `headerSumNow + sbw > viewport + 1`. На границе (`viewport ≈ headerSumNow + sbw`) критерий не имеет запаса и переключается в обе стороны.
   - [`presenter.MinWidth = effective`](Configuration%20Management/Views/MainWindow.Columns.cs:856) — запись **безусловная** (в отличие от строк, защищённых `_lastTreeMinWidth`). Изменение `MinWidth` презентера меняет желаемую ширину контента → `ExtentWidth` внутреннего ScrollViewer → новый `ScrollChanged`.
   - [`OnTreeScroll_ScrollChanged`](Configuration%20Management/Views/MainWindow.Scroll.cs:247): при `ViewportWidthChange/HeightChange != 0` зовёт [`SyncHeaderWidthWithList`](Configuration%20Management/Views/MainWindow.Columns.cs:636), при `ExtentWidthChange != 0` — снова `UpdateTreeMinWidth`. Замыкается цикл: `presenterMin → extent → ScrollChanged → SyncHeaderWidthWithList (HeaderGrid.Width = max(extent, viewport)) → headerSumNow ≈ HeaderGrid.Width → effective → presenterMin`.
   - Дополнительно `syncHeaderWidthWithList` (строка 658) ставит `HeaderGrid.Width = target` от **живого `ExtentWidth`** — это прямое звено обратной связи, из-за которого сумма колонок «догоняет» extent.
4. **Запуск усугубляет**: [`CountVisibleTreeItems`](Configuration%20Management/Views/MainWindow.Columns.cs:939) во время старта ненадёжен (дерево не построено, `rows=0`), а решение о переходе на обычный `StackPanel` принимается по этому числу — панель может включиться преждевременно и материализовать всё дерево (потеря виртуализации при тысячах баз → «виснет при старте»).

Итог: бесконечный layout-цикл `presenterMin ↔ viewport/extent ↔ HeaderGrid.Width ↔ effective` на границе `viewport ≈ total + sbw`, усиленный флипом ItemsPanel на каждом пересечении порога.

### 1.2 Исправления (#343 + защита для #309)

Все изменения — только WPF (`MainWindow.Columns.cs`, `MainWindow.Scroll.cs`); Avalonia-аналог (`MainWindow.Avalonia.Columns.cs`, `SyncListWidthToViewport`) уже защищён допуском 0,5 и логов не пишет, изменений не требует.

#### A1. Разорвать связку `HeaderGrid.Width ← extent` (главный фикс цикла)

Файл: `Configuration Management/Views/MainWindow.Columns.cs`, метод `SyncHeaderWidthWithList` (строки 636–661).

Шаги:
1. Целевую ширину заголовка считать от **расчётной** суммы колонок, а не от живого `ExtentWidth` внутреннего ScrollViewer: `target = Math.Max(UpdateTreeMinWidthTotal(), viewport)` вместо `Math.Max(extent, viewport)`. Длинные названия уже учтены в `total` через `_treeMinWidthContent` (передаётся в [`ListMinWidthCalculator.Compute`](Configuration%20Management/Views/MainWindow.Columns.cs:818)), поэтому потеря контента не происходит.
2. Для этого вынести вычисление `total` из `UpdateTreeMinWidth` в отдельный приватный метод `double ComputeTreeMinWidthTotal()` (без побочных эффектов: без записи `presenter.MinWidth`/`HeaderGrid.Width`), чтобы и `UpdateTreeMinWidth`, и `SyncHeaderWidthWithList` использовали один источник значения.
3. Обновить XML-комментарий: цель — убрать обратную связь «extent → ширина заголовка → сумма колонок → presenterMin → extent».

Критерий: `HeaderGrid.Width` больше не растёт от собственного влияния на extent; цикл на границе `viewport ≈ total + sbw` не может самоподдерживаться.

#### A2. Защита `presenter.MinWidth` от записи без изменения

Файл: `Configuration Management/Views/MainWindow.Columns.cs`, метод `UpdateTreeMinWidth` (строки 854–856).

Шаги:
1. Ввести поле `private double _lastPresenterMinWidth = -1;` рядом с `_lastTreeMinWidth`.
2. Записывать `presenter.MinWidth = effective` **только** при `Math.Abs(effective - _lastPresenterMinWidth) > 0.5` (как уже сделано для строк). Иначе вызов `UpdateTreeMinWidth` из `ExtentWidthChange`-ветки `OnTreeScroll_ScrollChanged` не должен инвалидировать раскладку.

Критерий: одинаковое `effective` не вызывает повторную раскладку; `ExtentWidthChange != 0` больше не запускает каскад.

#### A3. Гистерезис и дебаунс решения о панели (убить флип VirtualizingStackPanel/StackPanel)

Файл: `Configuration Management/Views/MainWindow.Columns.cs`, метод `ApplyTreePanelStrategy` (строки 901–936).

Шаги:
1. Ввести два поля состояния:
   - `private bool _treePanelSwitchQueued;` — признак отложенного переключения (дебаунс);
   - `private DateTime _lastTreePanelSwitchUtc;` — время последнего фактического переключения.
2. Заменить единый порог на **гистерезис с двумя направлениями**:
   - включать обычный `StackPanel` только при `total > viewport + PanelSwitchMargin` (margin ≥ 32, т.е. > 2×`sbw`);
   - возвращаться на `VirtualizingStackPanel` только при `total < viewport - PanelSwitchMargin`.
   - `const double PanelSwitchMargin = 32;` — константа рядом с `MaxRowsForPlainStackPanel`.
3. **Не переключать панель, пока дерево не построено**: перед сменой стратегии проверять, что `StartupInitializationCompleted` уже был (флаг в VM доступен; при отсутствии — признак по полю `_viewModel`, добавить при необходимости поле-флаг в окне, выставляемое в обработчике `StartupInitializationCompleted`, см. [`MainWindow.xaml.cs:213`](Configuration%20Management/Views/MainWindow.xaml.cs:213)). До этого момента остаёмся на `VirtualizingStackPanel` (startup с `rows=0` и неполным деревом не должен терять виртуализацию).
4. Дебаунс фактического переключения: если переключение реально требуется, но прошло менее 500 мс после `_lastTreePanelSwitchUtc` — отложить через `Dispatcher.BeginInvoke` (Background) повторную проверку (`_treePanelSwitchQueued` защищает от множественных отложенных вызовов). Фактическое переключение обновляет `_lastTreePanelSwitchUtc`.
5. `LogColumnsDiagnostics` из `ApplyTreePanelStrategy` перенести в точку фактического переключения (после п.4-фильтра), а не на каждый входящий вызов.

Критерий: за старт/стабилизацию окна панель переключается не более 1–2 раз; осцилляция на границе исключена гистерезисом и дебаунсом.

#### A4. Диагностика CM_COLUMNS — однократная / по требованию (требование #309)

Файл: `Configuration Management/Views/MainWindow.Columns.cs`, метод `LogColumnsDiagnostics` (строки 966–1024).

Шаги:
1. По умолчанию **выключить** постоянную запись. Логика гейта:
   - `private static readonly bool _columnsTraceEnabled = string.Equals(Environment.GetEnvironmentVariable("CM_COLUMNS_TRACE"), "1", StringComparison.OrdinalIgnoreCase);` — по образцу существующей конвенции `CM_TOOLTIP_TRACE` ([`ToolTipCloser.cs:152`](Configuration%20Management/Views/ToolTipCloser.cs:152), [`ToolTipCloserAvalonia.cs:74`](Configuration%20Management/Views/ToolTipCloserAvalonia.cs:74)).
   - дополнительно лимит: даже при включённом флаге писать **не более одного дампа на сессию приложения** (`private bool _columnsTraceDumped;`): первый вызов пишет и взводит флаг, последующие — `return`.
2. Вызовы из горячих точек (строка 881 в блоке изменения `effective`, строка 930 в `ApplyTreePanelStrategy`) оставить, но они упираются в гейт из п.1 — даже если остаточный цикл останется, лог не вырастет.
3. Полезное поведение для проверки пользователем: **один дамп после завершения стартовой компоновки** — вызвать `LogColumnsDiagnostics` однократно из обработчика `StartupInitializationCompleted` ([`MainWindow.xaml.cs:213`](Configuration%20Management/Views/MainWindow.xaml.cs:213), DispatcherPriority.Loaded), тогда в журнале одной строкой будут фактические значения для сверки с #309, а дальнейшие записи не появятся.
4. Обновить XML-комментарий метода: «включается env-переменной `CM_COLUMNS_TRACE=1`; без неё — не более одной записи на сессию».

Критерий: чистый старт не создаёт ни одной записи CM_COLUMNS; при `CM_COLUMNS_TRACE=1` — ровно одна запись за сессию (и не более).

### 1.3 Что НЕ меняем в рамках #343/#309

- Логику восьмой/девятой попытки `EnsureHorizontalReach`, `UpdateTreeMinWidthContent`, `MeasureFirstRowDiagnostics`, ручной замер строк — оставляем (нужны для достижимости последних колонок, issue #309).
- Avalonia-часть не трогаем (цикл — только WPF; на Linux аналог уже без логов и с допуском).

---

## 2. Issue #309 «Пропал горизонтальный скрол» — что делаем после #343

После устранения цикла пользователь сможет проверить скрол на сборке 0.3.9.298. Дополнительно в рамках #309:

1. Пункты A1–A4 раздела 1.2 уже гарантируют: лог не раздувается, панель не флипает, полоса появляется по стабильной границе `total > viewport + margin`.
2. Оставить `EnsureHorizontalReach` (fallback-прокрутка к последней колонке после применения настроек) — он не мешает и защищает достижимость последних колонок.
3. После релиза в комментарии к issue #309 попросить пользователя проверить по сценарию: сузить окно так, чтобы колонки не помещались → появилась горизонтальная полоса → докрутить до последней колонки («№ релиза»/«Конфигурация») → заголовки синхронно прокручиваются. Также проверить отсутствие роста журнала.
4. Замечание из #309 (id 5966017738): пользователь подтвердил, что скрол «стал меньше» и сможет прокрутить — проверка разблокируется только после фикса зависания (#343). Это ожидаемо; никаких дополнительных изменений логики до проверки не планируем.

---

## 3. Issue #344 «Нормальные имена файлам сценариев»

### 3.1 Выбор варианта: `<Имя>.<Id>.script.json` (вариант 1 из issue)

Текущее имя файла — `SanitizeId(Id) + ".script.json"` ([`ScriptScenarioStore.FilePathFor`](Configuration%20Management/Services/ScriptScenarioStore.cs:134)). Предлагаемое имя — **`<СанитизированноеИмя>.<Id>.script.json`**, например `Calc.19fb08b20a2b434588e6d857479c50f0.script.json`.

Обоснование выбора варианта 1 (имя + id), а не варианта 2 (только имя):

- **Детерминированный поиск по Id.** Хранилище ключуется Id (`Get(id)`/`Delete(id)`/`Save(scenario)`), и все вызовы UI идут по Id (`ScriptScenariosWindow`, `ScriptPickWindow`, `MainViewModel.Scripts`, `ScriptScenarioEditWindow`). Id в имени файла позволяет находить файл без дополнительного индекса: `Directory.EnumerateFiles(dir, "*.<id>.script.json")`.
- **Коллизии имён невозможны**: два сценария с именем «Calc» дают разные файлы (суффикс-Id уникален). При варианте 2 второй `Save` перезаписал бы первый.
- **Переименование простое**: при смене `Name` меняется только префикс файла; старый файл удаляется по Id-паттерну (см. 3.3). При варианте 2 программа должна была бы помнить исходное имя файла навсегда (поле в JSON + миграция), а редактирование имени создавало бы путаницу «имя в файле ≠ имя сценария».
- **Кириллица уже поддержана**: `SanitizeId` и новый `SanitizeName` заменяют только недопустимые символы (`Path.GetInvalidFileNameChars()`); кириллица в именах файлов валидна на NTFS (UTF-16) и Linux (UTF-8). Имя файла с кириллицей пользователь и хотел видеть.
- **Обратная совместимость чтения**: старые файлы `<id>.script.json` продолжают читаться (см. 3.2), ленивая миграция на новое имя произойдёт при первом редактировании (Save удалит старый файл по Id).

Вариант 2 отклонён: он требует хранить исходное имя файла (или индекс файл→Id), страдает от дубликатов имён и переименований, усложняет `Get`/`Delete`, не давая пользователю преимуществ, которых нет у варианта 1 (в папке всё равно видно осмысленное имя).

**Резервные сценарии (BackupScenarioStore) не меняем**: у них отдельный каталог `backups/scenarios`, суффикс `.scenario.json`, жалоб нет, и менять их в этом пакете не требуется (описано в issue как «вероятно менять не нужно»). Хранилища полностью независимы — общего кода формирования имён нет. В XML-комментарии `BackupScenarioStore` можно оставить пометку «при необходимости — тот же приём имени+ид», но код не трогаем.

### 3.2 Правила локализации файла (совместимость двух форматов)

Вводятся два формата имени:

- **Старый (чтение только):** `<id>.script.json` (GUID или произвольный санитизированный id без точки).
- **Новый (чтение и запись):** `<name>.<id>.script.json`, где `<name>` — санитизированное имя (без точки между сегментами? НЕТ: точки в имени допустимы, см. 3.4 — важно правило разбора, п.3).

Правила разбора id из имени файла:
1. Если у файла есть ровно один суффикс-сегмент до `.script.json` и он содержит id без имени (`<id>.script.json`) — это старый формат: id = весь стем.
2. Если сегментов два и более (`<name...>.<id>.script.json`) — id = **последний** сегмент стема (перед `.script.json`), имя = всё, что до него (точки в имени допустимы, включая конечную точку в «Calc.» — поведение документировать в тестах).
3. `LoadAll()`: для каждого файла `*.script.json` десериализуем; затем:
   - если у сценария пустой/отсутствует `Id` — заполнить из id-сегмента имени файла;
   - если `Id` в JSON отличается от id-сегмента имени — **предпочесть id из имени файла** (имя файла — источник локализации; `Get(filenameId)` обязан находить файл; это же защищает от ручного копирования файла с переименованием);
   - дедупликация по итоговому Id (защита от битых копий/краш-состояний): при совпадении Id оставить первый, остальные пропустить.
4. Порядок `LoadAll` не меняется: сортировка по `Name` (`OrdinalIgnoreCase`), как сейчас ([`ScriptScenarioStore.cs:79`](Configuration%20Management/Services/ScriptScenarioStore.cs:79)).

### 3.3 Изменения в `ScriptScenarioStore`

Файл: `Configuration Management/Services/ScriptScenarioStore.cs` (интерфейс `IScriptScenarioStore` не меняется — сигнатуры `Save/Get/Delete/LoadAll` остаются прежними, все вызовы UI по Id остаются рабочими).

1. **Новый `FilePathFor(id, name)`**: `Path.Combine(ScenariosDirectory, SanitizeName(name) + "." + SanitizeId(id) + ".script.json")`. Когда `SanitizeName(name)` пуст (нет имени) — вернуть старый формат `SanitizeId(id) + ".script.json"` (полная совместимость).
2. **`Save(ScriptScenario)`** (строки 89–99):
   - сгенерировать Id, если пуст (как сейчас);
   - **удалить прежние файлы сценария по Id до записи нового** (покрывает: переименование `Name` — убирает старый `Name.<id>.script.json`; миграцию старого формата — убирает `<id>.script.json`): helper `DeleteFilesById(id)` удаляет точное `<id>.script.json` и все `*.<id>.script.json`;
   - записать новый файл `FilePathFor(scenario.Id, scenario.Name)`.
3. **`Get(string id)`** (строки 117–132):
   - сначала точное старое имя `FilePathFor(id)` (быстрая проверка старого формата);
   - затем `Directory.EnumerateFiles(dir, "*." + SanitizeId(id) + ".script.json")` → первый существующий файл;
   - десериализовать, заполнить `Id` из имени, если пуст (п.3.2.3), вернуть.
4. **`Delete(string id)`** (строки 102–114): заменить `File.Delete(FilePathFor(id))` на `DeleteFilesById(id)` (точное имя + паттерн) — убирает и осиротевшие файлы после краша между delete и write.
5. **`SanitizeName(string name)`** — новый статический метод:
   - пустое/whitespace → `""` (сигнал вернуться к старому формату без имени);
   - заменить `Path.GetInvalidFileNameChars()` на `'_'`;
   - **обрезать** до 120 символов (защита от превышения 255 байт пути вместе с id и суффиксом);
   - обрезать ведущие и хвостовые пробелы и точки; если начинается с `.` — добавить префикс `_` (скрытые файлы на Linux);
   - **зарезервированные имена устройств Windows** (`CON`, `PRN`, `AUX`, `NUL`, `COM1..9`, `LPT1..9`, а также формы с точкой, напр. `CON.foo`) — префикс `_` (запрет действует и на составные имена с расширением);
   - результат «всё заменилось» (например, имя `"***"`) → `""` (фолбэк на формат без имени).
   - Внимание: точки ВНУТРИ имени сохраняются (это валидно и желательно для «Calc.»-подобных имён), разбор делает п.3.2.2.
6. Обновить XML-документацию класса и методов: формат `Name.<id>.script.json`, обратная совместимость со старым `id.script.json`, ленивая миграция при редактировании.

### 3.4 Тесты (`ConfigurationManagement.Tests/ScriptScenarioStoreTests.cs`)

Правка существующих:

1. `Save_WritesReadableUtf8_NotEscapedUnicode` (строка 275): путь чтения файла изменить на `Name + "." + scenario.Id + ".script.json"` (имя «Тест 2» → `Тест 2.<id>.script.json`, кириллица в имени файла — валидна).

Новые тесты:

2. `Save_UsesNameAndIdInFileName` — после `Save` файл называется `Calc.<id>.script.json` (ровно один `*.script.json`).
3. `LoadAll_LoadsOldGuidNamedFiles_BackwardCompatible` — предсоздать `<guid>.script.json` (без имени-префикса) → `LoadAll` возвращает сценарий, `Id` заполнен из имени.
4. `Save_RenameScenario_MovesFileNoOrphans` — сохранить `Name="A"`, изменить на `"B"`, `Save` → существует только `B.<id>.script.json`, старых файлов по Id нет, `LoadAll` — один элемент.
5. `Save_DuplicateNames_BothFilesAndEntries` — два сценария с именем «Calc» и разными Id → два файла, `LoadAll` возвращает 2 записи (коллизия имён не перезаписывает).
6. `Save_CyrillicAndIllegalCharsInName_Sanitized` — имя `"Отчёт за 2026: тест"` → файл существует (недопустимый `:` заменён), `Get` по Id работает.
7. `Save_ReservedDeviceName_Prefixed` — имя `"CON"` → файл создаётся с префиксом, `Get`/`LoadAll` работают.
8. `Save_VeryLongName_Truncated` — имя из 300 символов → файл создаётся (длина пути в пределах лимита), `Get` работает.
9. `Get_ByNameAndIdFormat_FindsFile` — файл в новом формате находится по Id через паттерн; `Get(несуществующий)` — `null` (существующий тест остаётся).
10. `LoadAll_FileIdWinsOverJsonId` — файл `Calc.<idA>.script.json` с JSON-содержимым `Id=idB` → `LoadAll` возвращает сценарий с `Id=idA` (имя файла — источник локализации).
11. `Save_OldFormatFile_RemovedOnRewrite` — предсоздать `<id>.script.json` (старый формат) и сохранить сценарий с тем же Id и именем → остаётся только новый формат.

Существующие legacy-тесты (`Get_LegacyJsonWithoutWorkingDirectory_MigratesToEmpty`, `Get_LegacyJsonWithoutShell_MigratesToAuto`, `Get_LegacyJsonWithEscapedUnicode_ReadsSameStrings`, `Get_UnknownId_ReturnsNull`, `Save_InvalidFileNameCharsInId_SanitizesWithoutErrors`, `Delete_RemovesFileAndEntry`, `LoadAll_SameIdRepeatedSave_NoDuplicates`, `Save_SameId_OverwritesExistingFile`) должны проходить без изменений — убедиться прогоном.

---

## 4. Версия, CHANGELOG, README, комментарии в issues

### 4.0 Обоснование одной микро-версии 0.3.9.298

Судя по истории (`plans/PLAN-0.3.9.291-296.md`, `plans/PLAN-0.3.9.297.md`, `CHANGELOG.md`), в проекте принято **одна микро-версия на пакет правок** (0.3.9.297 закрыл сразу 6 issues; комментарии вида `comment-XXX-0.3.9.YYY` — по одной на issue за версию). Для этого пакета одна версия тем более уместна: фикс #343 разблокирует проверку #309, а оба они — WPF-часть одной системы колонок. Отдельные версии на каждый issue дробят релиз без выгоды. **Решение: 0.3.9.298.**

### 4.1 Поднятие версии

Файл: `Configuration Management/Configuration Management.csproj` (строки 62–65):
- `Version` = `0.3.9.298`
- `AssemblyVersion` = `0.3.9.298`
- `FileVersion` = `0.3.9.298`
- `InformationalVersion` = `0.3.9.298`

### 4.2 CHANGELOG.md

Добавить секцию `## [0.3.9.298] — 2026-10-03` над `0.3.9.297` (в начало списка), по образцу прошлых записей:

- **Виснет при старте (issue #343)**: разорван layout-цикл расчёта минимальной ширины списка на границе `viewport ≈ сумма колонок + ширина скроллбара`: ширина заголовка больше не зависит от живого `ExtentWidth` внутреннего ScrollViewer (источник обратной связи), запись `MinWidth` презентера выполняется только при фактическом изменении, переключение панели `VirtualizingStackPanel`/`StackPanel` получило гистерезис (запас > 2×ширины полосы) и дебаунс 500 мс, а до завершения стартовой инициализации дерева стратегия панели не меняется вовсе — приложение не зависает на загрузке избранного.
- **Имена файлов сценариев (issue #344)**: файлы в `scripts/scenarios` получают имя `<Имя>.<Id>.script.json` (например `Calc.<id>.script.json`); старые файлы `<id>.script.json` читаются без изменений и мигрируют при первом редактировании; санитизация имени (недопустимые символы, длина 120, зарезервированные имена устройств, скрытые файлы Linux), коллизии имён исключены суффиксом-Id; `IScriptScenarioStore` и все вызовы UI не меняются.
- **Горизонтальный скрол (issue #309)**: диагностика CM_COLUMNS переведена в режим «одна запись на сессию по умолчанию» (включение — env `CM_COLUMNS_TRACE=1`); лог больше не раздувается; проверка скрола пользователем разблокирована фиксом #343.

### 4.3 README.md

- Строка 3: бейдж версии `Версия-0.3.9.297` → `Версия-0.3.9.298`.
- При наличии упоминания «Сценарии запуска скриптов» в разделе возможностей — при желании дополнить одной фразой про читаемые имена файлов (`<Имя>.<Id>.script.json`); не обязательно.

### 4.4 Комментарии в issues (не закрывать!)

По образцу существующих `publish/comment-XXX-0.3.9.YYY.md`. Файлы черновиков (создать в publish/):
- `publish/comment-343-0.3.9.298.md` — «Исправлено в версии 0.3.9.298»: краткое описание корня (layout-цикл CM_COLUMNS на границе total/viewport±sbw, флип панели), что изменилось, как проверить (старт на реальных данных, лог не растёт). Просьба проверить и написать результат.
- `publish/comment-344-0.3.9.298.md` — «Исправлено в версии 0.3.9.298»: выбран вариант 1 (`Calc.<id>.script.json`), почему (детерминированный поиск по Id, отсутствие коллизий, переименования), обратная совместимость и ленивая миграция, как проверить (открыть папку `scripts/scenarios`, отредактировать сценарий — файл переименуется).
- `publish/comment-309-0.3.9.298.md` — «Исправлено в версии 0.3.9.298»: устранён циклический лог и зависание, которое блокировало проверку; диагностика теперь однократная; сценарий проверки скрола (сузить окно → полоса → докрутить до последней колонки, заголовки синхронны). Просьба проверить.

Issues остаются открытыми до подтверждения пользователя.

---

## 5. Сборка и релиз (выполняется после реализации и прогона тестов)

Артефакты и команды — по прецеденту `publish/release_body_0.3.9.297.md`:

1. **Windows/WPF single-file**: `powershell -NoProfile -ExecutionPolicy Bypass -File "Configuration Management\build-windows-single-file.ps1"` → `dist/win-x64/ConfigurationManagement.exe`.
2. **Linux/Avalonia single-file** (кросс-сборка): `powershell -NoProfile -ExecutionPolicy Bypass -File "Configuration Management\build-linux-single-file.ps1"` → `dist/linux-x64/ConfigurationManagement`.
3. **.deb-пакет**: `python publish/build_deb_win_0.3.9.270.py` (версия берётся из csproj автоматически) → `package/linux/deb/out/configuration-management_0.3.9.298_amd64.deb`.
4. **Проверки**: PE-magic/ELF-magic, `FileVersion/ProductVersion = 0.3.9.298`, smoke `--help` (код 0), контроль .deb скриптом по образцу `publish/check_deb_win_0.3.9.297.py` (параметризовать версией).
5. Скопировать артефакты в `publish/out-0.3.9.298/` + `SHA256SUMS.txt`.
6. `publish/release_body_0.3.9.298.md` по образцу `release_body_0.3.9.297.md`; создать релиз GitHub `v0.3.9.298` с телом и тремя артефактами.
7. Опубликовать комментарии 4.4 в issues #343/#344/#309 (GH API/gh CLI), issues не закрывать.

---

## 6. Порядок реализации (рекомендуемый)

1. **#343**: A1 (вынос `ComputeTreeMinWidthTotal` + отвязка `HeaderGrid.Width` от extent) → A2 (guard `presenter.MinWidth`) → A3 (гистерезис + дебаунс панели + блокировка до `StartupInitializationCompleted`) → A4 (гейт диагностики).
2. Прогон `ConfigurationManagement.Tests` (WPF-тесты и общие) — регрессий по `ScriptScenarioStoreTests` быть не должно (они не зависят от MainWindow).
3. **#344**: правки `ScriptScenarioStore.cs` (3.3) + правка/новые тесты (3.4).
4. Полный прогон `ConfigurationManagement.Tests`; сборка WPF и Avalonia; ручная проверка: старт на большом списке баз (нет зависания, лог не растёт), сужение окна (появляется горизонтальная полоса, докрутка до последней колонки), создание/правка/удаление сценариев (имена файлов в новом формате, старые файлы мигрируют).
5. Версия 4.1, CHANGELOG 4.2, README 4.3, черновики комментариев 4.4.
6. Сборка и релиз (раздел 5) — отдельной задачей, вне этой.

## 7. Сводка затрагиваемых файлов

Код (common):
- `Configuration Management/Views/MainWindow.Columns.cs` — A1 (`SyncHeaderWidthWithList`, новый `ComputeTreeMinWidthTotal`), A2 (`UpdateTreeMinWidth`, `_lastPresenterMinWidth`), A3 (`ApplyTreePanelStrategy`, `_treePanelSwitchQueued`, `_lastTreePanelSwitchUtc`, `PanelSwitchMargin`), A4 (`LogColumnsDiagnostics`, `_columnsTraceEnabled`, `_columnsTraceDumped`, env `CM_COLUMNS_TRACE`).
- `Configuration Management/Views/MainWindow.Scroll.cs` — без изменений логики (проверить, что вызовы `SyncHeaderWidthWithList`/`UpdateTreeMinWidth` не конфликтуют с A1; при необходимости только комментарий).
- `Configuration Management/Views/MainWindow.xaml.cs` — точка однократного дампа диагностики после `StartupInitializationCompleted` (A4) и установка флага готовности дерева для A3.
- `Configuration Management/Services/ScriptScenarioStore.cs` — формат имени, `SanitizeName`, `DeleteFilesById`, правила `Get`/`LoadAll`.
- `Configuration Management/Services/IScriptScenarioStore.cs` — без изменений (комментарий при желании).
- `Configuration Management/Services/BackupScenarioStore.cs` — без изменений (только при желании комментарий о применимости того же приёма).

Тесты:
- `ConfigurationManagement.Tests/ScriptScenarioStoreTests.cs` — правка одного теста + новые (3.4).

Документация/релиз:
- `Configuration Management/Configuration Management.csproj` — версия 0.3.9.298.
- `CHANGELOG.md` — секция 0.3.9.298.
- `README.md` — бейдж версии.
- `publish/comment-343-0.3.9.298.md`, `publish/comment-344-0.3.9.298.md`, `publish/comment-309-0.3.9.298.md` — черновики комментариев.
- `publish/release_body_0.3.9.298.md`, `publish/check_deb_win_0.3.9.298.py` (по образцу), `publish/out-0.3.9.298/` — на этапе сборки.

## 8. Критерии приёмки

- **#343**: при старте на реальном профиле (большое дерево, загрузка избранного) приложение не зависает; в журнале нет потока `CM_COLUMNS`; размер лога за минуту не растёт.
- **#344**: в `scripts/scenarios` файлы вида `Calc.<id>.script.json`; старые `<id>.script.json` читаются; после редактирования имени файл переименовывается без дубликатов; два сценария с одинаковым именем сосуществуют; кириллица в имени файла корректна; все тесты `ScriptScenarioStoreTests` зелёные.
- **#309**: на суженном окне появляется горизонтальная полоса, прокрутка дотягивает до последней колонки, заголовок синхронен; при `CM_COLUMNS_TRACE=1` — ровно одна запись CM_COLUMNS за сессию.
- Релиз: 3 артефакта (exe, linux-x64, deb) с контрольными суммами; комментарии «исправлено в версии 0.3.9.298» в #343/#344/#309; issues открыты.

## 9. Риски

- **Точные WPF-внутренности цикла могут отличаться от гипотезы** (мы не запускали приложение). План лечит все звенья связки независимо: отвязка ширины заголовка от extent (A1), защита записи MinWidth (A2), гистерезис+дебаунс+блокировка на старте (A3) и однократный лог (A4) — любой из них в одиночку уже останавливает разрастание журнала, а совокупность рвёт цикл при любом из вариантов причины. При реализации подтвердить чтением стека/трассы в отладчике (см. A4: `CM_COLUMNS_TRACE=1` для отладки).
- **Регрессия ширины заголовка** после A1: `target` теперь от расчётной суммы, а не от extent. Проверить вручную: длинные названия баз, скрытие/показ колонок, перенос порядка колонок — полоса и выравнивание заголовка не хуже, чем в 0.3.9.297 (на 297 пользователь видел скрол, но приложение висло; на 298 скрол должен сохраниться без висения).
- **#344**: Windows запрещает имена устройств с любым расширением (`CON.<id>.script.json` недопустим) — покрыто префиксом; длина пути на NTFS 255 символов — покрыто обрезкой до 120; конфликт при ручном копировании файлов пользователем (один Id в двух файлах) — покрыто дедупликацией в `LoadAll` (тест 10–11).
- **Двуплатформенность**: правки #343/#309 — только `#if WINDOWS`; #344 — common-код (работает на обеих платформах, имена с кириллицей валидны на Linux UTF-8). Avalonia не трогаем.