# План исправлений: релизы 0.3.9.277 – 0.3.9.290

Репозиторий: github.com/sivatorov/ConfigurationManagement
Ветка: main (последний выпуск 0.3.9.276)
Проект: Configuration Management — стартер 1С на C#/.NET, две платформы UI (Windows/WPF + Linux/Avalonia, общий код под `#if WINDOWS`/`#if LINUX`, зеркала `.xaml.cs` ↔ `.Avalonia.cs`).
Источник задач: открытые issues #341, #340, #339, #338, #335, #334, #333, #330, #324, #323, #321, #309, #308, #305 (последний комментарий НЕ от sivatorov либо комментариев нет; #332 и #329 исключены — последний комментарий от sivatorov).

## Версии и порядок выполнения

| Версия | Issue | Тема | Тип |
|---|---|---|---|
| 0.3.9.277 | #340 | Снятие выделения после мультивыделения | баг |
| 0.3.9.278 | #308 | Скрипты: фокус клавиатуры в окне выбора | баг (финал) |
| 0.3.9.279 | #330 | Скачивание версии платформы: падение TwoWay-привязки LogText | баг |
| 0.3.9.280 | #339 | Отбор запущенных: актуализация статусов при переключении | доработка |
| 0.3.9.281 | #324 | Серверы 1С: парсер вывода rac не распознаёт кластеры | баг |
| 0.3.9.282 | #305 | Создание серверной базы: 4 замечания | баги + доработки |
| 0.3.9.283 | #321 (ч.1) | Типовые конфигурации: поле «Имя конфигурации» + корректные данные поставляемых | доработка |
| 0.3.9.284 | #321 (ч.2) | Типовые конфигурации: потеря строк релизов при сохранении | баг |
| 0.3.9.285 | #338 | Выбор конфигурации: пополнение списка из «Имени конфигурации» типовых | доработка |
| 0.3.9.286 | #333 | Учетки ИТС: кнопка на панели, просмотр пароля, отображение имени | доработки |
| 0.3.9.287 | #323 + #334 | Портал 1С: HTTP 302 и авторизация при проверке обновлений платформы и конфигураций | баги (общий корень) |
| 0.3.9.288 | #335 | Диагностика подключения: диалог портов по сервисам | доработка (большая) |
| 0.3.9.289 | #341 | Свертка/развертка текущей ветки дерева | новая возможность |
| 0.3.9.290 | #309 | Горизонтальный скролл списка баз (9-я попытка, Windows/WPF) | баг (сложный) |

Логика порядка: сначала быстрые багфиксы UI (#340, #308, #330), затем средние багфиксы (#339, #324, #305), далее справочник типовых конфигураций (#321), от которого зависит #338, затем учётки ИТС #333 и портал 1С (#323+#334), крупные возможности (#335, #341) и, последним, самый сложный затяжной баг #309 (требует диагностики с пользователем).

```mermaid
flowchart LR
    A[Этап 1: быстрые багфиксы UI] --> B[Этап 2: средние багфиксы]
    B --> C[Этап 3: типовые конфигурации]
    C --> D[Этап 4: учетки ИТС и портал 1С]
    D --> E[Этап 5: крупные возможности]
    E --> F[Этап 6: сложный баг скролла]
```

Правила для каждого выпуска:
- Номер версии поднимается в [Configuration Management.csproj](Configuration%20Management/Configuration%20Management.csproj:62) — поля `Version`, `AssemblyVersion`, `FileVersion`, `InformationalVersion`.
- После каждой версии: `dotnet test` (проект `ConfigurationManagement.Tests`) и `dotnet build -p:BuildLinux=true`.
- Issues НЕ закрывать; после каждой версии оставлять комментарий в issue (шаблон `publish/comment-NNN-<версия>.md` по образцу существующих, публикация через gh CLI).
- Локализация ru/en для всех новых строк UI ([ru.json](Configuration%20Management/Localization/Languages/ru.json) / en.json).
- Двухплатформенность: любое изменение UI делается в двух зеркалах — WPF (`.xaml`/`.xaml.cs`) и Avalonia (`.Avalonia.cs`); общая логика — в `ViewModels`/`Services` без директив платформы.
- Итоговые шаги процесса: сборка exe Windows и пакетов Linux (AppImage/deb по `package/linux` и `publish/build_deb_win_*.py`), запись в CHANGELOG.md и README.md (бейдж версии на строке 3), push на GitHub, создание Release.

---

## 0.3.9.277 — #340 «Снятие выделения после мультивыделения»

Без комментариев; формулировка требований по описанию.

**Суть.** Мультивыделение (Ctrl или Shift) в любом разделе списка → правая кнопка → контекстное меню → клик на другую строку, чтобы снять выделение → «через мгновение исчезает контекстное меню и выделение тоже пропадает». Требуется: клик по строке должен надёжно снимать мультивыделение и закрывать меню, а само выделение не должно «самопроизвольно» исчезать раньше времени.

**Гипотеза.** Классический WPF-сценарий: обработчик открытия контекстного меню (`MouseRightButtonDown/Up`) или событие `SelectionChanged` сбрасывает/переназначает `SelectedItem` при клике; либо `ContextMenu.StaysOpen` + фокус. На Avalonia аналогичный путь в `LeveledTreeView.Avalonia.cs`.

### Правки
- Windows/WPF: изучить обработчики мыши и открытия контекстного меню в [MainWindow.Events.cs](Configuration%20Management/Views/MainWindow.Events.cs) (клики по строкам дерева), [MainWindow.ContextMenu.cs](Configuration%20Management/Views/MainWindow.ContextMenu.cs) (если есть) и [LeveledTreeView.cs](Configuration%20Management/Controls/LeveledTreeView.cs). Проверить `BatchSelectionHelper` (выделение пачкой) и переприсваивание `SelectedInfobase`/`SelectedItems` в момент закрытия меню.
- Linux/Avalonia: аналогично [LeveledTreeView.Avalonia.cs](Configuration%20Management/Controls/LeveledTreeView.Avalonia.cs) — обработчики `RightTapped`/`PointerPressed`, сброс выделения.
- Корректировка: при клике левой кнопкой по строке во время открытого контекстного меню — сначала закрывать меню, затем применять обычную логику выбора; не давать меню «съедать» событие клика (Handle/RoutedEvent).

### Тесты
UI-поведение юнит-тестами не покрывается. Ручная проверка сценария из issue на обеих платформах; регрессия `dotnet test`.

---

## 0.3.9.278 — #308 «Пожелание - Скрипты» (финальное замечание)

**Последний комментарий 7OH (2026-10-02T13:49):** «Первая строка в списке стала текущей, но пока мышкой в список не ткнёшь — на кнопки клавиатуры не реагирует».

**Суть.** В 0.3.9.271 добавлены выбор первого пункта и Enter=«Выполнить», но реальный фокус ввода на списке не установлен: выделение есть (`SelectedIndex=0`), а `IsKeyboardFocusWithin` — false, поэтому стрелки/Enter не работают до клика мышью.

### Правки
- Windows/WPF [Views/ScriptPickWindow.xaml.cs](Configuration%20Management/Views/ScriptPickWindow.xaml.cs): в `Loaded` после `ScenariosList.SelectedIndex = 0` вызвать `ScenariosList.Focus()` и `Keyboard.Focus(ScenariosList)`; продублировать в `Dispatcher.BeginInvoke` (после первой отрисовки), проверить `IsKeyboardFocusWithin`; при необходимости вызвать `FocusManager.SetFocusedElement(this, ScenariosList)`.
- Linux/Avalonia [Views/ScriptPickWindow.Avalonia.cs](Configuration%20Management/Views/ScriptPickWindow.Avalonia.cs): в `Opened` после выбора первого пункта вызвать `_list.Focus()` (+ `NavigationMethod.Keyboard`/`KeyboardNavigation`), при необходимости через `Dispatcher.UIThread.Post`.

### Тесты
UI-поведение юнит-тестами не покрывается. Регрессия `dotnet test` + ручная проверка: окно открылось → стрелки двигают выбор, Enter запускает без клика мышью.

---

## 0.3.9.279 — #330 «Скачивание версии платформы» (падение при открытии)

**Последний комментарий 7OH (2026-10-02T11:39):** `InvalidOperationException: Привязка типа TwoWay или OneWayToSource не может работать с доступным только для чтения свойством "LogText"` типа `PlatformDownloadViewModel`.

**Причина найдена в коде:** [PlatformDownloadWindow.xaml](Configuration%20Management/Views/PlatformDownloadWindow.xaml:260) — `TextBox x:Name="LogBox" IsReadOnly="True" Text="{Binding LogText}"` без `Mode=OneWay`. WPF по умолчанию строит TwoWay-привязку к read-only свойству `PlatformDownloadViewModel.LogText` и валидирует её при переоткрытии окна → исключение. На Avalonia привязка уже явно `Mode=OneWay` ([PlatformDownloadWindow.Avalonia.cs](Configuration%20Management/Views/PlatformDownloadWindow.Avalonia.cs:358)).

### Правки
- [PlatformDownloadWindow.xaml](Configuration%20Management/Views/PlatformDownloadWindow.xaml:260): `Text="{Binding LogText, Mode=OneWay}"`.
- Проверить аналогичные окна журналов на ту же ошибку:
  - [PlatformUpdateWindow.xaml](Configuration%20Management/Views/PlatformUpdateWindow.xaml) — поле журнала `LogText` (подозрение то же);
  - `UpdateCheckWindow.xaml`, `NetworkDiagnosticsWindow.xaml`, `ActualReleasesWindow.xaml` — все read-only текстовые поля, привязанные к read-only свойствам (пробежать grep `Text="{Binding .*Log` / `IsReadOnly="True"` и добавить `Mode=OneWay`).
- При желании — страховка в VM не нужна (свойство read-only по контракту), достаточно XAML.

### Тесты
Регрессия `dotnet test` (существующий `PlatformDownloadViewModelTests` подтверждает, что `AppendLog` из фонового потока не бросает исключений). Ручная проверка: открыть окно «Скачивание версии платформы 1С» и «Обновление платформы 1С» несколько раз подряд.

---

## 0.3.9.280 — #339 «Отбор запущенных» (актуализация статусов)

**Последний комментарий 7OH (2026-10-02T13:39):** «Можно организовать обновление статусов при переключении на эту вкладку? … Одна запустилась сразу и видна, а вторая долго запускалась и не была с индикатором сразу — после прогрузки в списке не появляется. Аналогично, если база вылетела/закрыли — она не исчезает из списка, хотя индикатор у неё исчезает».

**Суть.** В 0.3.9.275 `RefreshRunningFlags()` вызывается в момент включения режима Running, но этого недостаточно: база может запускаться дольше таймера опроса, а ушедший в трей/закрытый процесс не отражается, потому что фильтр построен по кэшу флагов. Требуется: при переключении на вкладку «Запущенные» выполнять внеплановый опрос процессов И ПОСЛЕ него пересобирать фильтрованный список (чтобы новые флаги сразу влияли на состав списка); при повторном появлении флагов (таймер) — так же пересобирать.

### Правки
- Общий код: [MainViewModel.Running.cs](Configuration%20Management/ViewModels/MainViewModel.Running.cs) — `RefreshRunningFlags` сделать возвращающим/сигнализирующим факт изменения набора `IsRunning` (например, сравнить до/после); в [MainViewModel.Display.cs](Configuration%20Management/ViewModels/MainViewModel.Display.cs:155) при `ListViewMode.Running` вызывать `await RefreshRunningFlagsAsync()` и затем `RefreshFilteredList()`/пересборку видимых баз.
- Avalonia: [MainViewModel.Avalonia.Display.cs](Configuration%20Management/ViewModels/MainViewModel.Avalonia.Display.cs:298) — аналогично.
- Возможно, требуется повторный внеплановый опрос через короткий интервал (например, 1–2 сек) после включения режима, пока процесс «дозапускается» — обсудить с пользователем; минимально — один внеплановый опрос + пересборка по событию таймера.

### Тесты
`Etap13ListStateTests` — кейс «переключение в режим Running вызывает пересборку списка после изменения флагов». Mock монитора процессов (существующий паттерн в тестах Running).

---

## 0.3.9.281 — #324 «Серверы 1С» (парсер rac)

**Последний комментарий 7OH (2026-10-02T11:04):** «Не распознаёт оно вывод почему-то» (скриншот монитора серверов, в терминале `rac.exe localhost:27545 cluster list` работает и показывает кластер). До этого (0.3.9.252/270) были исправлены токен `host:port` и fallback-разбор.

**Гипотеза.** Вывод внутри приложения приходит в другой кодировке (OEM 866) либо с другим форматированием, чем в консоли пользователя: табуляции отсутствуют, а fallback «2+ пробела» режет значения. Либо rac-команда в приложении выполняется от другой установленной версии rac (не та, что в PATH консоли).

### Правки
- Диагностика: запросить у пользователя полный фрагмент журнала приложения с командой rac и, при возможности, текстовый вывод; добавить в монитор временный лог первых ~40 строк stdout команды (флаг диагностики), чтобы сравнить фактический формат.
- [RacClient.cs](Configuration%20Management/Services/RacClient.cs:248) — проверить `StandardOutputEncoding` (сейчас UTF-8) против кодировки реального rac (866/1251); проверить, что выполняется именно выбранный в настройках rac/платформа (а не системный).
- [RacOutputParser.cs](Configuration%20Management/Services/RacOutputParser.cs:36) — по результатам диагностики усилить `ParseTable`: устойчивое разбиение по заголовку колонок (позиционные индексы из первой строки), поддержка выравнивания пробелами без потери внутренних пробелов, обработка `\r`.
- По результатам — правка парсера и/или кодировки, расширение [RacOutputParserTests](ConfigurationManagement.Tests/RacOutputParserTests.cs)/`RacClientTests` реальным образцом вывода пользователя.

### Тесты
Да: новые кейсы парсера на реальном выводе (табы и пробелы), `RacClientTests` — кодировка/аргументы. Регрессия `dotnet test`.

---

## 0.3.9.282 — #305 «Создание серверной базы» (4 замечания)

**Последний комментарий 7OH (2026-10-02T10:57):**
1. Выбор сервера 1С из списка всё ещё ничего не подставляет в поле (вручную писать можно, базу создало, но выбор чинить надо).
2. Введённый `localhost` в «Сервер СУБД» при повторном открытии не сохранился (запоминание не работает).
3. Создало базу с разрядностью х86 (выбирал просто 8.3.27; в настройках по умолчанию х64) — надо исправить.
4. Не пояснён текст предупреждения/вывода окна — добавить пояснение.

### Правки
1. **Выбор сервера 1С.** [CreateInfobaseWindow.xaml.cs](Configuration%20Management/Views/CreateInfobaseWindow.xaml.cs:622) `OnServerBox_SelectionChanged` — проверить, почему текст не попадает в поле: вероятно, срабатывает только при `SelectedItem is string`, а при `IsTextSearchEnabled=False` выбор мышью может давать иной путь; либо событие не приходит на Avalonia. Добавить синхронизацию `Text` из `SelectionChanged`/`DropDownClosed` на обеих платформах ([CreateInfobaseWindow.Avalonia.cs](Configuration%20Management/Views/CreateInfobaseWindow.Avalonia.cs)); проверить, что отложенный сброс `SelectedItem` не стирает текст.
2. **Запоминание сервера СУБД.** Поля уже существуют: [AppSettings.cs](Configuration%20Management/Models/AppSettings.cs:133) `LastCreateDbServer`/`LastCreateDbPort`, восстановление — [CreateInfobaseWindow.xaml.cs](Configuration%20Management/Views/CreateInfobaseWindow.xaml.cs:606). Найти места СОХРАНЕНИЯ (в `MainViewModel.Commands.cs`/`MainViewModel.Avalonia.cs` в обработчике успешного `CreateInfobase`): скорее всего, сохранение не выполняется на одной из платформ или только при определённом пути создания. Добавить/выровнять сохранение в общей точке (например, в `CreateInfobaseService` или перед вызовом CREATEINFOBASE) на обеих платформах.
3. **Разрядность.** Найти логику выбора версии/разрядности платформы в окне создания (поля выбора версии и разрядности, вероятно в `CreateInfobaseWindow` + `PlatformVersionPicker`/`PlatformDistributionPicker`): при выборе «8.3.27» без явной разрядности подставляется первая найденная установленная (х86), хотя в настройках дефолт х64. Исправить приоритет: явно выбранная разрядность из настроек > автоопределение; проверить строку `PlatformArchitecture`/аналоги.
4. **Пояснение текста окна.** Определить, какой текст 7OH считает непонятным (скриншот-предупреждение при создании); либо убрать, либо добавить понятную подпись/ToolTip (локализация ru/en).

### Тесты
Да: `CreateInfobaseDbServerStringTests` — сохранение/восстановление значений (чистая функция), разбор `server:port`. Выбор разрядности — чистый helper с приоритетами (если выносится) + тесты.

---

## 0.3.9.283 — #321 ч.1 «Типовые конфигурации» (данные и «Имя конфигурации»)

**Последний комментарий 7OH (2026-10-02T12:38):**
1. Поле «Сегмент адреса» в списке и формах редактирования переименовать в «Имя конфигурации» — там будет имя как в конфигурации, например «БухгалтерияПредприятия».
2. Подставить корректные значения для поставляемых данных (приведён список: БП → БухгалтерияПредприятия/Accounting/2.0→Accounting20_82, 3.0→Accounting30; ЗУП → ЗарплатаИУправлениеПерсоналом/HRM30; УТ → УправлениеТорговлей/Trade/10.3→Trade103, 11.х→Trade110; КА → КомплекснаяАвтоматизация/ARAutomation/1.0,1.1,2.0→ARA*; Розница → Retail/2.3,3.0; ERP → УправлениеПредприятием/EnterpriseERP20; БГУ → БухгалтерияГосударственногоУчреждения/StateAccounting20). «Если просто 0 — оставляем поле пустым».
3. «Имя конфигурации» в адрес не попадает — оно служит для сопоставления данных о конфигурации, её версии и одной из строк в таблице.

**Суть.** В модель [OneCConfigType.cs](Configuration%20Management/Models/OneCConfigType.cs) добавить отдельное поле «Имя конфигурации» (`ConfigName`, напр. `БухгалтерияПредприятия`), НЕ участвующее в построении URL (в отличие от `UrlCode`/`Nick`). Заполнить корректные значения во встроенном наборе [BuiltInConfigTypes.cs](Configuration%20Management/Services/BuiltInConfigTypes.cs).

### Правки
- [Models/OneCConfigType.cs](Configuration%20Management/Models/OneCConfigType.cs): новое свойство `public string ConfigName { get; set; } = "";` (или переиспользовать `UrlCode` как «имя конфигурации», а для URL ввести отдельный сегмент — решение принять на этапе кода, чтобы не ломать построение адресов `EffectiveUrlCode`/`Nick`).
- [Services/BuiltInConfigTypes.cs](Configuration%20Management/Services/BuiltInConfigTypes.cs): заполнить `ConfigName` и таблицы релизов по списку 7OH (редакции/подредакции/ники файлов).
- UI: [ConfigTypeEditWindow.xaml](Configuration%20Management/Views/ConfigTypeEditWindow.xaml)/`.Avalonia.cs` и [ConfigTypesEditWindow.xaml](Configuration%20Management/Views/ConfigTypesEditWindow.xaml)/`.Avalonia.cs` — колонка «Сегмент адреса (ник)» → «Имя конфигурации» (двойная подпись: имя конфигурации + ник), подсказка ToolTip «используется для сопоставления, в адрес не попадает».
- [ConfigTypesFilter.cs](Configuration%20Management/ViewModels/ConfigTypesFilter.cs) — добавить `ConfigName` в поиск.
- Совместимость JSON хранилища [CustomConfigTypesStore.cs](Configuration%20Management/Services/CustomConfigTypesStore.cs) — старые файлы без нового поля мигрируют без ошибок (дефолт пустой).

### Тесты
`CustomConfigTypesStoreTests`/`BuiltInConfigTypes` — корректность новых данных (редакции, ники), миграция старого JSON. Регрессия `dotnet test`.

---

## 0.3.9.284 — #321 ч.2 «Типовые конфигурации» (потеря строк релизов)

**Последний комментарий 7OH (2026-10-02T12:41):** «Правка конфигураций всё ещё глючит: добавляешь строку в таблицу релизов — вроде видно в списке — после ОК — в списке одну строку показывает релизов, после открытия на правку ещё раз — тоже одна строка в табличной части. Где-то ошибка при сохранении».

**Гипотеза.** В [ConfigTypeEditWindow.xaml.cs](Configuration%20Management/Views/ConfigTypeEditWindow.xaml.cs) при сохранении редакций список копируется неправильно: либо привязка табличной части собирается в новый `List` на каждый ввод (теряются ранее введённые строки), либо `ReleaseList` сохраняет только первую строку (копирование коллекции при сохранении), либо дубль между `Releases`/`ReleaseList`.

### Правки
- Изучить модель релизов в `OneCConfigType` (поля редакций/релизов, их сериализацию в JSON) и цикл чтения/записи в редакторе окна (обе платформы).
- Исправить сохранение: табличная часть должна вестись как `ObservableCollection` и целиком переноситься в модель при ОК (а не частично), проверять что поле редакций при повторном открытии читается из сохранённого JSON.
- Добавить регрессионный тест: дважды добавить строки релизов → сохранить → `Load()` → количество строк сохранено.

### Тесты
Да: `CustomConfigTypesStoreTests` — сохранение/чтение релизов (несколько строк), редакций с подредакциями. Регрессия `dotnet test`.

---

## 0.3.9.285 — #338 «Выбор конфигурации из списка» (дополнение)

**Последний комментарий 7OH (2026-10-02T13:51):** «Всё работает, но… Надо будет добавлять в список выбора и значения из колонки "Имя конфигурации" из списка типовых, когда они там появятся».

**Суть.** После #321 (поле «Имя конфигурации») дополнить `AvailableConfigurations` (список автодополнения поля «Конфигурация» в [ConnectionSettingsViewModel.cs](Configuration%20Management/ViewModels/ConnectionSettingsViewModel.cs:343)) значениями `ConfigName` из списка типовых конфигураций (`CustomConfigTypesStore.LoadAll()`) — помимо значений из баз списка.

### Правки
- [ViewModels/MainViewModel.Commands.cs](Configuration%20Management/ViewModels/MainViewModel.Commands.cs:146) `GetAvailableConfigurations()`: объединить значения `Infobase.ConfigurationName` с `ConfigName` типовых конфигураций (фильтр пустых, `Trim()`, дедупликация без учёта регистра, сортировка).
- Зеркало [ViewModels/MainViewModel.Avalonia.cs](Configuration%20Management/ViewModels/MainViewModel.Avalonia.cs:1258).
- `ConnectionSettingsViewModel.SetAvailableConfigurations` — без изменений (уже умеет дедупликацию).

### Тесты
`ConnectionSettingsViewModelTests` — `SetAvailableConfigurations` с пересечением значений из баз и типовых. Регрессия `dotnet test`.

---

## 0.3.9.286 — #333 «Учетки доступа к ИТС» (финальные замечания)

**Последний комментарий 7OH (2026-10-02T13:47):**
1. «Задать основным» лучше видеть на панели кнопок (рядом с Добавить/Закрыть), а не в каждой строке; в строке пусть остаётся звёздочка-индикатор.
2. Добавить к полю пароля кнопку просмотра при правке.
3. В настройках по-прежнему ключ вместо значения; в правке типовой конфигурации — аналогично.

### Правки
1. **Кнопка «Задать основным» на панель.** [ItsAccountsWindow.xaml](Configuration%20Management/Views/ItsAccountsWindow.xaml)/`.xaml.cs` и `.Avalonia.cs`: убрать кнопку из колонки строк (оставить иконку-звезду/индикатор «Основная»), добавить кнопку «Задать основным» на нижнюю панель (рядом с Добавить/Изменить/Удалить/Закрыть), доступна при выборе неосновной записи (CanExecute на `SetPrimaryCommand`). Это откат части 0.3.9.272.
2. **Просмотр пароля.** В редакторе учётной записи (окно правки записи ИТС, WPF + Avalonia) добавить переключатель видимости пароля (глаз): [PasswordBox.Avalonia.cs](Configuration%20Management/Controls/PasswordBox.Avalonia.cs) — паттерн уже есть; для WPF — стандартный `PasswordBox` + ToggleButton.
3. **Ключ вместо значения.** Найти, как строится список выбора учётной записи: [ItsAccountsViewModel.cs](Configuration%20Management/ViewModels/ItsAccountsViewModel.cs:117) `ItsAccountSelectionBuilder.Build`. Если возвращаются объекты без настройки отображения — задать `ItemTemplate`/`DisplayMemberPath=Name` во всех ComboBox: WPF [SettingsWindow.xaml](Configuration%20Management/Views/SettingsWindow.xaml:1471), [ConfigTypeEditWindow.xaml](Configuration%20Management/Views/ConfigTypeEditWindow.xaml:111); Avalonia [SettingsWindow.Avalonia.cs](Configuration%20Management/Views/SettingsWindow.Avalonia.cs:359), [ConfigTypeEditWindow.Avalonia.cs](Configuration%20Management/Views/ConfigTypeEditWindow.Avalonia.cs:37). Либо строка «ключ» — это `Id`/`Code` записи; тогда исправить построение пунктов (значение=id, отображение=имя).

### Тесты
`ItsAccountSelectionBuilderTests` (существующий) — расширить кейсы отображения. Регрессия `dotnet test`.

---

## 0.3.9.287 — #323 «Окно Проверка обновлений» + #334 «Автообновление платформы» (портал 1С: HTTP 302)

**Общий корень.** Оба issue: `releases.1c.ru/project/<ник>` отвечает HTTP 302, каталог не получается.
- #323 (последний коммент 7OH 2026-10-02T11:38): `HTTP 302 для 'https://releases.1c.ru/project/AccountingCorp30' (requestUri=<тот же>)` — редирект не обработан.
- #334 (последний коммент 7OH 2026-10-02T11:39): `Для входа на portal.1c.ru не задан логин` + пустой ответ `Platform83`; связано с #333 (учётка отображается ключом — см. 0.3.9.286).

**Текущая логика** [OneCUpdatesService.cs](Configuration%20Management/Services/OneCUpdatesService.cs): `SendWithAuthAsync` (стр. 847) вручную следует редиректам (MaxRedirects=10), при редиректе на `login.1c.ru` выполняет `TryLoginPortalAsync` (гибридный вход); финальный 302 без Location/циклический возвращается как есть → `CheckForUpdatesAsync` (стр. 226–238) логирует `HTTP 302` и возвращает `Failed`.

**План действий:**
1. Диагностика реального поведения `releases.1c.ru`: получить от пользователя журнал с добавленной детализацией — в `SendWithAuthAsync` при 3xx логировать `Location` и номер шага редиректа; определить, куда ведёт 302 (на login.1c.ru без Location, на тот же URL, на `portal.1c.ru`).
2. Обработка результата: если финальный статус 3xx — интерпретировать как «требуется авторизация» (учесть cookie/сессию), повторно запустить вход (снять флаг `_portalLoginAttempted` для повторной попытки в новом сеансе/после настройки учёток) и повторить запрос; добавить понятное сообщение пользователю.
3. Для #334: если учётные данные ИТС не настроены (справочник пуст, `UpdatesLogin` пуст) — в окне «Обновление платформы 1С» и в проверке обновлений (F9) показывать явную подсказку «для доступа к releases.1c.ru настройте учётные данные ИТС (Утилиты → Информация → Учётные данные ИТС)» вместо голого `NetworkError`.
4. Проверить, что выбор учётной записи из настроек (после фикса #333 п.3) реально доходит до `GetCredentials()` [OneCUpdatesService.cs](Configuration%20Management/Services/OneCUpdatesService.cs:941).
5. Общие точки: [OneCUpdatesService.cs](Configuration%20Management/Services/OneCUpdatesService.cs) (`SendWithAuthAsync`, `CheckForUpdatesAsync`, `TryLoginPortalAsync`), [PlatformUpdateViewModel.cs](Configuration%20Management/ViewModels/PlatformUpdateViewModel.cs), [UpdateCheckWindow.xaml](Configuration%20Management/Views/UpdateCheckWindow.xaml)/`.xaml.cs`/`.Avalonia.cs` (сообщения), локализация.

### Тесты
`OneCUpdatesUrlTests`/`UpdateCheckCatalogTests`/`PlatformUpdateServiceTests` — моки HTTP: 302 без Location → статус AuthRequired с понятной ошибкой; 302 с Location на login.1c.ru + успешный вход → повторный запрос с cookie (расширить существующие мок-харнесы). Регрессия `dotnet test`.

---

## 0.3.9.288 — #335 «Диагностика подключения» (диалог портов по сервисам)

**Последний комментарий 7OH (2026-10-02T11:24):** итоговое требование:
1. Одно поле «Сервер» (со списком доступных, как при правке базы) — убрать отдельное поле «СерверЫ» (оставить редактируемый список прямо в поле адреса/сервера).
2. «Серверы» как таблица вариантов серверов/сервисов, для которых можно задавать свои порты.
3. При нажатии «Проверить порты 1С» — отдельный диалог с таблицей из двух колонок: «порт» и «описание сервиса»; править можно только порт.
4. При открытии диалога догадаться о портах: если в главном окне указан порт, отличающийся «началом» от стандартного (2541 вместо 1541) — вычислить остальные по образцу: 2540, 2542, 2545 (сдвиг префикса: стандартные 1540/1541/1542/1545 → с заменой первой цифры/начального числа).
5. После подтверждения — сканировать порты 1С.

### Правки
- [ViewModels/NetworkDiagnosticsViewModel.cs](Configuration%20Management/ViewModels/NetworkDiagnosticsViewModel.cs): убрать отдельное свойство «Серверы» как выбор; оставить `Port`/`PortText` (стр. 134) и `SelectedServer`→ обычное поле «Сервер» (как в правке базы: редактируемый ComboBox). `CheckPortsCommand` (стр. 83) → открывает новый диалог портов.
- Новый чистый helper (например `Services/ServerPortsGuesser.cs`): `GuessPorts(basePort, стандартная карта 1540/1541/1542/1545)` — вычисляет смещение и подставляет остальные; покрыт юнит-тестами (включая пример 2541 → 2540/2541/2542/2545 и отсутствие смещения).
- Новый диалог портов: [Views/PortsEditWindow.xaml](Configuration%20Management/Views/PortsEditWindow.xaml)+`.xaml.cs` и `.Avalonia.cs` — таблица «порт | описание», редактируется только порт, кнопки OK/Отмена; модальный со скинингом. Порт каждого сервиса сохраняется через [ServerPortsStore.cs](Configuration%20Management/Services/ServerPortsStore.cs) в разрезе сервера.
- После подтверждения диалога — запуск сканирования портов 1С с этими портами (существующая цепочка `CheckPortsAsync`).
- Разметка окна [NetworkDiagnosticsWindow.xaml](Configuration%20Management/Views/NetworkDiagnosticsWindow.xaml)/`.Avalonia.cs`: перекомпоновка (одно поле сервера, кнопка «Проверить порты 1С» открывает диалог).

### Тесты
`ServerPortsGuesser` — позитив/негатив, примеры из issue. `ServerPortsStoreTests` (существующий) — новые порты по сервисам. Регрессия `dotnet test`.

---

## 0.3.9.289 — #341 «Свертка групп» (новая возможность)

Без комментариев; по описанию:
- Рекурсивная свертка/развертка ТОЛЬКО текущей ветки дерева групп.
- Мышью: Ctrl-Click по группе — свернуть/развернуть ветку, не меняя текущую строку (если курсор на другой строке).
- Клавиатура: Ctrl+Plus / Ctrl+Minus — свернуть/развернуть ветку под курсором.

**Текущее состояние:** есть только полные «Развернуть все группы» / «Свернуть все группы» ([MainViewModel.Theme.cs](Configuration%20Management/ViewModels/MainViewModel.Theme.cs:434) `CollapseAllGroups`/`ExpandAllGroups` → `SetExpandedDeep`), команды `ExpandAllGroupsCommand`/`CollapseAllGroupsCommand` ([MainViewModel.cs](Configuration%20Management/ViewModels/MainViewModel.cs:633)), сворачивание узла — `ToggleGroup` (Theme.cs:424).

### Правки
- Общий код: в `GroupNodeViewModel`/`MainViewModel` добавить `ExpandBranch(GroupNodeViewModel node)` / `CollapseBranch(node)` — рекурсивно раскрыть/свернуть только детей текущей группы (использовать существующий `SetExpandedDeep(node.Children, expanded)`), при этом ветки-соседи не трогать.
- Мышь: в [LeveledTreeView.cs](Configuration%20Management/Controls/LeveledTreeView.cs) и `.Avalonia.cs` — обработка Ctrl+Click по узлу группы: если Ctrl зажат и клик по группе → toggle ветки вместо навигации; текущая строка (SelectedItem) не меняется.
- Клавиатура: горячие клавиши Ctrl+Plus/Ctrl+Minus — регистрация в [MainWindow.Hotkeys.cs](Configuration%20Management/Views/MainWindow.Hotkeys.cs) и [MainWindow.Avalonia.Hotkeys.cs](Configuration%20Management/Views/MainWindow.Avalonia.Hotkeys.cs): свернуть/развернуть ветку под курсором (узлы, в которых есть SelectedInfobase/SelectedGroup); учесть конфликты (Ctrl+Plus/NumPad+).
- Команды палитры (опционально): «Развернуть ветку»/«Свернуть ветку».
- Локализация ru/en.

### Тесты
Чистая логика `SetExpandedDeep` ветки — тест на модели (что соседние ветки не меняются). Клавиатурная обработка UI — ручная. Регрессия `dotnet test`.

---

## 0.3.9.290 — #309 «Пропал горизонтальный скрол» (9-я попытка, Windows/WPF)

**Последний комментарий 7OH (2026-10-02T13:54):**
```
CM_COLUMNS: total=1133,6, sumActualHeader=1133,6, content=1133,6, presenterMin=1133,6,
viewport=1134,4, extent=1137,6, scrollable=3,2, panel=VirtualizingStackPanel, dpiScale=1,25
«Пока можно только избавиться от скрола, если сделать окно сильно больше, чем колонки.
Движение колонок ничего не меняет — скрол одинакового размера»
```

**Ключевой вывод из лога:** `scrollable=3,2` — сумма колонок МЕНЬШЕ viewport почти на всю ширину (полоса не нужна!), но пользователь видит последние 2 колонки «за краем». Значит проблема НЕ в длине полосы: колонки «Конфигурация»/«№ релиза» физически обрезаются правым краем области, а `ExtentWidth` считается по сумме колонок, которая не совпадает с фактической раскладкой (возможно: вертикальный скроллбар + разметка строки, где блок имени занимает `Grid.ColumnSpan`, либо ширина «Название» со звездой считается иначе). Также `panel=VirtualizingStackPanel` — переключение панели (0.3.9.276) не сработало, т.к. условие `total > viewport` не выполнилось.

### Шаги
1. **Диагностика A (обязательно):** запросить у пользователя лог с дополненным `CM_COLUMNS` — добавить в лог фактические `ActualWidth` каждой колонки (заголовка и строк), координату правого края последней колонки, ширину окна, ширину вертикального скроллбара. Определить, какая колонка реально обрезается и насколько.
2. **Эксперимент A:** сравнить сумму фактических ширин колонок строк (`ActualWidth`) с `sumActualHeader`; если строки шире — проблема в разметке строки (ColumnSpan/растяжение «Название»), правка `MainWindow.xaml` (разметка строки), а не скролла.
3. **Эксперимент B:** проверить влияние вертикального скроллбара: если он накладывается на контент (Overlay или отсутствует запас), последние колонки визуально обрезаются при `scrollable≈0` — дать запас (Padding/Margin справа) или учесть скроллбар в расчёте минимума.
4. **Эксперимент C (структурный):** вынести горизонтальную прокрутку во внешний `ScrollViewer` вокруг связки «HeaderGrid + MainTree», внутреннему скроллу дерева отключить горизонталь (как в прошлых планах для Avalonia). Звёздную «Название» при `total > viewport` — в абсолютную ширину.
5. **Fallback-минимум:** если полоса всё ещё «короткая», но колонки не видны — принудительная прокрутка к концу после применения настроек колонок.

### Что не трогать
Avalonia-часть (проблема не воспроизводится); `ListMinWidthCalculator` (покрыт тестами) — менять только при выносе новой чистой функции.

### Тесты
Только чистые helper-функции, если появятся; UI-раскладка юнит-тестами не покрывается. Ожидается итерация с пользователем (лог CM_COLUMNS).

---

## Единые требования к качеству

- После каждого выпуска: `dotnet test` (Windows), затем `dotnet build -p:BuildLinux=true`.
- Комментарии в issues: `publish/comment-<номер>-<версия>.md` по образцу существующих, issue не закрывать; для #334/#323/#309 дополнительно запрашивать у пользователя журналы/подтверждение.
- Версия поднимается в csproj; CHANGELOG.md — новая секция на каждую версию; README.md — бейдж версии (строка 3) и, при необходимости, список возможностей.
- Сборка/публикация: Windows — `build-windows-single-file.ps1`/`build.ps1`, Linux — `package/linux` (AppImage/deb) и `publish/build_deb_win_*.py`; релиз на GitHub Releases с `release_body_*.md`.
- Никаких оценок времени; каждое изменение — минимальный коммит с упоминанием issue.