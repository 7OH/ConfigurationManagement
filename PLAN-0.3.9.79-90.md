# PLAN — серия 0.3.9.79 … 0.3.9.90 — 12 новых фич

Репозиторий [`sivatorov/ConfigurationManagement`](https://github.com/sivatorov/ConfigurationManagement),
локальная копия `f:\Yandex.Disk\h\Configuration_Management`, ветка `main`, HEAD `fa31d2c` (0.3.9.78, релиз опубликован).

Режим: Архитектор (план) → задачи-исполнители. **Одна фича = одна задача = одна версия = один коммит.**
Issues для фич нет (это новые возможности, не фиксы) — комментарии в issues не публикуются.
В конце серии: сборка single-file обеих платформ, push, **ОДИН релиз v0.3.9.90** с двумя артефактами.

## 0. Правила каждой задачи (как в предыдущих сериях)

1. Правка кода на ОБОИХ платформах (WPF + Avalonia), где применимо.
2. Версия в 4 полях `Configuration Management.csproj` (строки 62–65).
3. Запись в `CHANGELOG.md` (`## [X.Y.Z.N] — дата`, разделы «Добавлено/Изменено/Исправлено»).
4. `README.md`: бейдж версии (строка 3) + при необходимости раздел возможностей.
5. `dotnet build` (WPF) + `dotnet build -p:ForceLinux=true` (Avalonia) + `dotnet test` — зелёные.
6. Один коммит: `feat: <суть>; 0.3.9.N`.
7. Новые чистые .NET файлы (`ViewModels/Xxx.cs`, `Models/Xxx.cs`, `Services/Xxx.cs`) — ЯВНО
   добавить в csproj Linux-ItemGroup (глоб `ViewModels\**\*.cs` там удалён; `Views/*Window.Avalonia.cs`
   и `*.Avalonia.cs` покрываются глобами; `MainWindow.Avalonia.*.cs` — явно).
8. Локализация: `Localization/Languages/ru.json` + `en.json` (встроенные ресурсы, LogicalName cm_lang_).
9. PLAN-файлы остаются untracked; CHANGELOG/README коммитятся.

## 1. Сводка

| № | Версия | Фича | Ключевые точки |
|---|--------|------|----------------|
| 1 | 0.3.9.79 | CLI: `--run "База" [--designer]`, `--list` | App.xaml.cs:160 (рядом с ExplorerCommandLine), App.axaml.cs (Linux), новый `Services/CommandLineHandler.cs`, `IOneCLauncher` |
| 2 | 0.3.9.80 | Избранное в меню трея | `MainWindow.Tray.cs:209` RebuildTrayMenu, `MainWindow.Avalonia.Tray.cs:143` FillTrayMenu (+Signature) |
| 3 | 0.3.9.81 | Командная палитра Ctrl+K | Новый `Views/CommandPaletteWindow.*` + чистый VM, хоткей `HotkeyCommandPalette` (по образцу HotkeyFindInList) |
| 4 | 0.3.9.82 | Индикатор «база запущена» | Новый `Services/RunningInfobasesService(.Linux/.Windows)`, флаг `Infobase.IsRunning`, точка в строке, таймер VM |
| 5 | 0.3.9.83 | Клонирование файловой ИБ | Контекстное меню базы (обе платформы), рекурсивная копия каталога, новая запись Infobase |
| 6 | 0.3.9.84 | Экспорт/импорт выбранных баз и групп | Окно-чеклист поверх `InfobaseExportData` (уже есть, Tools.cs:513/Avalonia.Tools.cs:303) |
| 7 | 0.3.9.85 | Приватные базы | `Infobase.IsPrivate`, показ только при разблокированном профиле с паролем (ProfileService) |
| 8 | 0.3.9.86 | Ротация копий + колонка «Последняя копия» | `BackupScenario.KeepCount/DeleteOlderThanDays`, `BackupService.RunAsync` (чистка+метка `Infobase.LastBackupUtc`), колонка по образцу «Дата изменений» |
| 9 | 0.3.9.87 | chdbfl по расписанию | `ScheduledTaskKind.CheckIntegrity`, `SchedulerService.ExecuteAsync` → `IInfobaseAdminService.CheckIntegrity`, окно задания: выбор файловой ИБ |
| 10 | 0.3.9.88 | Пакетное обновление из хранилищ | «Утилиты» → новое окно: базы с заполненным `Infobase.Repository`, батч через OneCLauncher.DesignerBatch (/ConfigurationRepositoryUpdateCfg + /UpdateDBCfg) |
| 11 | 0.3.9.89 | Центр обслуживания (дашборд) | Окно-сводка: доступность, последняя копия, кэш, размер, устаревшие конфигурации (UpdateCheckCache) |
| 12 | 0.3.9.90 | Мультивыделение + пакетные операции | `BatchSelectedIds` в VM, Ctrl/Shift-клики в дереве (WPF row template + Avalonia LeveledTreeView), пакетные команды в контекстном меню |

Порядок выбран так: инфраструктурные мелочи первыми (79–81), детект процессов перед клоном (82→83),
экспорт перед приватностью (84→85), ротация перед дашбордом (86→89). Мультивыделение — последней
(самая инвазивная, пакетные команды используют наработки 84–86).

## 2. Ключевые якоря кодовой базы (разведка)

### Стартап / CLI (0.3.9.79)
- WPF: `App.xaml.cs` `OnStartup` — после `ProfileBackupService.DataDirectoryResolver` (строка ~120),
  `ExplorerCommandLine.TryHandle(e.Args)` на строке 160 — рядом добавить `CommandLineHandler.TryHandle(e.Args)`
  ДО проверки single-instance (второй процесс сам запускает базу, IPC не нужен).
- Linux: `App.axaml.cs` `OnFrameworkInitializationCompleted` — аналогично после загрузки settings (~202),
  аргументы: `App.axaml.cs` не получает args — взять `Environment.GetCommandLineArgs()` (см. Program.cs Main(args)).
- Обработчик: новый `Services/CommandLineHandler.cs` (чистый .NET, без #if, включить в Linux-глоб — Services не глючит глобом):
  `--run <имя|Id>` / `--run=<имя>` [--designer] [--list], `--list` печатает «Name\tId\tТип» в stdout и завершает процесс.
- Поиск базы: по Id, затем по имени (OrdinalIgnoreCase, первое совпадение); не найдена — stderr + exit code 1.
- Запуск: `IOneCLauncher.Launch(ib, OneCLaunchMode.Enterprise|Configurator)`; после успеха — обновить
  `LastLaunchDate`/`AddLaunchHistory(...,"cli")` и `repository.Save(bases)`; exit 0.
- Профили: если профилей >1 — берётся активный профиль по умолчанию (без окна входа в CLI-режиме).
- Ярлык: `<exe> --run "База"`. Тест: парсер аргументов (чистый метод Parse) → `ConfigurationManagement.Tests/CommandLineHandlerTests.cs`.

### Трей (0.3.9.80)
- WPF `MainWindow.Tray.cs`: `RebuildTrayMenu` (209) — секция избранного ПЕРЕД «Недавними»:
  `_viewModel.FavoriteHotkeyIds` → `FindByFavoriteKey(key)` → пункты с подменю `AttachLaunchSubmenu`.
  Иконка: TrayIconKind.Enterprise/новый Star (рисуется в DrawMaterialTrayIcon, 335).
- Avalonia `MainWindow.Avalonia.Tray.cs`: `FillTrayMenu` (143) + `TrayMenuSignature` (130) — добавить избранное
  (иначе меню не пересоберётся при смене слотов). `TrayInfobaseItem` уже умеет подменю.
- Запуск из трея: WPF `LaunchInfobaseById(id, isConfigurator)` (Commands.cs:620), Avalonia `LaunchInfobase(ib, cfg)` (Tray.cs:286).

### Палитра Ctrl+K (0.3.9.81)
- Окно: `Views/CommandPaletteWindow.xaml`(+.cs) WPF; `CommandPaletteWindow.axaml`(+.Avalonia.cs) Linux
  (образец: NameInputWindow.xaml/.axaml — уже двухплатформенное имя). Окно безрамочное, по центру владельца,
  TextBox + ListBox, Enter — выполнить, Esc — закрыть.
- Чистая VM `ViewModels/CommandPaletteViewModel.cs` (включить в csproj явно): элементы = базы (все видимые)
  + команды (запуск/конфигуратор/избранное/настройки/синхронизация/очистка кэша/проверка доступности/…),
  fuzzy-фильтр (простой substring + подстроки по словам), ранжирование: имя с начала > вхождение > команда.
- Результат: `CommandPaletteResult { Kind = Base|Command, InfobaseId, CommandId }`; окно возвращает результат,
  MainWindow исполняет (запуск через `_viewModel`-команды).
- Хоткей: `AppSettings.HotkeyCommandPalette` = "Ctrl+K"; регистрация `MainWindow.Hotkeys.cs` +
  `MainWindow.Avalonia.Hotkeys.cs` (по образцу HotkeyFindInList: AppSettings → SettingsWindow row → Hotkeys).

### Индикатор «база запущена» (0.3.9.82)
- Новый `Services/RunningInfobasesService.cs` (интерфейс + Windows-реализация `#if WINDOWS`: System.Management
  Win32_Process WHERE Name LIKE '1cv8%' → CommandLine; Linux: обход `/proc/[0-9]*/cmdline`).
- Соответствие базе: файловая — подстрока пути `F="<каталог>"`/`/F <каталог>` в командной строке;
  серверная — `S="<srv>\<base>"`/`/S`; сравнение каталогов без регистра/хвостов-разделителей.
- VM: `ObservableCollection`-независимый флаг `Infobase.IsRunning` (SetProperty) + обновление
  `RefreshRunningFlags()` из DispatcherTimer (10 с, старт в конструкторе обеих VM, стоп не нужен — жизнь VM).
- UI: зелёная точка рядом с именем (DataTrigger на IsRunning в шаблоне строки WPF; в Avalonia BuildInfobaseRow).
- Подсказка точки: «База сейчас запущена (N процессов)». Windows-only пакет System.Management уже подключён.

### Клонирование ИБ (0.3.9.83)
- Контекстное меню базы (только файловые): «Дублировать базу…» — WPF MainWindow.xaml ContextMenu +
  OnBaseContextMenu_Opened-логика (MainWindow.Columns.cs:73) видимость; Avalonia BuildRowContextMenu (Tree.cs:1632).
- Диалог: имя клона (NameInputWindow переиспользовать или своё), группа клона (GroupPickerWindow), копировать ли
  пользователей/пароли (чекбокс). Подтверждение с размером каталога.
- Копия: рекурсивный DirectoryCopy в `<родитель>\Имя - Копия` (если занято — суффикс 2,3…); запрет, если база
  запущена (0.3.9.82) или путь не существует. Прогресс — окно с индетерминированным баром (модально).
- Результат: новый Infobase (новый GUID Id), Connection.FilePath = новый каталог, SortOrder рядом с оригиналом;
  Save() + RebuildGroupTree().

### Экспорт/импорт (0.3.9.84)
- Полный экспорт уже есть: `MainViewModel.Tools.cs:513` (WPF) / `Avalonia.Tools.cs:303` (Linux) пишут `InfobaseExportData`
  (bases + groups). Новое: окно-чеклист выбора (дерево групп с чекбоксами баз, опции: «включая пароли»,
  «включая группы»), экспорт только отмеченных в JSON (тот же формат, поле `Version` добавить).
- Импорт: выбор файла → окно-чеклист найденных баз (по умолчанию все) → слияние: по Id существует — пропуск/замена
  (вопрос), иначе добавить; группы слить по имени. Прогресс/итог — диалог.
- Команды: «Утилиты» → «Экспорт баз…» / «Импорт баз…» (рядом с существующим экспортом списка).

### Приватные базы (0.3.9.85)
- `Infobase.IsPrivate` (bool, JSON). Критерий показа: `!IsPrivate || ProfileUnlocked`.
- `ProfileService`: профиль с паролем; `IsPrivateBasesUnlocked` (session-флаг, сброс при смене профиля/выходе).
  Разблокировка: ввод пароля профиля (диалог по образцу AppLockWindow) — команда «Открыть приватные базы» в меню
  профиля/«Утилиты»; авто-разблокировка при входе через LoginWindow с паролем.
- Скрытие: `RebuildGroupTree`/`RebuildTree` (обе VM) — фильтр по IsPrivate&&locked; поиск, тег-панель, треи, палитра (81),
  CLI (79 — отказ с сообщением), недавние. Правка флага: окно свойств базы (ConnectionSettingsWindow, чекбокс;
  при включении без пароля профиля — предложить задать пароль).
- Пароль профиля хранится PBKDF2 (PasswordHasher, формат «100000.saltB64.hashB64»).

### Ротация + колонка «Последняя копия» (0.3.9.86)
- `BackupScenario`: `KeepCount` (int, 0 — хранить все), `DeleteOlderThanDays` (int, 0 — не чистить по возрасту).
  Окно редактирования сценария (BackupScenarioEditWindow WPF+VM `BackupScenarioEditViewModel` + Avalonia): два числовых поля.
- Чистка: в `BackupService.RunAsync` после успешного копирования — по КАЖДОМУ каталогу назначения: файлы
  `*<extension>` с именем, начинающимся с `{Base}-префиксной части` шаблона (безопасно: префикс базы), сортировка по
  LastWriteTime desc → удалить за пределами KeepCount и старше DeleteOlderThanDays. Лог удалений в BackupRunResult.PurgedFiles.
- Метка: `Infobase.LastBackupUtc` (+`LastBackupDisplay`) — проставляется в RunAsync успешно; сериализуется.
- Колонка «Последняя копия»: по образцу «Дата изменений» (AppSettings.ShowModifiedColumn/ModifiedColumnWidth →
  ShowLastBackupColumn/LastBackupColumnWidth; MainWindow.Columns.cs + Avalonia.Columns.cs + заголовок + ячейка; ColumnOrder).
- Чистая логика ротации — статический метод `BackupRotation.Apply(dir, prefix, ext, keep, days)` → тесты.

### chdbfl по расписанию (0.3.9.87)
- `ScheduledTaskKind.CheckIntegrity`; `ScheduledTask.InfobaseId` — файловая ИБ.
- `SchedulerService.ExecuteAsync` (143): case → найти ИБ, вызвать `IInfobaseAdminService.CheckIntegrity(ib)` —
  сейчас он открывает окно/запускает процесс; для планировщика нужен тихий запуск процесса с ожиданием —
  извлечь/добавить `CheckIntegrityQuiet(ib)` в реализацию сервиса, результат в BackupRunResult (Success/Message).
- Окно задания (ScheduledTaskEditWindow обе платформы): тип «Проверка целостности (chdbfl)» → выбор файловой ИБ.
- Список типов в ViewModel ScheduledTaskEditViewModel + локализация.

### Пакетное обновление из хранилищ (0.3.9.88)
- Проверить/расширить `OneCLauncher.DesignerBatch` (Windows) и `OneCLauncher.Linux.DesignerBatch.cs`:
  режим RepositoryUpdate: `/ConfigurationRepositoryF <адрес> /ConfigurationRepositoryN <user> /ConfigurationRepositoryP <pwd>
  /ConfigurationRepositoryUpdateCfg /UpdateDBCfg` (+ /ConfigurationRepositoryDumpCfg нет).
- Окно «Обновление из хранилищ» («Утилиты»): список баз с `Repository` заполненным (адрес непуст), чекбоксы,
  кнопка «Обновить выбранные» — последовательный прогон с логом построчно (успех/ошибка/время), итог.
- VM `ViewModels/RepositoryBatchUpdateViewModel.cs` (чистая, в csproj явно) + окна WPF/Avalonia.
- Безопасность: подтверждение; пропуск баз «запущена» (0.3.9.82); результат — AddLaunchHistory("RepoUpdate").

### Центр обслуживания (0.3.9.89)
- Окно «Центр обслуживания» («Утилиты», после «Консоли серверов»): таблица всех баз × колонки:
  Доступность (IsAvailable/последняя проверка), Последняя копия (0.3.9.86), Размер ИБ (FileSizeDisplay),
  Кэш (RefreshCacheSizeAsync при открытии), Конфигурация/версия, Возраст данных (FileLastWriteTimeUtc),
  «Обновление» (UpdateCheckCache по UpdateConfigCode — последний результат).
- Фильтр-переключатели: «Только проблемы» (недоступна / копия старше N дней / кэш > 1 ГБ / проверка обновлений
  показывала новый релиз). Кнопки: «Проверить доступность», «Открыть базу» (переход к строке через FindInList-механику),
  двойной клик — выбрать базу в главном окне.
- VM `ViewModels/MaintenanceCenterViewModel.cs` (чистая, строки — MaintenanceCenterRowViewModel) + окна WPF/Avalonia.

### Мультивыделение (0.3.9.90)
- VM (обе): `HashSet<string> BatchSelectedIds` + события `BatchSelectionChanged`; методы
  `ToggleBatchSelection(ib, modifier)`, `SelectRange(from,to)`, `ClearBatchSelection()`;
  `SelectedInfobase` остаётся «якорем» (последний клик без Ctrl).
- WPF: обработчик PreviewMouseLeftButtonDown на TreeViewItem строки (MainWindow.Tree.cs — где сейчас клик/выделение):
  Ctrl — toggle, Shift — диапазон по видимому порядку; визуал — DataTrigger `IsBatchSelected` (bool-свойство на
  Infobase, служебное, не сериализуется — [JsonIgnore]) → фон строки вторичной выделенности.
- Avalonia: `LeveledTreeView.Avalonia.cs` (клики) + `MainWindow.Avalonia.Tree.cs` BuildInfobaseRow — тот же паттерн,
  класс фона строки.
- Пакетные операции: блок в контекстном меню строки «Для выделенных (N)…» (виден при N>1): назначить тег,
  переместить в группу (GroupPickerWindow), добавить в избранное, выполнить сценарий резервирования (выбор),
  проверить доступность, удалить (общий DeleteInfobaseWindow в цикле). Приватные/клоны не мешают.
- Экспорт (0.3.9.84) может использовать мультивыделение как предвыбор — опционально.

## 3. Финал серии (после 0.3.9.90)

1. `dotnet build` + `dotnet build -p:ForceLinux=true` + `dotnet test` — финальная проверка.
2. `build-windows-single-file.ps1` → `dist/win-x64/ConfigurationManagement.exe`;
   `build-linux-single-file.ps1` → `dist/linux-x64/ConfigurationManagement`; проверить версии бинарников (0.3.9.90).
3. `publish/release_body_0.3.9.90.md` — сводка 12 фич по образцу release_body_0.3.9.78.md.
4. `git push origin main`; `gh release create v0.3.9.90 <оба артефакта> --notes-file …`.
5. Проверить релиз: ассеты, тело, автообновление (IsNewerThan по тегу).

## 4. Риски

- **0.3.9.90 мультивыделение** — самая инвазивная: касание выделения ломало поведение (#301/#300).
  Делать последней, только после стабильной серии; минимальный визуал (фон строки), без правки LeveledTreeView-геометрии.
- **0.3.9.88 конфигуратор в батче**: ключи хранилища варьируются по версиям платформы — тестировать на 8.3.27+;
  ошибки конфигуратора парсить из exit code + log-файла (/Out).
- **0.3.9.82 детект процессов**: WMI на части машин выключен — сервис обязан тихо деградировать (пустой набор).
- **0.3.9.85 приватность**: «забыл пароль» — базы недоступны; предупреждение в окне включения флага (без возможности восстановления — как в 1С).
- Каждый шаг: сборка обеих платформ + тесты ДО коммита; CS2001 obj-глюк лечится удалением obj/Debug.
