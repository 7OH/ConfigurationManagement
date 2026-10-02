# План исправлений: релизы 0.3.9.271 – 0.3.9.276

Репозиторий: github.com/sivatorov/ConfigurationManagement
Ветка: main (последний коммит 562f18c «0.3.9.270»)
Проект: Configuration Management — стартер 1С на C#/.NET, две платформы UI (Windows/WPF + Linux/Avalonia, общий код под `#if WINDOWS`/`#if LINUX`, зеркала `.xaml.cs` ↔ `.Avalonia.cs`).

## Версии и порядок выполнения

| Версия | Issue | Тема |
|---|---|---|
| 0.3.9.271 | #308 | Окно выбора скрипта: фокус на список + Enter = «Выполнить» |
| 0.3.9.272 | #333 | Учётные записи ИТС: удаление обновляет список, двойной клик, кнопка «Задать основным», отображение имени |
| 0.3.9.273 | #338 | Поле «Конфигурация» — выпадающий список с ручным вводом |
| 0.3.9.274 | #332 | Обрезка сегмента локали ru_RU при добавлении/правке базы |
| 0.3.9.275 | #339 | Кнопка-переключатель «Отбор запущенных» + активация окна запущенной базы + хоткей |
| 0.3.9.276+ | #309 | Горизонтальный скролл списка баз (диагностика + эксперимент) |

Правила для каждого выпуска:
- Номер версии поднимается в [Configuration Management.csproj](Configuration%20Management/Configuration%20Management.csproj:62) — поля `Version`, `AssemblyVersion`, `FileVersion`, `InformationalVersion` (строки 62–65).
- После каждой версии: `dotnet test` (проект `ConfigurationManagement.Tests`, ~1483 теста) и `dotnet build -p:BuildLinux=true`.
- Issues НЕ закрывать; после каждой версии оставлять комментарий в issue через скрипты `publish/comment-NNN-*.md` + `publish/show_issue.ps1`/gh CLI.
- Двухплатформенность: любое изменение UI делается в двух зеркалах — WPF (`.xaml`/`.xaml.cs`) и Avalonia (`.Avalonia.cs`); общая логика — в `ViewModels`/`Services` без директив платформы.

---

## 0.3.9.271 — #308 «Пожелание - Скрипты»

Окно `ScriptPickWindow` уже умеет двойной клик и кнопку «Выполнить». Не хватает: (а) фокуса на списке при открытии, (б) Enter = «Выполнить».

### WPF — [Views/ScriptPickWindow.xaml.cs](Configuration%20Management/Views/ScriptPickWindow.xaml.cs)
- В конструкторе после `LoadScenarios()`: выбрать первый пункт `ScenariosList.SelectedIndex = ScenariosList.Items.Count > 0 ? 0 : -1;` и дать фокус списку через `Loaded += (_, _) => { ScenariosList.Focus(); Keyboard.Focus(ScenariosList); }` (Focus до отображения окна может не сработать).
- Добавить обработчик `ScenariosList_KeyDown`: `e.Key == Key.Enter && Selected is not null` → `Run_Click(sender, e); e.Handled = true;`.
- Разметка [Views/ScriptPickWindow.xaml](Configuration%20Management/Views/ScriptPickWindow.xaml:41) — на `ListBox x:Name="ScenariosList"` добавить `KeyDown="ScenariosList_KeyDown"`.

### Linux — [Views/ScriptPickWindow.Avalonia.cs](Configuration%20Management/Views/ScriptPickWindow.Avalonia.cs)
- В `BuildRoot()` для `_list` подписаться на `KeyDown`: `e.Key == Key.Enter && Selected is not null` → `await RunSelectedAsync(); e.Handled = true;`.
- Фокус и первый пункт: после `Reload()` в `Opened += (_, _) => { if (_list.ItemCount > 0 && _list.SelectedIndex < 0) _list.SelectedIndex = 0; _list.Focus(); }`. Учесть, что `ModalWindowBase` — обёртка над `Window` с событием `Opened`.

### Тесты
UI-поведение юнит-тестами не покрывается. Проверка: `dotnet test` + сборка обеих платформ.

---

## 0.3.9.272 — #333 «Учетки доступа к ИТС»

Четыре замечания пользователя. Диагноз по коду:

1. **Список после удаления не обновляется** — найдена причина: [ItsAccountsViewModel.cs](Configuration%20Management/ViewModels/ItsAccountsViewModel.cs:77) `Delete(string id)` вызывает `_store.Delete(id)` БЕЗ `Reload()`, тогда как `Add`/`Update`/`OnSetPrimary` пересобирают `Rows`. Запись исчезает только после перезапуска.
2. **Кнопка «Задать основным» есть, но не видна** — в WPF это колонка с иконкой-звездой [ItsAccountsWindow.xaml](Configuration%20Management/Views/ItsAccountsWindow.xaml:177) без текста (только ToolTip); в Avalonia — текстовая кнопка [ItsAccountsWindow.Avalonia.cs](Configuration%20Management/Views/ItsAccountsWindow.Avalonia.cs:121). Требуется единообразная видимая текстовая кнопка.
3. **Двойной клик по строке → правка** — обработчика нет ни в WPF DataGrid, ни в Avalonia `BuildRow`.
4. **Отображение ключа (id) вместо наименования** — по коду все ComboBox выбора учётки имеют `DisplayMemberPath="Name"`/`DisplayMemberBinding=Name`: WPF [SettingsWindow.xaml](Configuration%20Management/Views/SettingsWindow.xaml:1471), [ConfigTypeEditWindow.xaml](Configuration%20Management/Views/ConfigTypeEditWindow.xaml:111), Avalonia [SettingsWindow.Avalonia.cs](Configuration%20Management/Views/SettingsWindow.Avalonia.cs:359), [ConfigTypeEditWindow.Avalonia.cs](Configuration%20Management/Views/ConfigTypeEditWindow.Avalonia.cs:37). Реальный риск: `ItsAccount.Name` пустой/`null` — тогда пункт отображается пустой строкой, а выбор падает на виртуальную «Основную». Страховка — нормализация имени при построении списка.

### Правки
- [ItsAccountsViewModel.cs](Configuration%20Management/ViewModels/ItsAccountsViewModel.cs:77): `Delete`: после `_store.Delete(id)` добавить `Reload()`.
- [ItsAccountsWindow.xaml](Configuration%20Management/Views/ItsAccountsWindow.xaml:177): колонку «Задать основным» сделать заметной — иконка + текст `{loc:Loc ItsAccounts.SetPrimary}` (ширину колонки увеличить до ~150, стиль `SelectAllButtonStyle` сохранить). Команду `SetPrimaryCommand` оставить.
- [ItsAccountsWindow.xaml](Configuration%20Management/Views/ItsAccountsWindow.xaml:124): на `DataGrid x:Name="AccountsGrid"` добавить `MouseDoubleClick="AccountsGrid_MouseDoubleClick"`.
- [ItsAccountsWindow.xaml.cs](Configuration%20Management/Views/ItsAccountsWindow.xaml.cs): добавить обработчик: если `AccountsGrid.SelectedItem is ItsAccountItemViewModel row` → `OnEditRow(row); e.Handled = true;`.
- [ItsAccountsWindow.Avalonia.cs](Configuration%20Management/Views/ItsAccountsWindow.Avalonia.cs:71): в `BuildRow` на корневой `grid` подписаться `grid.DoubleTapped += (_, _) => OnEditRow(account);`.
- [ItsAccountsViewModel.cs](Configuration%20Management/ViewModels/ItsAccountsViewModel.cs:119) / `ItsAccountSelectionBuilder.Build`: при пустом имени записи подставлять локализованный плейсхолдер (например, `ItsAccounts.Untitled` или «Без имени») — исключает пустые пункты, из-за которых выбор «не читается». Проверить локализацию `ru.json`/`en.json` в [Localization/Languages](Configuration%20Management/Localization/Languages).

### Тесты
- [ItsAccountsStoreTests.cs](ConfigurationManagement.Tests/ItsAccountsStoreTests.cs): добавить/подтвердить кейс «Delete удаляет из Load()».
- Новый кейс для `ItsAccountsViewModel`: `Add` → `Delete` → коллекция `Rows` больше не содержит удалённой записи (ObservableCollection не требует UI-потока).
- `ItsAccountSelectionBuilderTests`: пустое имя → плейсхолдер вместо пустой строки.

---

## 0.3.9.273 — #338 «Выбор конфигурации из списка»

Аналог готового паттерна «Сервер 1С»: редактируемый `ComboBox` с `ItemsSource AvailableServers` + `Text`-binding ([ConnectionSettingsWindow.xaml](Configuration%20Management/Views/ConnectionSettingsWindow.xaml:389)) и наполнением из баз списка (`MainViewModel.GetAvailableServers()`).

### Правки
- [ConnectionSettingsViewModel.cs](Configuration%20Management/ViewModels/ConnectionSettingsViewModel.cs:343): по образцу `AvailableServers` добавить `public ObservableCollection<string> AvailableConfigurations { get; } = new();` и `public void SetAvailableConfigurations(IEnumerable<string>? configurations)` — фильтр пустых, `Trim()`, `Distinct(OrdinalIgnoreCase)`, `OrderBy(OrdinalIgnoreCase)`.
- WPF [ConnectionSettingsWindow.xaml](Configuration%20Management/Views/ConnectionSettingsWindow.xaml:1152): поле «Конфигурация» — TextBox (строка 1159) заменить на `ComboBox` по образцу сервера: `IsEditable="True" IsTextSearchEnabled="False" StaysOpenOnEdit="True" ItemsSource="{Binding AvailableConfigurations}" Text="{Binding ConfigurationName, UpdateSourceTrigger=PropertyChanged}"`, стиль `ModernComboBox`. Колонка «Версия конфигурации» и кнопка «Определить» не меняются.
- Linux [ConnectionSettingsWindow.Avalonia.cs](Configuration%20Management/Views/ConnectionSettingsWindow.Avalonia.cs:1057): `var configName = Tb("ConfigurationName");` заменить на редактируемый `ComboBox` (как `_serverBox`/ComboBox сервера в этом окне): `IsEditable = true`, `ItemsSource` → `AvailableConfigurations`, `Text`-binding на `ConfigurationName`.
- WPF [MainViewModel.Commands.cs](Configuration%20Management/ViewModels/MainViewModel.Commands.cs:146): добавить `GetAvailableConfigurations()` (по `Infobases`: `ConfigurationName` непустые → distinct → sorted) и передавать в оба вызова конструктора `ConnectionSettingsWindow` (строки 99–103 — добавление, 250–254 — правка) новым параметром `availableConfigurations`.
- Linux [MainViewModel.Avalonia.cs](Configuration%20Management/ViewModels/MainViewModel.Avalonia.cs:1258) и (Configuration Management/ViewModels/MainViewModel.Avalonia.cs:1409): аналогично — формировать список конфигураций рядом с `AvailableServers()` (1483) и передавать в окно.
- [ConnectionSettingsWindow.xaml.cs](Configuration%20Management/Views/ConnectionSettingsWindow.xaml.cs:65) и Avalonia-зеркало: в конструкторе вызвать `_viewModel.SetAvailableConfigurations(...)`.

### Подводные камни
- Редактируемый ComboBox в WPF: при `IsTextSearchEnabled=True` ввод текста конфликтует с автодополнением — у сервера уже стоит `False`; повторить для конфигурации.
- Значение поля при сохранении должно браться из `Text` (свободный ввод), а не из `SelectedItem` — binding `Text` это обеспечивает; проверять на регресс «введённая вручную конфигурация теряется» (был issue #164).

### Тесты
- Новый [ConnectionSettingsViewModelTests.cs](ConfigurationManagement.Tests): `SetAvailableConfigurations` — дедупликация, сортировка, отсев пустых.
- Расширить `Etap13ListStateTests`/соседние тесты VM при необходимости.

---

## 0.3.9.274 — #332 «Обработка ссылки на базу с сегментом локали»

Фикс 0.3.9.234 касался только «Перейти по ссылке» (`OneCLauncher.StripWebLocaleSegment`). Нужно то же самое при сохранении свойств базы.

### Правки
- [ConnectionSettingsViewModel.cs](Configuration%20Management/ViewModels/ConnectionSettingsViewModel.cs:1294): в `ApplyTo` строка `conn.WebUrl = WebUrl;` → `conn.WebUrl = OneCLauncher.StripWebLocaleSegment(WebUrl) ?? string.Empty;`. Это общая точка для WPF и Avalonia (оба `OnSave_Click` вызывают `ApplyTo`), единое место правки.
- Поведение: тихое обрезание (как 1CEStart), без диалога. При желании добавить одну строку-подсказку в `ToolTip` поля «URL публикации» в [ConnectionSettingsWindow.xaml](Configuration%20Management/Views/ConnectionSettingsWindow.xaml:427) и Avalonia-версии.
- Проверить, что это не ломает «Перейти по ссылке» (там хелпер уже применяется в [OneCLauncher.Arguments.cs](Configuration%20Management/Services/OneCLauncher.Arguments.cs:167)).

### Тесты
- [WebLinkLocaleTests.cs](ConfigurationManagement.Tests/WebLinkLocaleTests.cs) уже покрывает сам хелпер. Добавить сквозной тест: `ConnectionSettingsViewModel.ApplyTo` на базе с `ConnectionType.WebServer` и `WebUrl = https://host/base/ru_RU/` → в результате `Connection.WebUrl == https://host/base/`; для файловой/КС базы URL не меняется.

---

## 0.3.9.275 — #339 «Отбор запущенных»

Кнопка-переключатель в панели списка баз; при включении показываются только запущенные базы (индикатор `Infobase.IsRunning` уже есть — монитор [MainViewModel.Running.cs](Configuration%20Management/ViewModels/MainViewModel.Running.cs), опрос раз в 10 сек). При активном отборе двойной клик и Enter активируют окно запущенной базы (игнорируя глобальную настройку двойного клика). Хоткей — настраиваемый.

### Модель и ViewModel (общий код)
- [ListViewMode.cs](Configuration%20Management/Models/ListViewMode.cs): добавить `Running` в enum.
- [AppSettings.cs](Configuration%20Management/Models/AppSettings.cs): добавить `ShowRunningOnly` (bool, default false) и `HotkeyShowRunning` (string, default "" — как у ShowAll).
- WPF [MainViewModel.Display.cs](Configuration%20Management/ViewModels/MainViewModel.Display.cs:143): в сеттере `ListViewMode` синхронизировать `_showFavoritesOnly` и добавить `OnPropertyChanged(nameof(IsListModeRunning))`; свойство `IsListModeRunning`; в сеттере режима Running дополнительно вызвать `RefreshRunningFlags()` (мгновенная актуализация флагов, без ожидания таймера).
- Avalonia [MainViewModel.Avalonia.Display.cs](Configuration%20Management/ViewModels/MainViewModel.Avalonia.Display.cs:265): аналогично (там `_listMode` строковый: "All"/"Favorites"/"Recent" → добавить "Running", поле `_settings.ShowRunningOnly`).
- WPF фильтр [MainViewModel.Theme.cs](Configuration%20Management/ViewModels/MainViewModel.Theme.cs:542) `EnumerateFilteredInfobases`: ветка `else if (mode == ListViewMode.Running) source = source.Where(i => i.IsRunning);`.
- Avalonia фильтр [MainViewModel.Avalonia.cs](Configuration%20Management/ViewModels/MainViewModel.Avalonia.cs:928): `IsFilterModeActive` уже сработает (`_listMode != "All"`), а в `MatchesFilter` (≈строка 1039) добавить условие `if (_listMode == "Running" && !ib.IsRunning) return false;`.
- Команда: WPF [MainViewModel.Commands.cs](Configuration%20Management/ViewModels/MainViewModel.Commands.cs:561) и Avalonia [MainViewModel.Avalonia.Commands.cs](Configuration%20Management/ViewModels/MainViewModel.Avalonia.Commands.cs:94): `ShowRunningCommand = new RelayCommand(() => IsListModeRunning = true);` + пункты палитры команд (WPF ~1573, Avalonia ~314).
- Хоткей (провести через все точки, где живут HotkeyShow*):
  - Свойство `HotkeyShowRunning` + нормализация в [MainViewModel.Commands.cs](Configuration%20Management/ViewModels/MainViewModel.Commands.cs:1383) (рядом с `HotkeyShowFavorites`);
  - Перенос в [MainViewModel.Launch.cs](Configuration%20Management/ViewModels/MainViewModel.Launch.cs:675) (структура настроек), [MainViewModel.SwitchUser.cs](Configuration%20Management/ViewModels/MainViewModel.SwitchUser.cs:169), [MainViewModel.Tools.cs](Configuration%20Management/ViewModels/MainViewModel.Tools.cs:2463) `ApplyDisplaySettings`, инициализация в [MainViewModel.cs](Configuration%20Management/ViewModels/MainViewModel.cs:442);
  - Регистрация: WPF [MainWindow.Hotkeys.cs](Configuration%20Management/Views/MainWindow.Hotkeys.cs:67), Linux [MainWindow.Avalonia.Hotkeys.cs](Configuration%20Management/Views/MainWindow.Avalonia.Hotkeys.cs:133);
  - Настройка: WPF [SettingsWindow.Hotkeys.cs](Configuration%20Management/Views/SettingsWindow.Hotkeys.cs:39) `BindHotkeyBox(HotkeyShowRunningBox, ...)` + разметка (строка рядом с `HotkeyShowAllBox`), Linux [SettingsWindow.Avalonia.cs](Configuration%20Management/Views/SettingsWindow.Avalonia.cs:2801) `HotkeyRow(...)`.

### UI-кнопка
- WPF [MainWindow.xaml](Configuration%20Management/Views/MainWindow.xaml:393): в `TabsPanel` после RadioButton «Недавние» добавить ToggleButton/RadioButton с `IsChecked="{Binding IsListModeRunning, Mode=TwoWay}"`, значок — одноцветная точка (Ellipse ~14–16px) крупнее индикатора в списке, ToolTip `Main.RunningTooltip`, заголовок `Main.Running`.
- Linux [MainWindow.Avalonia.cs](Configuration%20Management/Views/MainWindow.Avalonia.cs:1042): в `BuildListModeSegments` добавить `SegmentButton` с иконкой-точкой и `Bind(ToggleButton.IsCheckedProperty, new Binding("IsListModeRunning") { Mode = BindingMode.TwoWay })`.

### Активация окна запущенной базы
- Общая точка: WPF [MainWindow.Events.cs](Configuration%20Management/Views/MainWindow.Events.cs:445) `ActivateInfobaseByDoubleClickAction` — в начале: `if (_viewModel.IsListModeRunning && infobase.IsRunning) { _viewModel.ActivateRunningInfobase(infobase); return; }`. Enter на WPF идёт через эту же точку ([MainWindow.Hotkeys.cs](Configuration%20Management/Views/MainWindow.Hotkeys.cs:886)).
- Linux: [LeveledTreeView.Avalonia.cs](Configuration%20Management/Controls/LeveledTreeView.Avalonia.cs:452) `ActivateInfobaseLikeDoubleClick` — аналогичная проверка перед `ResolveDoubleClickAction`.
- Новый метод `MainViewModel.ActivateRunningInfobase(Infobase ib)` с partial-реализациями:
  - Windows — новый [Services/OneCWindowActivator.Windows.cs]: `IRunningInfobasesService.GetRunningDetails()` → PID процесса, чья `CommandLine` матчится через `RunningInfobaseMatcher.MatchesCommandLine(ib, cmd)` → `EnumWindows`/`GetWindowThreadProcessId` (DllImport user32, примеры уже есть в [App.xaml.cs](Configuration%20Management/App.xaml.cs:651) и [ToolTipCloser.cs](Configuration%20Management/Views/ToolTipCloser.cs:509)) → видимое окно → `ShowWindow(SW_RESTORE)` + `SetForegroundWindow`.
  - Linux — [Services/OneCWindowActivator.Linux.cs]: вернуть false (активация чужих окон на X11 требует wmctrl/xdotool, в проект не добавлять; в комментарии issue указать ограничение).

### Локализация
[ru.json](Configuration%20Management/Localization/Languages/ru.json) / en.json: ключи `Main.Running`, `Main.RunningTooltip`, `Settings.Hotkeys.ShowRunning`, плейсхолдер для #333.

### Тесты
- Расширить `Etap13ListStateTests` (уже умеет строить VM с Infobases): кейс «режим Running показывает только базы с IsRunning=true» и «двойной клик в режиме Running вызывает ActivateRunningInfobase, а не Launch».
- Хоткей-нормализация покрывается существующими тестами горячих клавиш (`EtapHotkeysFavoritesTests`), при необходимости расширить.

---

## 0.3.9.276+ — #309 «Пропал горизонтальный скрол»

Проблема только Windows/WPF (Linux/Avalonia не воспроизводится). Уже 7 попыток (0.3.9.102–0.3.9.258); текущее состояние:
- [MainWindow.xaml](Configuration%20Management/Views/MainWindow.xaml:1345): `MainTree` — TreeView с внутренним ScrollViewer: `CanContentScroll=True`, `VirtualizingPanel.IsVirtualizing=True`, `VirtualizationMode=Recycling`, `ScrollUnit=Pixel`, ItemsPanel `VirtualizingStackPanel`.
- [MainWindow.Columns.cs](Configuration%20Management/Views/MainWindow.Columns.cs:734) `UpdateTreeMinWidth`: сумма колонок через `ListMinWidthCalculator.Compute`, MinWidth ставится на `ScrollContentPresenter` и дублируется на корневые Grid материализованных строк; инструментальный лог `CM_COLUMNS` ([MainWindow.Columns.cs](Configuration%20Management/Views/MainWindow.Columns.cs:815)).
- [MainWindow.Scroll.cs](Configuration%20Management/Views/MainWindow.Scroll.cs:247): `OnTreeScroll_ScrollChanged` синхронизирует горизонталь заголовка с деревом и пересчитывает минимум по `ExtentWidthChange`.

Гипотеза: `VirtualizingStackPanel` при `CanContentScroll=True` меряет детей вьюпортной шириной, а Recycle-строки не всегда перемериваются после смены `MinWidth`; `ExtentWidth` остаётся меньше суммы колонок (`extent ≈ viewport < total`), поэтому полоса короткая и последние 2 колонки недостижимы. Это подтверждается логом `CM_COLUMNS`.

### Шаги
1. **Диагностика**: получить от пользователя строки `CM_COLUMNS` из журнала приложения (total, sumActualHeader, content, presenterMin, viewport, extent). Если `extent < total` — гипотеза подтверждена.
2. **Эксперимент A (быстрый)**: в `UpdateTreeMinWidth` после установки MinWidth на строки вызвать `treeScroll.InvalidateMeasure()` + `treeScroll.UpdateLayout()` (разово при изменении `_lastTreeMinWidth`), затем принудительно `ScrollToHorizontalOffset(ScrollableWidth)` — проверить, становится ли последняя колонка достижимой. Включить `LogColumnsDiagnostics` на время проверки.
3. **Эксперимент B (главный)**: динамическое переключение ItemsPanel между `VirtualizingStackPanel` и обычным `StackPanel` в code-behind ([MainWindow.Columns.cs](Configuration%20Management/Views/MainWindow.Columns.cs:734)), когда `total > viewport` (полоса реально нужна). StackPanel даёт честный `ExtentWidth = DesiredSize` (MinWidth строк = сумме колонок) — прокрутка гарантированно доходит до конца. Побочный эффект — потеря виртуализации на время полосы (оценить на больших списках; возможно, приемлемо, т.к. строки «лёгкие»).
4. **Эксперимент C (структурный, если B не решит)**: вынести горизонтальную прокрутку во внешний `ScrollViewer` (`CanContentScroll=False`, Horizontal=Auto, Vertical=Disabled) вокруг связки «HeaderGrid + MainTree»; внутреннему скроллу дерева отключить горизонталь. Звёздную колонку «Название» при `total > viewport` переводить в абсолютную ширину (по заголовку). Минус — более глубокая правка разметки и регресс-проверка компакт-режима, DnD, контекстных меню.
5. **Workaround-минимум** (если эксперименты не успевают в релиз): после каждого `UpdateTreeMinWidth` при необходимости принудительно прокручивать в конец; колонки станут доступны, даже если полоса рисуется короткой.

### Что не трогать
- [MainWindow.Avalonia.Columns.cs](Configuration%20Management/Views/MainWindow.Avalonia.Columns.cs) и Avalonia-разметку (проблемы нет).
- Логику сортировки/колонок и `ListMinWidthCalculator` (покрыт тестами [ListMinWidthCalculatorTests.cs](ConfigurationManagement.Tests/ListMinWidthCalculatorTests.cs)).

### Тесты
Юнит-тестов на WPF-раскладку нет (UI-зависимо); при вынесении новой чистой функции «нужна ли полоса / переключение панели» — покрыть её тестом в [ListMinWidthCalculatorTests.cs](ConfigurationManagement.Tests/ListMinWidthCalculatorTests.cs).

---

## Единые требования к качеству

- После каждого выпуска: `dotnet test` (Windows), затем `dotnet build -p:BuildLinux=true` (проверка обеих платформ).
- Комментарии в issues: готовить `publish/comment-<номер>-<версия>.md` (по образцу существующих `publish/comment-301-0.3.9.70.md`), issue не закрывать.
- Локализация ru/en для всех новых строк UI.
- Никаких оценок времени; каждое изменение — минимальным PR-подобным коммитом с упоминанием issue в сообщении.