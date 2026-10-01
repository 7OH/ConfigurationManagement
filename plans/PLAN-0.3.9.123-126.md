# PLAN — цикл 0.3.9.123–0.3.9.126 — встроенный монитор серверов 1С (RAC)

Репозиторий [`sivatorov/ConfigurationManagement`](https://github.com/sivatorov/ConfigurationManagement),
локальная копия `f:\Yandex.Disk\h\Configuration_Management`, ветка `main`.
Локальный HEAD — 0.3.9.122 (csproj строки 62–65, CHANGELOG, бейдж README строка 3).
План начинается с версии **0.3.9.123** (по заданию; перед стартом работ синхронизироваться
с origin: `git pull` — если в origin уже есть 0.3.9.123, перенумеровать цикл с 0.3.9.124).

Режим: Архитектор (план) → цепочка задач-исполнителей в режиме **code** (new_task по одному на этап).
**Один этап = одна версия = один коммит.** План-файл остаётся untracked (plans/ в .codeassistantignore);
CHANGELOG/README/csproj коммитятся. Релиз/публикация в этот цикл НЕ входит (автор сам).

---

## 1. Сводка

Новая функция «Встроенный монитор серверов 1С»: встроенное окно «Серверы 1С» (меню «Утилиты»,
горячая клавиша CTRL+ALT+S) вместо внешней оснастки `1CV8Servers.msc` / запуска `rac` без параметров.
Окно работает через утилиту `rac` (Remote Administration Client), распространяемую с платформой 1С
и доступную в каталоге `bin` платформы на Windows и Linux:

- подключение к серверу 1С (адрес, порт — по умолчанию 1540 ragent / 1545 RAS, логин/пароль администратора кластера);
- просмотр кластеров (uuid + имя), рабочих процессов (rphost: PID, память, потоки, занятость),
  сеансов (пользователь, база, время старта, состояние, блокировки), соединений, блокировок объектов,
  информации о выбранном кластере (`cluster info`);
- действия «Завершить сеанс» / «Разорвать соединение» с подтверждением;
- автообновление по таймеру раз в 5 с (по образцу Инспектора процессов) и ручное обновление;
- парсинг текстового вывода `rac` (list — таблица с разделителем `\t`; info — «ключ: значение»)
  на обеих платформах;
- темизация окна (светлая/тёмная, DynamicResource в WPF + ThemeBrushes.Bind в Avalonia),
  локализация ru/en;
- защита пароля администратора кластера: **пароль НЕ сохраняется на диск** (решение планирования,
  см. раздел 5); адрес/порт/логин сохраняются в AppSettings без пароля.

| № | Версия  | Этап | Суть | Сложность |
|---|---------|------|------|-----------|
| 1 | 0.3.9.123 | Сервисный слой rac | Модели + чистый парсер вывода rac + клиент запуска rac (поиск в bin платформы, аргументы, таймаут) + юнит-тесты парсера | средняя |
| 2 | 0.3.9.124 | Окно и ViewModel | `ServerMonitorWindow` (WPF+Avalonia), `ServerMonitorViewModel`, роу-модели, пункты меню «Утилиты», локализация, темизация, сохранение адреса/порта/логина | большая |
| 3 | 0.3.9.125 | Действия и автообновление | Завершение сеанса / разрыв соединения с подтверждением, таймер 5 с, ручное обновление, статусы/ошибки, юнит-тесты VM | средняя |
| 4 | 0.3.9.126 | Тесты и документация | Полный прогон dotnet test + build -p:BuildLinux=true, README-раздел, финализация CHANGELOG/локализации, ручной чек-лист | малая |

```mermaid
flowchart LR
    A[0.3.9.123 сервисный слой rac] --> B[0.3.9.124 окно и VM]
    B --> C[0.3.9.125 действия и автообновление]
    C --> D[0.3.9.126 тесты и документация]
```

Общие требования К КАЖДОЙ задаче:
1. Реализация на обеих платформах (Windows/WPF + Linux/Avalonia), где применимо; чистые сервисы/VM —
   без платформенных зависимостей (одна реализация), окна — пара WPF `.xaml` / Avalonia `.Avalonia.cs`.
2. Поднять версию в [`Configuration Management.csproj`](Configuration%20Management/Configuration%20Management.csproj:62)
   (строки 62–65: Version/AssemblyVersion/FileVersion/InformationalVersion).
3. Запись в CHANGELOG.md (сверху, формат как у прошлых версий) и обновить бейдж версии в
   README.md (строка 3).
4. Тесты: `dotnet test` зелёный (427+ текущих + новые) и `dotnet build -p:BuildLinux=true` без ошибок
   (компиляция Linux-ветки обязательна после каждого изменения).
5. Один коммит (без пуша). Сообщение по образцу: `feat: ...; 0.3.9.XXX`.
6. В конце задачи — запустить следующую задачу через `new_task(mode=code)` с инструкцией
   следующего пункта (полные тексты инструкций — в разделе 4).

---

## 2. Ключевые якоря кодовой базы (разведка выполнена 2026-09-28)

### Общее
- Версия: [`Configuration Management.csproj`](Configuration%20Management/Configuration%20Management.csproj:62) — 4 поля, строки 62–65. Локальный HEAD — 0.3.9.122.
- Регистрация сервисов DI: [`AppServices.cs`](Configuration%20Management/AppServices.cs:14) — общий блок (строки 20–73) для обеих платформ;
  чистые сервисы регистрируются в общем блоке, UI-зависимые — под `#if WINDOWS` / `#else`.
- Локализация: [`LocalizationManager.cs`](Configuration%20Management/Localization/LocalizationManager.cs:60) — ключи-строки в
  `Localization/Languages/ru.json` и `en.json`, вызов `LocalizationManager.T("Key")`, fallback ru→en→ключ.
  Образцы ключей: `ProcessInspector.*` (ru.json:1650–1667), `Admin.ServerConsole*` (ru.json:588–594).
- Темизация WPF: `{DynamicResource TextPrimaryBrush}` / `TextSecondaryBrush` / `CardBackgroundBrush` /
  `ItemHoverBrush` / `BorderBrush`; стили окон — implicit `Style TargetType="TextBlock"` и т.п.
  (образец: [`Views/ProcessInspectorWindow.xaml`](Configuration%20Management/Views/ProcessInspectorWindow.xaml:11)).
- Темизация Avalonia: [`Themes/ThemeBrushes.Avalonia.cs`](Configuration%20Management/Themes/ThemeBrushes.Avalonia.cs:14) —
  `ThemeBrushes.Bind(target, property, "BrushKey")`; статусные цвета, читаемые в обеих схемах (вывод 0.3.9.121):
  `#16A34A` (ок), `#D97706` (предупреждение), `#DC2626` (опасно), `#64748B` (нейтрально).
- Маскирование секретов в логах: [`Services/SensitiveDataMasker.cs`](Configuration%20Management/Services/SensitiveDataMasker.cs) —
  пароль rac в журнал НЕ писать (смотреть способ применения в проекте и использовать аналогично).

### Запуск внешней консоли (что заменяем/дополняем)
- [`Services/InfobaseAdminService.cs`](Configuration%20Management/Services/InfobaseAdminService.cs:171) —
  `OpenServerAdminConsole`: разрешает `binDir` через `ResolveBinDirectory` (строки 216–242:
  `OneCLauncher.ResolveArchitecture` + `PlatformVersionService.ResolveVersionBinDirectory` / `FindPlatformVersionDirs`),
  на Windows пробует `1CV8Servers.msc`, затем `FindInBinDir(binDir, "rac")` (строки 273–301, платформенное
  расширение `.exe`/без расширения + регистронезависимый поиск) и `Launch(rac, ...)`.
  **`FindInBinDir`/`ResolveBinDirectory` — кандидаты на вынос в общий helper** (например `PlatformPaths`/
  `OneCRacLocator`), чтобы клиент rac их переиспользовал.
- Команда меню WPF: [`ViewModels/MainViewModel.Admin.cs`](Configuration%20Management/ViewModels/MainViewModel.Admin.cs:55) —
  `OpenServerConsoleCommand` → `ExecuteOpenServerConsole` (строки 73–85), хоткей `HotkeyServerConsole`
  (CTRL+ALT+S, строки 33–42). Команда активна всегда (issue #295).
- Пункт меню WPF: [`Views/MainWindow.xaml`](Configuration%20Management/Views/MainWindow.xaml:678) —
  `<MenuItem Header="{loc:Loc Admin.ServerConsole}" Command="{Binding OpenServerConsoleCommand}"
  InputGestureText="{Binding HotkeyServerConsole}">` внутри подменю «Утилиты»
  (контекст: строки 642–752, рядом Инспектор процессов 751–752).
- Пункт меню Avalonia: [`Views/MainWindow.Avalonia.Tree.cs`](Configuration%20Management/Views/MainWindow.Avalonia.Tree.cs:1652) —
  `menu.Items.Add(MenuAction("Admin.ServerConsole", _vm.OpenServerConsoleCommand, _vm.HotkeyServerConsole, "IconServer", "#14B8A6"));`
  внутри `BuildUtilitiesMenu()` (строки 1632–1731; Инспектор — 1725).
- Avalonia-команда сервиса: [`ViewModels/MainViewModel.Avalonia.Admin.cs`](Configuration%20Management/ViewModels/MainViewModel.Avalonia.Admin.cs:23) —
  `HotkeyServerConsole`; реализация вызова `OpenServerAdminConsole` аналогична WPF.

### Инспектор процессов — образец для нового окна
- VM: [`ViewModels/ProcessInspectorViewModel.cs`](Configuration%20Management/ViewModels/ProcessInspectorViewModel.cs:17) —
  чистая VM: таймер `AutoRefreshIntervalMs = 5000` (строка 20), `Timer` (строки 58, 137–141),
  флаг занятости `Interlocked.Exchange(ref _refreshBusy, 1)` (строки 80–82), фон `Task.Run` +
  `_dispatchToUi` (null в тестах), подтверждение/ошибки через `IDialogService` (`KillSelected`, 107–127),
  `ObservableCollection<...>` строк, пересоздание коллекции в `ApplyRows` (167–179).
- Окно WPF: [`Views/ProcessInspectorWindow.xaml`](Configuration%20Management/Views/ProcessInspectorWindow.xaml:1) +
  [`Views/ProcessInspectorWindow.xaml.cs`](Configuration%20Management/Views/ProcessInspectorWindow.xaml.cs:26) —
  DataGrid с `DataGridTemplateColumn`, `CellStyle`, кнопки «Обновить/Завершить/Закрыть», hint; код-бихайнд
  получает сервисы из `AppServices`, создаёт VM с `dispatchToUi` через Dispatcher, `Closed += Dispose`.
- Окно Avalonia: [`Views/ProcessInspectorWindow.Avalonia.cs`](Configuration%20Management/Views/ProcessInspectorWindow.Avalonia.cs:29) —
  `ModalWindowBase`, ListBox + `FuncDataTemplate<...>`, `BuildRow` (Grid с колонками), `CellText`,
  `BuildActionButton` с `ControlThemes.ModernButton` и `ThemeBrushes.Bind`, `ShowDialogSync`.
- Команда WPF: [`ViewModels/MainViewModel.Tools.cs`](Configuration%20Management/ViewModels/MainViewModel.Tools.cs:2531) —
  `ProcessInspectorCommand` → `ExecuteProcessInspector` (2534–2543): `new ProcessInspectorWindow(Infobases.ToList(), ib => FindInListCommand.Execute(ib)) { Owner = ... }; ShowDialog()`.
- Команда Avalonia: [`ViewModels/MainViewModel.Avalonia.Tools.cs`](Configuration%20Management/ViewModels/MainViewModel.Avalonia.Tools.cs:1508) —
  аналогично, `_allInfobases.ToList()`, `ShowDialogSync(OwnerWindow())`.
- Тесты-образец парсинга: [`ConfigurationManagement.Tests/ProcessInspectorParsingTests.cs`](ConfigurationManagement.Tests/ProcessInspectorParsingTests.cs:11) —
  Theory/InlineData на чистых парсерах.

### Учётные данные и хранение
- Пароли профилей: PBKDF2 — [`Services/PasswordHasher.cs`](Configuration%20Management/Services/PasswordHasher.cs:13)
  (формат `итерации.сольBase64.хэшBase64`, совместим с ProfileService). **Для rac неприменим**: rac нужен
  ОТКРЫТЫЙ пароль (передаётся аргументом `--password`), PBKDF2 необратим. Решение — НЕ сохранять пароль
  вообще (см. раздел 5). Логин/адрес/порт — в `AppSettings` (plain), как остальные настройки интерфейса.
- Настройки: [`Models/AppSettings.cs`](Configuration%20Management/Models/AppSettings.cs:6) — POCO, сериализуется
  System.Text.Json (сохранение в `InfobaseRepository.SaveSettings/LoadSettings`); новые поля добавляются
  как обычные свойства. Образец поля с портом/хоткеем — `HotkeyServerConsole` (строки 619–621).
- Пример окна с PasswordBox и TabControl: [`Views/ConnectionSettingsWindow.xaml`](Configuration%20Management/Views/ConnectionSettingsWindow.xaml:1)
  (вкладки слева `ConnTabControl`/`ConnTabItem`, стили TextBox/ComboBox/CheckBox/GroupBox/Label →
  TextPrimaryBrush; поле пароля — стандартный WPF PasswordBox, его наличие проверить в XAML ниже строки 150).

### Клиент-серверные базы / выполнение процессов
- [`Services/ComReadHost.cs`](Configuration%20Management/Services/ComReadHost.cs:54) — паттерн «дочерний агент +
  stdin-протокол, пароль не в командной строке» (полезно как референс по безопасности, но для rac
  применяется прямой запуск с RedirectStandardOutput — rac не читает пароль из stdin).
- [`Services/InfobaseAdminService.cs`](Configuration%20Management/Services/InfobaseAdminService.cs:103) —
  образец async-запуска с `RedirectStandardOutput/Error`, параллельным чтением и таймаутом
  (`CheckIntegrityQuiet`, строки 103–168): взять за основу выполнения rac-команд.
- [`Services/ExternalCommandRunner.cs`](Configuration%20Management/Services/ExternalCommandRunner.cs:78) —
  общий образец Process+таймаут+kill, НО через shell (`cmd /c` / `sh -c`) — для rac не годится
  (нужен прямой запуск `rac.exe`/`rac` с аргументами и захватом stdout; shell ломает экранирование).
- Кодировка вывода rac: задать `StandardOutputEncoding = Encoding.UTF8` (русские имена баз/пользователей);
  проверить фактическую кодировку на Windows (см. риски).

---

## 3. Декомпозиция на этапы

### Этап 1 — 0.3.9.123: сервисный слой rac (структура + парсинг вывода)

**Цель:** чистый, тестируемый слой работы с rac без UI: модели данных, парсер текстового вывода,
клиент запуска rac (поиск исполняемого файла в каталоге платформы, сборка аргументов, выполнение
с таймаутом и захватом stdout/stderr).

**Новые файлы:**
- `Configuration Management/Models/RacModels.cs` — POCO:
  - `RacCluster { Guid Id; string Name; int Port; ... }` (uuid, имя, порт);
  - `RacProcessInfo { Guid Id; string Type; string Host; int Pid; int Port; DateTime StartedAt;
    long MemorySize; long MemoryTotal; long MemoryAvailable; long MemoryExcess; int Threads;
    double Cpu; double AvailablePerformances; bool Running; int Infobases; ... }` (rphost/rmngr);
  - `RacSessionInfo { Guid Id; Guid? InfobaseId; string User; string Host; string AppId;
    DateTime StartedAt; DateTime LastActiveAt; bool BlockedByLs; bool BlockedByDeadlock;
    long DbProcDuration; long DurationAll; long DurationCurrent; long DurationDbms;
    long DurationCpu; long DurationWait; long Memory; long Bytes; long Position; long Read; long Write;
    Guid ConnectionId; bool Hibernate; string State; ... }`;
  - `RacConnectionInfo { Guid Id; Guid SessionId; bool Blocked; string Connector; Guid ProcessId;
    string Host; int Port; DateTime EstablishedAt; DateTime LastConnectionTime; long Duration; ... }`;
  - `RacLockInfo { Guid Id; Guid SessionId; Guid InfobaseId; Guid ConnectionId; Guid TransactionId;
    bool Waiting; bool Blocking; string Object; ... }`;
  - `RacClusterInfo` (для `cluster info` — словарь свойств + типизированные частые поля:
    name, hostName, port, expirationTimeout, lifetimeLimit, maxMemorySize, maxMemoryTimeLimit,
    securityLevel, sessionIdleTimeout, sessionMaxMemorySize, sessionMaxTimeLimit).
  - Точный набор полей зафиксировать по реальному выводу rac на установленной платформе
    (реализатор запускает `rac cluster list`, `rac process list --cluster=...`,
    `rac session list --cluster=...`, `rac connection list --cluster=...`, `rac lock list --cluster=...`
    на своей машине) и дополнить образцы тестов.
- `Configuration Management/Services/RacOutputParser.cs` — ЧИСТЫЙ статический парсер (без I/O, без UI):
  - `ParseTable(string output)` → `IReadOnlyList<IReadOnlyList<string>>` (split по `\n`, затем по `\t`;
    пустые строки пропускать);
  - `ParseInfo(string output)` → `IReadOnlyDictionary<string, string>` (строки «ключ: значение»,
    разделитель — первый `": "`, ключ trim);
  - типизированные `ToClusters/ToProcesses/ToSessions/ToConnections/ToLocks(text)` —
    позиционный маппинг по индексам колонок (формат rac документирован на ИТС; порядок колонок
    фиксирован для версии, допускается расширение справа — парсер игнорирует лишние колонки);
  - устойчивость: значения с пробелами — норма (разделитель только `\t`); пустые значения — пустая строка;
    UUID парсятся в `Guid` (невалидные — пропуск/fallback); булевы поля — «0/1»;
    даты — `DateTime.TryParse` с инвариантной культурой (rac отдаёт в фиксированном формате).
  - При невозможности разобрать колонку по индексу (не совпадает число полей) — не падать, заполнять
    default и (для диагностики) не выбрасывать: строка пропускается с признаком.
- `Configuration Management/Services/IRacClient.cs` + `Configuration Management/Services/RacClient.cs`:
  - `IRacClient`: `Task<IReadOnlyList<RacCluster>> GetClustersAsync(RacConnectionParams p, CancellationToken)`,
    `GetClusterInfoAsync`, `GetProcessesAsync`, `GetSessionsAsync`, `GetConnectionsAsync`, `GetLocksAsync`,
    `TerminateSessionAsync`, `DisconnectConnectionAsync` (действия — API добавить на этапе 3, но сигнатуры
    заложить с этапа 1, чтобы не менять интерфейс дважды);
  - `RacConnectionParams { string Address; int Port; string User; string Password; }` — пароль только в памяти;
  - поиск rac: вынести логику `FindInBinDir`/`ResolveBinDirectory` из
    [`InfobaseAdminService.cs`](Configuration%20Management/Services/InfobaseAdminService.cs:216) в общий helper
    (например статический `OneCRacLocator.FindRacExecutable(Infobase? ib)` или расширение PlatformPaths);
    либо переиспользовать открытые методы — минимум дублирования;
  - выполнение команды: `Process` + `RedirectStandardOutput/Error`, `StandardOutputEncoding = UTF8`,
    таймаут (например 30 с, константа), параллельное чтение stdout/stderr, `CreateNoWindow = true`,
    `UseShellExecute = false`, `ArgumentList` (без shell, корректное экранирование кавычек);
    пароль НЕ пишется в лог (SensitiveDataMasker или просто не логировать аргументы целиком);
  - построение команд: `rac [--host=addr --port=N --user=U --password=P] <command> [--cluster=uuid] ...`;
    `cluster list` без `--cluster`; остальные с `--cluster=<uuid>`; завершение сеанса —
    `session terminate --cluster=<uuid> --session=<id>`; разрыв соединения —
    `connection disconnect --cluster=<uuid> --connection=<id>`.
- `Configuration Management/AppServices.cs` — регистрация в ОБЩЕМ блоке (обе платформы):
  `services.AddSingleton<IRacClient, RacClient>();` (около строки 60, рядом с IInfobaseAdminService).

**Тесты:** `ConfigurationManagement.Tests/RacOutputParserTests.cs`:
- `ParseTable`/`ParseInfo` на образцах вывода (включая русские имена, пробелы в значениях, пустые поля);
- типизированные парсеры: кластеры, процессы (rphost с памятью/потоками/CPU), сеансы (состояния, блокировки),
  соединения, блокировки, `cluster info`;
- устойчивость: строка с меньшим числом полей не валит парсер; кривой uuid → пропуск поля.
При желании — `RacClientTests` на сборке аргументов (без запуска процесса): экранирование, отсутствие
пароля в строке журнала. Без реального rac тесты парсера — на фиксированных строках.

**Изменяемые:** `AppServices.cs`, `csproj` (0.3.9.123), `CHANGELOG.md`, `README.md` (бейдж строка 3).
**Коммит:** `feat: встроенный монитор серверов 1С — сервисный слой rac (парсинг вывода, клиент); 0.3.9.123`.

### Этап 2 — 0.3.9.124: окно «Серверы 1С» и ViewModel (подключение + просмотр)

**Цель:** рабочее окно: панель подключения (адрес/порт/логин/пароль, кнопка «Подключиться»), выбор
кластера, вкладки «Рабочие процессы», «Сеансы», «Соединения», «Блокировки», «Информация о кластере»,
ручное обновление, статус-строка. Действия и автообновление — этап 3.

**Новые файлы:**
- `Configuration Management/ViewModels/ServerMonitorViewModel.cs` — чистая VM (образец —
  `ProcessInspectorViewModel`):
  - свойства подключения: `ServerAddress`, `ServerPort` (int, default 1540), `UserName`, `Password`
    (пароль в памяти; в тестах сеттер открытый, в UI — PasswordBox с ручной передачей значения);
  - `IReadOnlyList<RacCluster> Clusters`, `SelectedCluster` (Guid? SelectedClusterId), команда `ConnectCommand`
    (подключение: `GetClustersAsync`; при успехе — загрузка данных кластера), `RefreshCommand`;
  - вкладки-коллекции: `ObservableCollection<RacProcessRow> Processes`,
    `ObservableCollection<RacSessionRow> Sessions`, `ObservableCollection<RacConnectionRow> Connections`,
    `ObservableCollection<RacLockRow> Locks`, свойства `ClusterInfoText`/`ClusterInfo` (для «Информация о кластере»);
  - `StatusText`, `IsBusy` (флаг занятости через Interlocked, как в ProcessInspectorViewModel:80),
    `HasConnected`, `ErrorMessage` — статус-строка, ошибки не роняют окно;
  - `LoadClusterDataAsync(Guid clusterId, CancellationToken)` — параллельно/последовательно грузит
    процессы/сеансы/соединения/блокировки/инфо; применяет через `_dispatchToUi` (null — тесты);
  - DI: `IRacClient`, `IDialogService`, опционально `IProfileService` не нужен; `Action<Action>? dispatchToUi`.
- `Configuration Management/ViewModels/RacProcessRow.cs`, `RacSessionRow.cs`, `RacConnectionRow.cs`,
  `RacLockRow.cs`, `RacClusterRow.cs` — роу-модели с форматированием для отображения (форматы памяти
  «МБ», времени «HH:mm:ss», дат локально, состояния/цвета — как `ProcessRowViewModel`).
- `Configuration Management/Views/ServerMonitorWindow.xaml` + `ServerMonitorWindow.xaml.cs` (WPF):
  панель подключения (TextBox адрес, NumericUpDown/TextBox порт, TextBox логин, PasswordBox пароль,
  ModernButton «Подключиться», «Обновить», «Закрыть»), ComboBox кластеров, TabControl с вкладками,
  DataGrid на каждой вкладке (колонки по образцу `ProcessInspectorWindow.xaml`), статус-строка;
  стили — `{DynamicResource ...}`, implicit `Style TargetType="TextBlock"`; PasswordBox в WPF не биндится —
  код-бихайнд передаёт `PasswordBox.Password` в VM при подключении (образец — окна с паролями проекта).
- `Configuration Management/Views/ServerMonitorWindow.Avalonia.cs` (Linux): `ModalWindowBase`,
  ListBox + `FuncDataTemplate<...>` на каждой вкладке, `ThemeBrushes.Bind`, `BuildActionButton` —
  всё по образцу `ProcessInspectorWindow.Avalonia.cs`.

**Изменяемые:**
- `ViewModels/MainViewModel.Tools.cs` (WPF): команда `ServerMonitorCommand` → `ExecuteServerMonitor`
  (окно без аргументов или с `SelectedInfobase` для подсказки адреса; `ShowDialog()`), рядом с
  `ProcessInspectorCommand` (строки 2521–2543).
- `ViewModels/MainViewModel.Avalonia.Tools.cs` (Linux): то же, `ShowDialogSync(OwnerWindow())`.
- `Views/MainWindow.xaml` (WPF): в «Утилитах» (строка ~678) пункт «Серверы 1С…»
  (`ServerMonitor.Title`, команда `ServerMonitorCommand`, `InputGestureText="{Binding HotkeyServerConsole}"`)
  на месте «Консоли администрирования серверов»; ниже добавить «Консоль администрирования серверов
  (внешняя)» (`Admin.ServerConsole` → переименовать ключ/подпись на «внешнюю», команда `OpenServerConsoleCommand`
  без хоткея) — либо, по вкусу реализатора, оставить старый пункт ниже без хоткея. Решение зафиксировать в CHANGELOG.
- `Views/MainWindow.Avalonia.Tree.cs` (Linux): `BuildUtilitiesMenu` (строки 1652–1657) — те же два пункта
  (новый `ServerMonitor.Title` с хоткеем + внешняя консоль без хоткея).
- `Models/AppSettings.cs`: поля `RacServerAddress` (string, ""), `RacServerPort` (int, 1540),
  `RacUserName` (string, "") — пароль НЕ добавляем.
- `Localization/Languages/ru.json`, `en.json`: ключи `ServerMonitor.*` (Title, Address, Port, User, Password,
  Connect, Refresh, Close, Cluster, Tabs.*, Columns.*, Status.*, Empty*, Errors.*, Hint, ExternalConsole и т.д.).
- `AppServices.cs` — без изменений (этап 1); при необходимости зарегистрировать VM (Transient) — по образцу `SettingsViewModel`.

**Тесты:** `dotnet test` зелёный (новые VM-тесты — этап 3) + `dotnet build -p:BuildLinux=true`.
Возможен мини-тест: дефолты `ServerMonitorViewModel` (порт 1540, пустые строки, команды существуют).
**Коммит:** `feat: встроенный монитор серверов 1С — окно и ViewModel; 0.3.9.124`.

### Этап 3 — 0.3.9.125: действия и автообновление

**Цель:** завершение сеансов/разрыв соединений с подтверждением, автообновление по таймеру 5 с,
ручное обновление, корректные статусы и обработка ошибок.

- В `IRacClient`/`RacClient` реализовать (сигнатуры заложены на этапе 1):
  `Task<bool> TerminateSessionAsync(RacConnectionParams p, Guid clusterId, Guid sessionId, CancellationToken)`
  (`session terminate --cluster= --session=`) и
  `Task<bool> DisconnectConnectionAsync(RacConnectionParams p, Guid clusterId, Guid connectionId, CancellationToken)`
  (`connection disconnect --cluster= --connection=`); возврат успеха по ExitCode 0 + разбор stderr для сообщения об ошибке.
- В `ServerMonitorViewModel`:
  - `TerminateSessionCommand`/`DisconnectConnectionCommand` (по SelectedSession/SelectedConnection): подтверждение
    через `IDialogService.Confirm` (тексты локализации, предупреждение о потере несохранённых данных сеанса),
    выполнение, обновление списков, ошибка — `_dialogs.ShowWarning` + статус-строка (образец `KillSelected` из
    `ProcessInspectorViewModel.cs:107`);
  - автообновление: `AutoRefreshIntervalMs = 5000` (константа как в `ProcessInspectorViewModel.cs:20`),
    `System.Threading.Timer` создаётся при успешном подключении, `Dispose()` — при закрытии окна (`Closed` →
    `vm.Dispose()`); при автообновлении перечитываются данные выбранного кластера; флаг занятости
    (`Interlocked`) исключает наложение запросов и пропускает тик, если предыдущий ещё выполняется;
    автообновление НЕ перекрывает ручной «Обновить» (оба идут через один метод с тем же флагом);
  - при отсутствии подключения/пароля таймер не запускается; ошибки фонового опроса — в статус-строку,
    окно не ронять.
- Локализация: ключи подтверждений/ошибок действий (`ServerMonitor.TerminateConfirmFormat`,
  `ServerMonitor.DisconnectConfirmFormat`, `ServerMonitor.TerminateFailedFormat`,
  `ServerMonitor.DisconnectFailedFormat`, `ServerMonitor.AutoRefreshOff/On` — при необходимости).
- Окна: кнопки «Завершить сеанс»/«Разорвать соединение» на соответствующих вкладках
  (WPF — DataGrid кнопка/внешняя кнопка по SelectedItem; Avalonia — кнопка + ListBox SelectedItem);
  подсказка (hint) про автообновление — по образцу `ProcessInspectorWindow`.

**Тесты:** `ConfigurationManagement.Tests/ServerMonitorViewModelTests.cs`:
- команды действий: подтверждение (mock `IDialogService` с записью вызовов), вызов клиента с верными
  аргументами (fake `IRacClient`), обновление списков после действия, ошибка → статус/диалог;
- автообновление: таймер запускается после подключения и не стартует без него; флаг занятости не даёт
  наложиться запросам (fake клиент с задержкой);
- форматирование роу-строк (память, время, состояния) — чистые методы.
**Коммит:** `feat: встроенный монитор серверов 1С — завершение сеансов, разрыв соединений, автообновление; 0.3.9.125`.

### Этап 4 — 0.3.9.126: тесты, документация, финализация

**Цель:** закрыть цикл: полный прогон тестов/сборки, документация README, финализация локализации,
ручной чек-лист на обеих платформах.

- README.md: раздел «Серверы 1С (CTRL+ALT+S)» — заменить/дополнить строку 50 («Консоль администрирования
  серверов 1С (CTRL+ALT+S)»): описать встроенный монитор (подключение адрес:порт, просмотр кластеров/
  процессов/сеансов/соединений/блокировок, завершение сеансов, автообновление 5 с, rac-команды),
  отметить, что внешняя оснастка осталась пунктом «Консоль администрирования серверов (внешняя)».
- CHANGELOG.md: итоговая запись 0.3.9.126 (или дополнение записей этапов 1–3 — по факту коммитов;
  в каждом коммите этапа CHANGELOG уже обновлялся, здесь — выверить связность).
- Локализация: выверить все ключи `ServerMonitor.*` в ru.json/en.json (отсутствующие en-переводы —
  fallback на ru, но лучше заполнить).
- Проверки: `dotnet test` (427+новые все зелёные), `dotnet build -p:BuildLinux=true` без ошибок.
- Ручной чек-лист (Windows + Linux, светлая/тёмная тема, ru/en):
  подключение к реальному серверу 1С (localhost ragent:1540; при необходимости RAS:1545),
  кластеры/процессы/сеансы/соединения/блокировки, инфо кластера, завершение сеанса с подтверждением,
  разрыв соединения, автообновление (5 с), ручное обновление, ошибки подключения (неверный порт/пароль),
  закрытие окна без утечки таймера.
- Если в ходе ручной проверки найдены дефекты — они фиксируются отдельными коммитами в этой же версии
  (правило «один этап = одна версия = один коммит» допускает правки в пределах 0.3.9.126, т.к. релиза нет).
**Коммит:** `docs: встроенный монитор серверов 1С — README, финализация тестов; 0.3.9.126`.

---

## 4. Сообщения-инструкции для задач-исполнителей (передаются в new_task mode=code)

> Общая шапка для каждого сообщения: «Задача этапа N цикла 0.3.9.123–0.3.9.126 (встроенный монитор
> серверов 1С, rac). Репозиторий sivatorov/ConfigurationManagement, локальная копия
> f:\Yandex.Disk\h\Configuration_Management, ветка main. Перед началом: git pull (синхронизация с origin).
> Требования: обе платформы (Windows/WPF + Linux/Avalonia), версия в Configuration Management.csproj
> строки 62–65 → 0.3.9.XXX, запись в CHANGELOG.md сверху, бейдж README.md строка 3, dotnet test зелёный
> + dotnet build -p:BuildLinux=true, один коммит без пуша. План-файл: plans/PLAN-0.3.9.123-126.md (прочитать).
> В конце — создать следующую задачу через new_task(mode=code) с сообщением следующего этапа (ниже).»

### Задача 1 (0.3.9.123) — сервисный слой rac
Прочитать `plans/PLAN-0.3.9.123-126.md`, разделы 2 и 3 (Этап 1). Реализовать:
1. `Models/RacModels.cs` — POCO кластер/процесс/сеанс/соединение/блокировка/cluster-info (поля по плану).
2. `Services/RacOutputParser.cs` — чистый парсер `ParseTable`/`ParseInfo` + типизированные
   `ToClusters/ToProcesses/ToSessions/ToConnections/ToLocks` (позиционный маппинг по колонкам rac;
   образцы вывода зафиксировать в тестах; при возможности прогнать реальный rac на установленной платформе).
3. `Services/IRacClient.cs` + `Services/RacClient.cs` — поиск rac в bin платформы (вынести/переиспользовать
   логику `FindInBinDir`/`ResolveBinDirectory` из `Services/InfobaseAdminService.cs`), запуск команд rac
   (Process + RedirectStandardOutput/Error, UTF-8, таймаут 30 с, ArgumentList без shell), методы
   GetClusters/GetClusterInfo/GetProcesses/GetSessions/GetConnections/GetLocks + заготовки
   TerminateSession/DisconnectConnection (сигнатуры — сразу). Пароль — только в памяти, в лог не писать.
4. `AppServices.cs` — регистрация `IRacClient`→`RacClient` в общем блоке.
5. Тесты `ConfigurationManagement.Tests/RacOutputParserTests.cs` (парсер: таблицы, info, русские имена,
   пробелы, пустые поля, устойчивость к «кривым» строкам).
6. Версия 0.3.9.123, CHANGELOG, бейдж README, dotnet test, dotnet build -p:BuildLinux=true.
7. Коммит `feat: встроенный монитор серверов 1С — сервисный слой rac (парсинг вывода, клиент); 0.3.9.123`.
8. `new_task(mode=code)` со следующим сообщением (Задача 2).

### Задача 2 (0.3.9.124) — окно и ViewModel
Прочитать план (Этап 2). Реализовать: `ViewModels/ServerMonitorViewModel.cs` (+ роу-модели
`RacProcessRow/RacSessionRow/RacConnectionRow/RacLockRow/RacClusterRow`), окна
`Views/ServerMonitorWindow.xaml`/`.xaml.cs` (WPF) и `Views/ServerMonitorWindow.Avalonia.cs` (Linux) —
панель подключения (адрес/порт/логин/пароль), комбобокс кластеров, вкладки
«Процессы/Сеансы/Соединения/Блокировки/Инфо кластера», статус-строка; команды меню
`ServerMonitorCommand` в `MainViewModel.Tools.cs` (WPF) и `MainViewModel.Avalonia.Tools.cs` (Linux);
пункты «Утилит» в `Views/MainWindow.xaml` (~строка 678) и `Views/MainWindow.Avalonia.Tree.cs`
(~строки 1652–1657): новый «Серверы 1С…» с хоткеем HotkeyServerConsole + «Консоль администрирования
серверов (внешняя)» без хоткея; поля `RacServerAddress/RacServerPort/RacUserName` в `Models/AppSettings.cs`
(пароль НЕ сохранять); ключи `ServerMonitor.*` в ru.json/en.json; темизация (DynamicResource /
ThemeBrushes.Bind); версия 0.3.9.124, CHANGELOG, README-бейдж, dotnet test, build -p:BuildLinux=true;
коммит `feat: встроенный монитор серверов 1С — окно и ViewModel; 0.3.9.124`; затем `new_task(mode=code)` (Задача 3).

### Задача 3 (0.3.9.125) — действия и автообновление
Прочитать план (Этап 3). Реализовать: в `IRacClient/RacClient` — `TerminateSessionAsync`/`DisconnectConnectionAsync`;
в `ServerMonitorViewModel` — команды завершения сеанса/разрыва соединения с подтверждением
(`IDialogService.Confirm`) и обработкой ошибок, автообновление (таймер 5 с, константа
`AutoRefreshIntervalMs = 5000`, флаг занятости Interlocked, запуск после подключения, Dispose при закрытии),
ручное обновление, статус-строка; кнопки действий в обоих окнах; локализация подтверждений/ошибок;
тесты `ConfigurationManagement.Tests/ServerMonitorViewModelTests.cs` (fake IRacClient, mock IDialogService:
подтверждение, аргументы, обновление списков, таймер, флаг занятости); версия 0.3.9.125, CHANGELOG,
README-бейдж, dotnet test, build -p:BuildLinux=true; коммит
`feat: встроенный монитор серверов 1С — завершение сеансов, разрыв соединений, автообновление; 0.3.9.125`;
затем `new_task(mode=code)` (Задача 4).

### Задача 4 (0.3.9.126) — тесты и документация
Прочитать план (Этап 4). Реализовать: README-раздел «Серверы 1С (CTRL+ALT+S)» (заменить/дополнить строку 50,
бейдж строка 3), выверить CHANGELOG (запись 0.3.9.126), выверить ключи `ServerMonitor.*` в en.json
(добавить недостающие переводы), полный прогон `dotnet test` + `dotnet build -p:BuildLinux=true`,
ручной чек-лист по плану (реальный сервер 1С, обе платформы, светлая/тёмная, ru/en); при обнаружении
дефектов — исправить в этой же версии; версия 0.3.9.126; коммит
`docs: встроенный монитор серверов 1С — README, финализация тестов; 0.3.9.126`. Релиз/публикация не требуется.

---

## 5. Ключевые решения планирования (зафиксировать в CHANGELOG/README)

1. **Пароль администратора кластера НЕ сохраняется на диск.** Причины: rac требует открытый пароль
   (аргумент `--password`), поэтому необратимый PBKDF2-хэш (`PasswordHasher`, как у профилей) бесполезен;
   DPAPI/шифрование — только Windows, на Linux нет надёжного аналога без keyring. Пароль живёт только
   в памяти окна (PasswordBox), при каждом открытии окна вводится заново. Адрес/порт/логин сохраняются
   в `AppSettings` (обычные настройки интерфейса).
2. **Риск видимости пароля в командной строке процесса rac** (WMI `Win32_Process`/`/proc/<pid>/cmdline`
   доступны другим процессам пользователя) — принимаем осознанно (так работает rac; альтернатив нет —
   rac не читает пароль из stdin). Не логировать аргументы rac в журнал приложения.
3. **Порт подключения по умолчанию 1540** (агент сервера 1С `ragent`). Если на сервере запущен
   сервер администрирования RAS — указывать порт 1545. Подсказка в UI.
4. **Парсер вывода rac — позиционный** (порядок колонок list-команд фиксирован на ИТС и для версии
   платформы; лишние колонки справа игнорируются). Формат list — строки, поля через `\t`; info —
   «ключ: значение». JSON-вывода в rac нет — только текст.
5. **Автообновление 5 с** — только при успешно установленном подключении и открытом окне; флаг занятости
   исключает наложение запросов (паттерн `ProcessInspectorViewModel`).
6. **Внешняя оснастка остаётся** пунктом «Консоль администрирования серверов (внешняя)» в «Утилитах»
   (без хоткея); хоткей CTRL+ALT+S переходит встроенному монитору (поле `HotkeyServerConsole` переиспользуется).

---

## 6. Риски и зависимости

### Риски
| Риск | Влияние | Смягчение |
|------|---------|-----------|
| Порядок колонок rac меняется между версиями платформы | Неверные данные в таблицах | Позиционный парсер + игнор лишних колонок; образцы вывода в тестах; ручная проверка на реальном сервере |
| Кодировка вывода rac (Windows — OEM/ANSI?) | «Кракозябры» в русских именах | `StandardOutputEncoding = UTF8`; при необходимости `Console.OutputEncoding`/проверка на реальной платформе; тесты парсера на UTF-8-строках |
| Пароль виден в командной строке процесса rac | Раскрытие пароля соседним процессам | Не сохранять пароль; не логировать аргументы; документация в README |
| `lock list` может отсутствовать/меняться в старых версиях платформы | Ошибка вкладки «Блокировки» | Обработка ненулевого ExitCode как «не поддерживается» → статус-строка, вкладка пустая |
| Долгий запрос rac + таймер | Накопление очередей запросов | Флаг занятости (Interlocked), как в ProcessInspectorViewModel |
| Отсутствие rac в bin платформы | Невозможность подключения | Чёткая ошибка со статусом; поиск по новейшей установленной платформе (переиспользование ResolveBinDirectory) |
| Права администратора кластера | Отказ завершения сеанса/разрыва соединения | Явные сообщения об ошибке с текстом stderr rac |
| Дублирование логики поиска rac с InfobaseAdminService | Расхождение | Вынести `FindInBinDir`/`ResolveBinDirectory` в общий helper на этапе 1 |
| В origin уже опубликована 0.3.9.123 | Номер версии занят | `git pull` перед стартом; при совпадении — сдвинуть цикл на 0.3.9.124+ |

### Зависимости
- Этап 2 зависит от Этапа 1 (модели, парсер, клиент).
- Этап 3 зависит от Этапа 2 (окно, VM, команды действий добавляются в ту же VM).
- Этап 4 зависит от Этапов 1–3 (полный прогон).
- Порядок коммитов строго последовательный: каждый этап — отдельная версия и коммит.
- Тесты `dotnet test` и `dotnet build -p:BuildLinux=true` обязательны после каждого этапа
  (Linux-ветка компилируется всегда, иначе регресс Avalonia-окна обнаружится только на релизе).