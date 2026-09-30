# Архитектура проекта «Управление конфигурациями 1С»

## 1. Целевая платформа и стратегия

**Приоритет — Windows (WPF).** Linux (Avalonia) — вторичная цель.

Проект собирается из одного файла [`Configuration Management.csproj`](Configuration Management/Configuration Management.csproj)
с условной компиляцией:

| Платформа | TFM | UI-фреймворк | Символ |
|-----------|-----|--------------|--------|
| Windows (основная) | `net10.0-windows` | WPF | `WINDOWS` (задаётся автоматически TFM) |
| Linux (вторичная) | `net10.0` | Avalonia | `LINUX` (`DefineConstants`) |

Код, специфичный для платформы, выносится в файлы-близнецы с суффиксами:
- Windows: базовое имя без суффикса (`MainWindow.xaml.cs`, `MainViewModel.cs`).
- Linux: суффикс `.Avalonia.cs` / `.Linux.cs` (`MainWindow.Avalonia.cs`, `OneCLauncher.Linux.cs`).

Включение/исключение файлов регулируется условными глобами в конце `.csproj`
(блок `ItemGroup Condition="!$([MSBuild]::IsOSPlatform('Windows'))"`).

## 2. Иерархия папок (целевая)

```
Configuration Management/
├── Program.cs                  # Точка входа (Windows #else / Linux #if LINUX)
├── App.xaml / App.xaml.cs      # Запуск, обработчики фатальных ошибок (Windows)
├── App.axaml / App.axaml.cs    # (Linux)
├── AppServices.cs              # DI-контейнер (Microsoft.Extensions.DependencyInjection)
├── Models/                     # Чистые .NET-модели (без зависимостей от UI)
│   ├── Group.cs, Infobase.cs, AppSettings.cs, ColorScheme.cs ...
├── Services/                   # Бизнес-логика и интеграция с 1С
│   ├── I*.cs                   # Интерфейсы
│   ├── *.cs                    # Реализации (Windows)
│   ├── *.Linux.cs              # Реализации (Linux)
├── ViewModels/                 # Логика представления (MVVM)
│   ├── MainViewModel.cs        # Каркас: поля, конструктор, коллекции, команды
│   ├── MainViewModel.Sync.cs   # Синхронизация ibases.v8i
│   ├── MainViewModel.Display.cs# Колонки, сессия, статус-бар, раскладка окна
│   ├── MainViewModel.Commands.cs # Реализации команд CRUD, избранное, закрепление
│   ├── MainViewModel.Launch.cs # Запуск 1С, сохранение, фильтр, язык
│   ├── MainViewModel.Theme.cs  # Темы, цветовые схемы, шрифты, дерево групп
│   ├── MainViewModel.Tools.cs  # Импорт/экспорт, кеш, COM, дамп, теги, перемещение
│   └── *.Avalonia.cs           # (Linux-аналоги)
├── Converters/                 # WPF-конвертеры
│   └── Avalonia/               # Avalonia-конвертеры
├── Themes/                     # WPF-темы (.xaml/.cs) и Avalonia (.axaml)
├── Localization/               # Локализация (LocalizationManager, Languages/)
├── Controls/                   # Пользовательские контролы (WPF)
└── Views/                      # Окна (view): *.xaml + code-behind + *.Avalonia.cs
    ├── MainWindow.*.cs         # Главное окно (каркас + partial-блоки)
    ├── SettingsWindow.*.cs     # Окно настроек (каркас + partial-блоки)
    └── *Window.xaml/.xaml.cs   # Прочие окна и модальные диалоги
```

Пространство имён корневое — `Configuration_Management` (см. `RootNamespace`),
логически разбито на `Models`, `Services`, `ViewModels`, `Converters`, `Themes`,
`Localization`.

## 3. Разделение ответственности

- **Models/** — данные и их сериализация; не зависят от UI.
- **Services/** — работа с 1С (запуск, COM-коннектор, кеш, синхронизация ibases,
  резервные копии, шаблоны) и инфраструктура (лог, диалоги, профили).
- **ViewModels/** — состояние и команды интерфейса (MVVM). Не ссылаются на конкретные
  окна, только на `IDialogService`, `IInfobaseRepository` и т.п.
- **Окна (*.xaml)** — тонкие «view»: только разметка и код, обслуживающий визуальное
  дерево (drag&drop, трей, хоткеи), без бизнес-логики.

### Импорт баз из кластера 1С (rac)

Функция «Утилиты → Операции → Импорт из кластера 1С…» (цикл 0.3.9.172–0.3.9.175) добавляет
информационные базы сервера 1С в список приложения. Связка чистых сервисов (без платформенных
зависимостей): [`IRacClient`](Configuration Management/Services/IRacClient.cs) →
[`RacOutputParser`](Configuration Management/Services/RacOutputParser.cs) →
[`RacInfobaseMapper`](Configuration Management/Services/RacInfobaseMapper.cs) →
[`ClusterImportViewModel`](Configuration Management/ViewModels/ClusterImportViewModel.cs) → окна.

- [`IRacClient`](Configuration Management/Services/IRacClient.cs) — запуск утилиты `rac`
  (`ArgumentList` без shell, таймаут, маскирование пароля
  [`SensitiveDataMasker.MaskRacPassword`](Configuration Management/Services/SensitiveDataMasker.cs));
  список баз — команда `infobase summary list --cluster=<uuid>` (плюс `cluster list`/`cluster info`).
- [`RacOutputParser`](Configuration Management/Services/RacOutputParser.cs) — позиционный парсер
  табличного вывода в модель `RacInfobaseSummary` (минимальный набор — 2 колонки, лишние справа
  игнорируются, строка заголовка и невалидный GUID пропускаются).
- [`RacInfobaseMapper`](Configuration Management/Services/RacInfobaseMapper.cs) — чистый маппинг в
  `Infobase`/`ConnectionSettings` (тип `ClientServer`): `Srvr` — хост из `cluster info` (`hostName`,
  корректен для удалённого RAS) с fallback на host-часть введённого адреса, порт — порт КЛАСТЕРА
  из `cluster list` (по умолчанию 1541, не порт ragent/RAS), `Ref` — имя ИБ в кластере,
  аутентификация `Prompt`; дедупликация `IsDuplicate` по строке подключения без учёта регистра
  (порт 1541 и его отсутствие — один ключ, одноимённые базы разных кластеров — разные).
- [`ClusterImportViewModel`](Configuration Management/ViewModels/ClusterImportViewModel.cs) — чистая
  вью-модель диалога: подключение (`GetClustersAsync` с автовыбором первого кластера), загрузка
  баз с кэшем `cluster info` по `clusterId`, чеклист `ClusterImportRow` (пометка дубликатов и
  файловых ИБ кластера), сводка, команды «Выделить все»/«Снять все»/«Импортировать»,
  `IsBusy`-guard от гонок.
- Окна — тонкие обёртки: WPF `Views/ClusterImportWindow.xaml` и Avalonia
  `Views/ClusterImportWindow.axaml` + `.Avalonia.cs`; результат — `SelectedBases`, добавление баз
  и создание недостающих групп выполняет `MainViewModel` (`ImportClusterInfobasesCommand`).

Ограничения (см. план цикла 0.3.9.172–0.3.9.175, п. 8/11):
- эквивалентность хостов `localhost` ↔ `127.0.0.1` при дедупликации не распознаётся
  (нормализуются только регистр и порт по умолчанию);
- файловые ИБ кластера (`dbms` пуст) в чеклисте помечаются «не импортируется» и в импорт не
  попадают (для них нужен `file-descriptor` из `infobase list` — вне цикла);
- пароль администратора кластера передаётся в VM из PasswordBox вручную и живёт только в памяти
  окна: в настройках хранятся лишь адрес/порт/логин, в журнал пароль не попадает.

### Регламентные задания кластера (rac)

Функция «Серверы 1С → вкладка Регламентные задания» (цикл 0.3.9.176–0.3.9.179) расширяет
встроенный монитор серверов: список регламентных заданий кластера с состояниями, расписанием,
временами и результатом последнего запуска, фильтром по базе-владельцу, действиями
«Приостановить/Возобновить» (с подтверждением) и окном «Детали задания». Связка чистых
сервисов та же, что у импорта: [`IRacClient`](Configuration Management/Services/IRacClient.cs) →
[`RacOutputParser`](Configuration Management/Services/RacOutputParser.cs) →
[`ServerMonitorViewModel`](Configuration Management/ViewModels/ServerMonitorViewModel.cs) → окна.

- [`IRacClient`](Configuration Management/Services/IRacClient.cs) — добавлены
  `GetJobsAsync` (`job list --cluster=<uuid>`) и `SetJobStateAsync`
  (`job pause/resume/disable/enable --cluster= --job=`, `bool` + `LastActionError`
  по образцу `session terminate`). Модель `RacJobInfo` (идентификатор, ИБ-владелец, имя, метод,
  признак предопределённого, cron-расписание, состояние, времена запусков, успех/ошибка и
  результат последнего запуска) и перечисление `RacJobAction` — в `Models/RacModels.cs`.
- [`RacOutputParser`](Configuration Management/Services/RacOutputParser.cs) — `ToJobs`:
  позиционный парсер с минимальным набором 3 колонок (заголовок и невалидный GUID пропускаются),
  у строковых полей снимаются обрамляющие кавычки (`schedule`/`result` могут содержать пробелы).
- [`RacJobRow`](Configuration Management/ViewModels/RacJobRow.cs) — форматирование строки вкладки:
  локализованное состояние с цветом, времена, обрезанный результат, доступность действий
  (`CanPause`/`CanResume`) и полный текст деталей `DetailsText`.
- [`ServerMonitorViewModel`](Configuration Management/ViewModels/ServerMonitorViewModel.cs) —
  коллекции `Jobs`/`FilteredJobs`, фильтр по базе через кэшированный `GetInfobasesAsync`
  (задания отдают GUID ИБ, имена подставляются из `infobase summary list`; сбой списка баз не
  роняет вкладку), команды `PauseJobCommand`/`ResumeJobCommand` с подтверждением и обработкой
  `LastActionError`; выбор и фильтр сохраняются при автообновлении (5 с).
- Окна — тонкие обёртки: вкладка в WPF `Views/ServerMonitorWindow.xaml` и Avalonia
  `Views/ServerMonitorWindow.Avalonia.cs` (фильтр-ComboBox, кнопки действий с энаблингом от
  состояния выбранной строки), детали — `Views/JobDetailsWindow.xaml` / `.Avalonia.cs`.

Ограничения (см. план цикла 0.3.9.176–0.3.9.179, п. 6/9):
- состав колонок `job list` может отличаться между версиями платформы — парсер позиционный
  (минимум 3 колонки), отсутствующие справа поля показываются «—»; на старых rac без поддержки
  `job`-команд пользователь видит понятную ошибку (stderr как есть);
- задания видны только администратору кластера: при пустом списке вкладка показывает подсказку
  про права;
- изменение расписания (`schedule set`), ручной запуск и история запусков глубже последнего —
  вне цикла.

### Массовая замена в строке подключения баз

Функция «Заменить в строках подключения…» (цикл 0.3.9.187–0.3.9.192) — массовое применение
правила замены «найти → заменить на» к полям строки подключения выбранных баз с обязательным
предпросмотром, подтверждением, одноуровневым откатом и JSON-бэкапом. Связка чистых слоёв
(без платформенных зависимостей) и тонких платформенных обёрток:

- [`Services/ConnectionReplaceModels.cs`](Configuration Management/Services/ConnectionReplaceModels.cs) —
  типы правила: `ConnectionField` (Server/Port/Ref/FilePath/WebUrl/Any), `ConnectionMatchMode`
  (Exact/Prefix/Substring/Regex) и запись `ConnectionStringReplaceRule` (Find/Replace/Field/Mode/IgnoreCase).
- [`Services/ConnectionStringEditor.cs`](Configuration Management/Services/ConnectionStringEditor.cs) —
  единый редактор: `Parse`/`Build` — тонкие обёртки над `ConnectionSettings.ParseConnectionString`/
  `ToConnectionString` (единая точка нормализации, без дублирования логики); `TryApply` —
  применение правила к одному полю **на копии** настроек с отдачей «было → станет»; `TryApplyRaw` —
  замена по всей строке как тексту (сегментная пересборка сохраняет кавычки, экранирование и
  прочие параметры Usr/Pwd/SchJobDn/disstt). Для Exact/Prefix/Substring искомый текст
  экранируется (`Regex.Escape`) — литеральное сопоставление; для Regex — паттерн .NET, невалидный
  выбрасывает `ArgumentException`; замена всегда литеральная.
- [`Services/ConnectionReplacementPlanner.cs`](Configuration Management/Services/ConnectionReplacementPlanner.cs) —
  планировщик: `SelectCandidates` — отбор кандидатов области (AllBases/BatchSelected/CurrentGroup)
  из **уже видимого** списка (планировщик про приватность не знает), `FilterVisibleInfobases` —
  единая фильтрация приватных база (использует мост в MainViewModel); `Plan` — построение плана
  **без мутаций** (правило применяется к копии настроек; базы без подключения — в счётчик
  `EmptyConnectionCount`); `Apply` — применение с глубокой копией всех полей прежних настроек
  (`ConnectionReplaceUndoEntry.Before`) для отката; `Undo` — восстановление снапшотов.
  План и применение используют одну функцию сопоставления (`TryApply`) — предпросмотр и результат
  не расходятся.
- [`ViewModels/ConnectionReplaceViewModel.cs`](Configuration Management/ViewModels/ConnectionReplaceViewModel.cs) —
  чистый VM окна (обе платформы, по образцу `ClusterImportViewModel`): поля Найти/Заменить,
  выбор поля/области/режима/регистра, коллекции `DisplayItem<T>` для ComboBox'ов, предпросмотр-
  коллекция `PreviewRows`, сводка, команды `RefreshPreviewCommand`/`ApplyCommand`/`UndoLastCommand`;
  колбэки `onApplied`/`onUndone` передают записи отката в MainViewModel (окна только привязываются).
- [`Views/ConnectionReplaceWindow.xaml`](Configuration Management/Views/ConnectionReplaceWindow.xaml)
  (WPF) и [`Views/ConnectionReplaceWindow.Avalonia.cs`](Configuration Management/Views/ConnectionReplaceWindow.Avalonia.cs)
  (Linux) — тонкие обёртки над VM: DataGrid/список «База | Поле | Было | Станет» с подсветкой
  изменённых строк (колонка «Станет» — зелёный фон WPF / Foreground Avalonia по `Changed`),
  подтверждение через `IDialogService`/`AvaloniaDialogService`, кнопка «Отменить последнюю замену».
- [`ViewModels/MainViewModel.ConnectionReplace.cs`](Configuration Management/ViewModels/MainViewModel.ConnectionReplace.cs)
  — общий мост обеих платформ: `GetConnectionReplaceCandidates` (видимые базы: приватные скрытого
  профиля исключаются через `FilterVisibleInfobases` + `IProfileService.CanShowPrivateBases`;
  выделенные — по Id мультивыделения; текущая группа — по полному пути выбранного узла дерева);
  `ApplyConnectionReplace` — одноуровневая undo-история (`_lastConnectionReplaceUndo`, повторное
  применение замещает предыдущее), JSON-бэкап списка `connection_replace_backup_<yyyyMMdd_HHmmss>.json`
  через `InfobaseJsonTransfer` в каталог данных приложения **ПЕРЕД** применением (ошибка записи не
  блокирует — логируется Warn), журнал (`IAppLogger.Info`) и системное уведомление (kind Success);
  `UndoLastConnectionReplace` — откат с той же персистентностью. Платформенный partial-хук
  `AfterConnectionReplaceCommitted`: Windows (`MainViewModel.ConnectionReplace.Windows.cs`) —
  `ScheduleSave`+`RebuildGroupTree`+`ExportToIbasesAfterLocalChange`; Linux (`*.Avalonia.cs`) —
  `SaveSilently`+`RebuildTree`+`ExportToIbasesAfterLocalChange`. Точки вызова — пункт «Заменить в
  строках подключения…» в блоке «Для выделенных (N)…» контекстного меню базы и в «Утилитах»
  (WPF `MainWindow.xaml`/`MainWindow.Events.cs`, Avalonia `MainWindow.Avalonia.Tree.cs`), пункт
  «Отменить последнюю замену строк подключения» (Enabled по `CanUndoConnectionReplace`).

Ограничения и ключевые решения (см. план цикла 0.3.9.187–0.3.9.192, п. 3/6/9):
- **структурное хранение подключений**: приложение хранит `ConnectionSettings` структурно,
  поэтому замена оперирует полями, а каноническая строка `ToConnectionString` используется как
  единый вид «было/станет» для предпросмотра и подтверждения; «сырые» тексты из ibases.v8i могут
  содержать неизвестные параметры — их сохраняет режим `Any` (сегментная замена значения в исходном
  тексте);
- **порт 1541 опускается в строке** (`GetServerWithPort`): замена поля Port — семантическая
  (числовой `ConnectionSettings.Port`), сборка сама добавляет/убирает «host:port»;
- **одноуровневый undo + JSON-бэкап**: история отката живёт в памяти сессии и покрывает только
  последнюю операцию; страховка вне сессии — автосоздаваемый JSON-файл всего списка перед применением;
- **приватные базы исключаются на уровне кандидатов**: мост строит кандидатов из видимого списка
  (`CanShowPrivateBases`) — окно никогда не получает скрытые приватные базы;
- **миграция между типами подключения НЕ выполняется**: функция меняет содержимое полей, а не тип
  подключения; если правило совпало в поле, не соответствующем типу базы (например FilePath у
  серверной базы), база не затрагивается;
- запущенные базы не блокируют операцию — изменение строки подключения не влияет на уже запущенные
  процессы 1С (информационный счётчик в сводке).

## 4. Выполненный рефакторинг

### Разбиение монолита `MainViewModel`
Было: один файл **5929 строк** (~180 методов) со смешанными обязанностями.

Стало: **частичный класс** `public partial class MainViewModel : ViewModelBase`,
разбитый на 7 файлов по функциональным блокам:

| Файл | Строк | Содержимое |
|------|-------|------------|
| `MainViewModel.cs` | 1059 | поля, конструктор, коллекции, версии платформы, настройки ibases, тип `TagFilterItem` |
| `MainViewModel.Sync.cs` | 235 | синхронизация с ibases.v8i (таймер, импорт/экспорт) |
| `MainViewModel.Display.cs` | 727 | колонки, теги-фильтры, сессия, статус-бар, раскладка окна, объявления команд |
| `MainViewModel.Commands.cs` | 858 | реализации команд: выбор, добавление, правка, удаление, избранное, закрепление, хоткеи |
| `MainViewModel.Launch.cs` | 492 | запуск 1С, сохранение списка, фильтр, смена языка |
| `MainViewModel.Theme.cs` | 545 | темы, цветовые схемы, шрифты, свёрнутые группы, `RebuildGroupTree` |
| `MainViewModel.Tools.cs` | 1491 | импорт/экспорт, кеш, конфигурация, COM-регистрация, дампы, теги, перемещение групп, поведение |

Содержимое методов сохранено без изменений (разбиение выполняется скриптом
[`tools/split_mainviewmodel.ps1`](tools/split_mainviewmodel.ps1) по границам методов),
поэтому поведение не изменилось. Сборка Windows (WPF) проверена: **0 ошибок**.

Это снижает риск конфликтов при параллельной разработке и облегчает поиск по коду.

### Реорганизация view-папки
Все окна (view) перенесены из корня проекта в подпапку [`Views/`](Configuration Management/Views):
`*.xaml`, WPF-код за разметкой (`*.xaml.cs`) и Avalonia-аналоги (`*.Avalonia.cs`),
а также базовый класс [`ModalWindowBase.cs`](Configuration Management/Views/ModalWindowBase.cs).
Пространства имён и `x:Class` не менялись, поэтому DI и XAML-привязки не пострадали.
Глобы в `.csproj` (секция Linux) согласованно обновлены (`Views\...`), сборка Windows проверена: **0 ошибок**.

### Разбиение окон на частичные классы
Тем же приёмом, что и `MainViewModel`, разбиты два крупнейших code-behind:

| Файл | Было | Стало |
|------|------|-------|
| [`MainWindow.xaml.cs`](Configuration Management/Views/MainWindow.xaml.cs) | 3058 строк | каркас (~435) + 9 partial-файлов: `.Tray`, `.Hotkeys`, `.Tree`, `.Columns`, `.Tags`, `.Language`, `.Scroll`, `.DragDrop`, `.Events` |
| [`SettingsWindow.xaml.cs`](Configuration Management/Views/SettingsWindow.xaml.cs) | 1971 строка | каркас + 8 partial-файлов: `.Profile`, `.Language`, `.Schemes`, `.Display`, `.Fonts`, `.Hotkeys`, `.Sync`, `.Platforms` |

Содержимое методов сохранено без изменений (только перемещение между файлами),
поэтому поведение не изменилось. Сборка Windows (WPF) проверена: **0 ошибок**.

### Выделение логики из интерфейса
Создана модель представления [`ViewModels/SettingsViewModel.cs`](Configuration Management/ViewModels/SettingsViewModel.cs)
(WINDOWS-only, зарегистрирована в DI), которая инкапсулирует состояние и бизнес-операции
вкладки «Цветовое оформление»: разрешение/валидацию/локализацию имён тем, рабочие копии
правок, персист изменённых тем и CRUD пользовательских схем, а также чистое преобразование
проверки дубликатов хоткеев. `SettingsWindow` делегирует в VM всю чистую бизнес-логику,
оставляя в view только работу с WPF-контролами и диалогами. Сборка Windows: **0 ошибок**.

VM углублена и для других вкладок настроек (без изменения XAML-привязок, поведение прежнее):
- **Синхронизация ibases.v8i** ([`SettingsWindow.Sync.cs`](Configuration Management/Views/SettingsWindow.Sync.cs)):
  рабочее состояние (`Sync` — вложенный класс `IbasesSyncSettings`: режим/путь/момент синхронизации)
  и его чистые преобразования — разрешение отображаемого пути (`ResolveDisplayPath`), построение
  локализованного статус-текста (`BuildStatusText`) и разбор интервала (`ParseInterval`, дефолт 30).
  В code-behind остаётся только чтение/запись значений контролов и диалог выбора файла.
- **Шрифт интерфейса** ([`SettingsWindow.Fonts.cs`](Configuration Management/Views/SettingsWindow.Fonts.cs)):
  рабочие копии настроек шрифтов областей (`ElementFonts`) с загрузкой (`LoadElementFontWorkingCopies`)
  и гарантированным созданием области (`EnsureElementFont`). Code-behind читает значения из контролов
  в модель и применяет их, но само хранение/подготовку рабочих копий ведёт VM.

Также создана модель представления [`ViewModels/ProfilesViewModel.cs`](Configuration Management/ViewModels/ProfilesViewModel.cs)
(WINDOWS-only, зарегистрирована в DI), выносящая из [`Views/ProfilesWindow.xaml.cs`](Configuration Management/Views/ProfilesWindow.xaml.cs)
всю бизнес-логику окна учётных записей: валидацию имени, построение списка профилей, выбор
текущей записи, CRUD через `IProfileService` (создание/переименование/смена пароля/удаление
с подтверждением через `IDialogService`) и локализацию подписи текущего профиля.
Окно стало тонкой «view»: оно лишь задаёт `DataContext`, связывает контролы через `{Binding}`
и передаёт пароль из `PasswordBox` в свойство `ProfilesViewModel.Password` (пароль не является
DependencyProperty и не поддерживает двустороннюю привязку). Кнопки используют команды
`CreateCommand`/`SaveCommand`/`DeleteCommand`, ошибки отображаются через `ErrorMessage`/`HasError`.
Сборка Windows: **0 ошибок**.

### Разбиение на блоки (Windows-приоритет)
- Из [`Services/ComReadHost.cs`](Configuration Management/Services/ComReadHost.cs) (~1335 строк,
  крупнейший Windows-монолит) выделены контрактные типы протокола — перечисление
  [`ComFailureKind`](Configuration Management/Services/ComReadHost.Types.cs) и результат
  [`ComReadResult`](Configuration Management/Services/ComReadHost.Types.cs) — в отдельный
  файл-блок [`ComReadHost.Types.cs`](Configuration Management/Services/ComReadHost.Types.cs).
  Тело самого хоста осталось на месте: это критичный и сильно связный код (жизненный цикл
  агента, протокол и диагностика переплетены, методы родителя вызывают методы агента),
  поэтому ручной разнос методов по partial-файлам здесь не выполнялся — это рекомендованный
  следующий шаг ниже.
- Консолидирован DI-контейнер [`AppServices.cs`](Configuration Management/AppServices.cs):
  общие регистрации сервисов вынесены за пределы `#if WINDOWS`/`#else`, внутри веток остались
  только платформозависимые. Это убирает дублирование и делает Windows-приоритет явным:
  Windows дополнительно регистрирует `IDialogService` (WPF), регистратор COM-коннектора
  и Windows-only ViewModel (`SettingsViewModel`, `ProfilesViewModel`); Linux — только
  `IDialogService` (Avalonia). Сборка Windows: **0 ошибок**.

### Разбиение `OneCLauncher` на частичные классы
Windows-сервис [`Services/OneCLauncher.cs`](Configuration Management/Services/OneCLauncher.cs)
(~1274 строк) разбит на частичный класс `public static partial class OneCLauncher`
по функциональным секциям. Содержимое методов сохранено дословно (только перемещение),
поведение и публичный API не изменились:

| Файл | Строк | Содержимое |
|------|-------|------------|
| `OneCLauncher.cs` | 575 | usings, перечисления `OneCLaunchMode`/`OneCClientType`/`OneCRunMode`/`OneCArchitecture`, поля `DefaultArchitecture`/`_activeBatchProcesses`, события `DesignerBatchStarted`/`Completed`, методы запуска (`Launch`, `GetRunModeFromLaunchMode`, `GetArchitecture`, `ResolveArchitecture`, `FindBestVersionDir`, `CompareVersionDirs`, `BuildArguments`, `LaunchWebClient`, `FindExecutable`) |
| `OneCLauncher.DesignerBatch.cs` | 386 | пакетные операции DESIGNER (`RunDesignerBatch`, `GetBaseConnectionToken`, `RegisterBatchProcess`, `CompleteDesignerBatch`, `ReadLogFile`, `TruncateLogTail`, `PruneDeadBatchProcesses`, `IsDesignerBlocked`, `IsConfiguratorRunningForBase`) и типы `DesignerBatchOperation`/`DesignerBatchInfo` |
| `OneCLauncher.Arguments.cs` | 347 | сборка аргументов и ссылок (`BuildConnectionArgument`, `BuildAuthArgument`, `ResolveThickClientExe`, `BuildEnterpriseShortcutArguments`, `LaunchByLink`, `ParsedLink`, `ParseLink`, `CreateInfoBase`) |

Новые partial-файлы добавлены в список исключений Linux-сборки в `.csproj`
(рядом с `OneCLauncher.cs`), поэтому Avalonia-сборка не затрагивается.
Сборка Windows (WPF): **0 ошибок**.

## 5. Рекомендуемые следующие шаги

1. **Разбить Avalonia-аналоги** тем же приёмом частичных классов:
   `MainViewModel.Avalonia.cs` (3282) и `MainWindow.Avalonia.cs` (3483) — как это сделано
   для WPF-версий `MainWindow` и `SettingsWindow`.
2. **`ComReadHost.cs` остаётся монолитом** (решение после анализа). Разделители «сторона
   родителя» / «сторона агента» — условные комментарии, а не чистые границы: поля,
   хелперы (`Encode`, `TryDecode`) и единая таблица `TokenMap` используются обеими сторонами,
   `Read` вызывает агентные помощники напрямую, поэтому разносить тело по partial-файлам
   небезопасно. При необходимости рефакторинга — только точечно и с обязательной проверкой
   сборки и сравнением набора методов.
3. **Продолжить MVVM-вынос** из окон: в `SettingsViewModel` уже перенесены синхронизация
   ibases.v8i и рабочие копии шрифтов; следующий кандидат — блок «Платформы» (`PlatformVersionService`
   сканирование и группировка версий), остающиеся поля которого пока завязаны на `PlatformsTree`
   и `Dispatcher`. Также рассмотреть отдельные VM для крупных диалогов (например, окно выбора
   групп / редактирования), если их логика явно бизнесовая и отвязывается от контролов.
4. **Выделить сервисы** из `MainViewModel` (например, `TagsFilterService`,
   `FavoritesHotkeyService`), чтобы ещё сильнее разгрузить VM.
5. **Проверить Linux-конфигурацию на Linux-хосте**: после переноса окон в `Views/` глобы
   `.csproj` обновлены согласованно, но сборка Avalonia возможна только на Linux (на Windows
   условие `IsOSPlatform('Windows')` включает WPF). Обязательно прогнать `dotnet build -c Debug`
   на Linux перед релизом.