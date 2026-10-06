# Результат сборки 0.3.9.315 (Кластер C, issue #347 «Сказ о trace.json»)

Дата: 2026-10-06 (UTC+3, Europe/Moscow)
Версия csproj: `0.3.9.315` (`Version`, `AssemblyVersion`, `FileVersion`, `InformationalVersion`)
Среда: Windows 11, .NET SDK 10 (net10.0-windows / net10.0), кросс-сборка Linux из Windows
План: `plans/PLAN-0.3.9.315.md`; карточка: `publish/issue_347_full.md`

## Состав изменений

| Файл | Изменение |
|---|---|
| `Configuration Management/Services/TraceFlags.cs` | Новый статический сервис флагов: `EnsureExists()` (создание конфига, миграция JSONL-журнала), `IsEnabled()` (env-override + mtime-кэш, перечитывание без перезапуска), `IsMenuEnabled()` (CM_MENUCLOSE ∥ CM_MENUCLICK) |
| `Configuration Management/Services/TraceFlagsFormat.cs` | Чистый парсер/форматтер: `Serialize/SerializeDefaults/Parse/IsEnabled/ShouldMigrateLegacyJsonl/ResolveMenuCloseFileName`; константы `ConfigFileName=trace.json`, `MenuCloseLogFileName=trace_menuclose.jsonl`, `LegacyJsonlBackupFileName=trace_menuclose_legacy.json` |
| `Configuration Management/Services/MenuCloseTrace.cs` | Гейт по `CM_MENUCLOSE`/`CM_MENUCLICK` (no-op при выключенном флаге); журнал → `trace_menuclose.jsonl`; startup-запись под флагом; `EnsureStarted` всегда гарантирует наличие конфига `trace.json` |
| `Configuration Management/Services/MenuCloseTraceFormat.cs` | `PrimaryFileName = trace_menuclose.jsonl`; добавлены `ConfigFileName`, `LegacyJsonlBackupFileName`; `ResolveFileName` учитывает legacy 0.3.9.306 |
| `Configuration Management/Views/MainWindow.Columns.cs` | `CM_COLUMNS` через `TraceFlags.IsEnabled` (динамически); env `CM_COLUMNS_TRACE=1` — override включения |
| `Configuration Management/Services/OneCUpdatesService.cs` | INFO-диагностика редиректов/входа (`[Updates] Редирект…`, `Вход запущен…`, инвентаризация cookie, POST-диагностика) — под `CM_REDIRECT` через локальный хелпер `LogRedirectInfo`; WARN/ERROR пишутся всегда |
| `Configuration Management/App.xaml.cs`, `App.axaml.cs` | `TraceFlags.EnsureExists()` при старте (обе платформы) |
| `README.md` | Раздел про trace.json переписан: конфиг флагов, таблица флагов, инструкция включения; бейдж версии → 0.3.9.315 |
| `CHANGELOG.md` | Секция 0.3.9.315 (#347) |
| `Configuration Management/Configuration Management.csproj` | Версия → 0.3.9.315 (4 поля) |
| `ConfigurationManagement.Tests/TraceFlagsTests.cs` | Новый, +8 тестов (парсинг/дефолты/битый JSON/неизвестные флаги/сериализация/миграция/перечитывание по mtime) |
| `ConfigurationManagement.Tests/TestEnvironment.cs` | Новый: ModuleInitializer включает env `CM_REDIRECT=1` для тестового процесса (override включения, без файлового I/O) |
| `ConfigurationManagement.Tests/MenuCloseTraceFormatTests.cs` | Обновлён: +3 проверки имени журнала/конфига/резервной копии; legacy `menuclose_trace.json` продолжает дописываться |

## Проверки

### dotnet test (полный, Windows)
- Команда: `dotnet test ConfigurationManagement.Tests/ConfigurationManagement.Tests.csproj -c Debug`
- Результат: **Пройдено 1823, не пройдено 0, пропущено 0** (было 1813 в 0.3.9.314; +8 TraceFlagsTests, +2 нетто MenuCloseTraceFormatTests — одна проверка имени файла заменена тремя новыми).

### Сборка Windows (WPF, net10.0-windows)
- `dotnet build "Configuration Management/Configuration Management.csproj" -c Debug`
- Результат: **Ошибок: 0** (предупреждения прежние, не связанные с кластером).

### Кросс-сборка Linux (Avalonia, net10.0)
- `dotnet build "Configuration Management/Configuration Management.csproj" -c Debug -p:BuildLinux=true`
- Результат: **Ошибок: 0** — Avalonia-ветка компилируется с новым сервисом флагов.

## Артефакты

| Файл | Размер (байт) | SHA-256 |
|---|---|---|
| `Configuration Management/dist/win-x64/ConfigurationManagement.exe` | 84 059 618 | `b61ab6bb36bc716574ac7c3bf28c0860ebc3489914bf8beb5bd4b795b645118e` |
| `Configuration Management/dist/linux-x64/ConfigurationManagement` | 52 547 061 | `71cda4d055bf71fc6363c24a6c32fbe73ee543c45442d32967e69f8d99b9203d` |
| `package/linux/deb/out/configuration-management_0.3.9.315_amd64.deb` | 45 320 302 | `cf27535b0234b55c32fa320340f2a0e9b6f5db9088362e4be4e6331c9209ea6d` |

Размеры в MiB: Windows ≈ 80.2 MiB, Linux ≈ 50.1 MiB, .deb ≈ 43.2 MiB.
Копии и `SHA256SUMS.txt` — в `publish/out-0.3.9.315/`:

| Файл в `publish/out-0.3.9.315/` | Описание |
|---|---|
| `ConfigurationManagement.exe` | Windows (WPF, single-file) |
| `ConfigurationManagement` | Linux (Avalonia, single-file) |
| `configuration-management_0.3.9.315_amd64.deb` | Debian/Ubuntu пакет |
| `SHA256SUMS.txt` | Контрольные суммы SHA-256 всех трёх артефактов |

## Как собиралось

1. **Windows single-file** — `.\build-windows-single-file.ps1`: `dotnet publish -c Release -r win-x64
   --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
   -p:EnableCompressionInSingleFile=true -p:PublishReadyToRun=false`; в `dist/win-x64/` оставлен
   только `ConfigurationManagement.exe` (80.2 MB).
2. **Linux single-file** — `.\build-linux-single-file.ps1` (кросс-сборка из Windows,
   `-p:ForceLinux=true` → net10.0 + Avalonia); в `dist/linux-x64/` оставлен только файл
   `ConfigurationManagement` (50.1 MB).
3. **.deb** — `publish/build_deb_win_0.3.9.315.py` (копия образца 312; версия из
   `InformationalVersion` csproj = 0.3.9.315; бинарь из `dist/linux-x64/`; ar-архив на Windows,
   детерминированно: uid=gid=0, mtime=0, gzip mtime=0). Проверка — `publish/check_deb_win_0.3.9.315.py`.

## Проверки артефактов

### Версия в бинарниках и magic
- Windows: `FileVersion = 0.3.9.315`, `ProductVersion = 0.3.9.315+2bd4b13efb0b17963c554c670118a98a96584795`
  (суффикс SHA коммита добавляется git/SourceLink), ProductName «Управление конфигурациями 1С».
- Magic: EXE = `MZ` (4D 5A), Linux = `7F-45-4C-46` (ELF).

### Запуск (smoke)
- Windows: CLI-режим работает — `--list` печатает список баз (7 баз в текущем профиле) и
  завершается кодом 0.
- `--help` парсером `CliArgs.Parse` в текущем коде НЕ распознаётся (значение `Help` объявлено
  в `CliCommandKind`, но в `Parse` ветка не подключена — см. комментарий к `Unsupported` в
  `CliCommands.cs`), поэтому процесс переходит к обычному запуску GUI-окна и не завершается
  сам. Это поведение кода, не связанное с кластером C; в задаче сборки код не изменялся,
  проверка CLI выполнена через `--list`.

### .deb (publish/check_deb_win_0.3.9.315.py)
- ar-members: `debian-binary`, `control.tar.gz`, `data.tar.gz`.
- `control Version: 0.3.9.315` — совпадает.
- data.tar.gz: `usr/bin/ConfigurationManagement` (mode 0o755), desktop-файл, иконка, copyright.
- `DEBIAN/md5sums`: 4 записи, совпадают с md5 содержимого data.tar.gz — структура корректна.

## Примечания

- Релиз НЕ создавался, коммит/публикация НЕ выполнялись, issues НЕ закрывались, комментарий НЕ публиковался (черновик — `publish/comment-347-0.3.9.315.md`).
- Скрипты `publish/build_deb_win_0.3.9.315.py`, `publish/check_deb_win_0.3.9.315.py` созданы по
  образцу 312; артефакты (`dist/*`, `package/linux/deb/out/*`, `publish/out-0.3.9.315/*`) в git
  НЕ добавляются (в .gitignore).
- Поведение оконного стека (попапы меню, виртуализация) юнит-тестами не покрывается — для диагностики #340 пользователь включает `CM_MENUCLOSE=true` по инструкции из черновика комментария.
- Старый JSONL-журнал `trace.json` (0.3.9.308–0.3.9.314) при первом старте новой версии переименовывается в `trace_menuclose_legacy.json`; legacy `menuclose_trace.json` (0.3.9.306) не трогается.