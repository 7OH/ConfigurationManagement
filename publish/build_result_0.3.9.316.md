# Результат сборки 0.3.9.316 (issues #347, #348, #340, #323)

Дата: 2026-10-06 (UTC+3, Europe/Moscow)
Версия csproj: `0.3.9.316` (`Version`, `AssemblyVersion`, `FileVersion`, `InformationalVersion`)
Среда: Windows 11, .NET SDK 10 (net10.0-windows / net10.0), кросс-сборка Linux из Windows
План: `plans/PLAN-0.3.9.316.md`

## Состав изменений (кратко, полный перечень — в PLAN-0.3.9.316.md и CHANGELOG.md)

| Issue | Изменение |
|---|---|
| #347 «Сказ о trace.json» | Pretty-print `trace.json`; жёсткий гейт `CM_COLUMNS` (явный `false` выключает всегда, даже при env `CM_COLUMNS_TRACE=1`); конфиг перенесён в каталог активного профиля (рядом с settings), журнал — в `logs/trace_menuclose.json` с миграцией |
| #348 «Вставка адреса хранилища» | При вставке `tcp://dev:555/base` поле «Сервер» сохраняет протокол: `tcp://dev:555` (согласовано с `RepositorySettings.Server`) |
| #340 «Снятие выделения после контекстного меню» | 10-я итерация: расширена стабилизация (`MenuClosed` + `MenuClosedCursor(overTreeRow=True)` + недавний `MouseUp`), fallback по hit-test курсора; новые записи трассировки |
| #323 «Окно Проверка обновлений» | 8-я итерация: реализовано недостающее звено CAS `security_check`/cookie для `releases.1c.ru` по образцу рабочего кода 1С |

Тесты обновлены: `TraceFlagsTests`, `MenuCloseTraceFormatTests`, `ConnectionSettingsViewModelTests`, `BatchSelectionHelperTests`, `OneCUpdatesLoginFlowTests`, `TestEnvironment`.

## Проверки

### dotnet test (полный, Windows)
- Команда: `dotnet test "ConfigurationManagement.Tests/ConfigurationManagement.Tests.csproj" -c Debug`
- Результат: **Пройдено 1851, не пройдено 0, пропущено 0** (было 1823 в 0.3.9.315; +28).
- Примечание: первый прогон дал один флаки-сбой
  `ProcessInspectorSelectionTests.Refresh_DoesNotOverrideUserSelectionMadeSinceRestore`
  (`InvalidOperationException: Collection was modified…`, гонка модификации коллекции
  `Processes` из фонового потока опроса; тест относится к issue #342, не к кластеру
  0.3.9.316). Повторный полный прогон — зелёный (1851/0/0).

### Сборка Windows (WPF, net10.0-windows)
- Скрипт: `.\build-windows-single-file.ps1` (Release, RID win-x64, self-contained single-file)
- Результат: **Ошибок: 0** (предупреждения прежние, не связанные с кластером).

### Кросс-сборка Linux (Avalonia, net10.0)
- Скрипт: `.\build-linux-single-file.ps1` (`-p:ForceLinux=true` → net10.0 + Avalonia, RID linux-x64)
- Результат: **Ошибок: 0** — Avalonia-ветка компилируется со всеми исправлениями цикла.

## Артефакты

| Файл | Размер (байт) | SHA-256 |
|---|---|---|
| `Configuration Management/dist/win-x64/ConfigurationManagement.exe` | 84 063 242 | `6badca28c81f7b968c3bdab7d08b2a23300d037242d60c8d7ed9fb6e80782fd7` |
| `Configuration Management/dist/linux-x64/ConfigurationManagement` | 52 550 581 | `4b1e2374c4b651c597d0aee15f0a898ea8284a6eed36831b05f32e713ada8df3` |
| `package/linux/deb/out/configuration-management_0.3.9.316_amd64.deb` | 45 323 600 | `dce289c3ecb26635f75094b7cc99c592f6e017a536d6d3383e591eebc53c8d8c` |

Размеры в MiB: Windows ≈ 80.2 MiB, Linux ≈ 50.1 MiB, .deb ≈ 43.2 MiB.
Копии и `SHA256SUMS.txt` — в `publish/out-0.3.9.316/`:

| Файл в `publish/out-0.3.9.316/` | Описание |
|---|---|
| `ConfigurationManagement.exe` | Windows (WPF, single-file) |
| `ConfigurationManagement` | Linux (Avalonia, single-file) |
| `configuration-management_0.3.9.316_amd64.deb` | Debian/Ubuntu пакет |
| `SHA256SUMS.txt` | Контрольные суммы SHA-256 всех трёх артефактов (проверены — ALL_OK) |

## Как собиралось

1. **Windows single-file** — `.\build-windows-single-file.ps1`: `dotnet publish -c Release -r win-x64
   --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
   -p:EnableCompressionInSingleFile=true -p:PublishReadyToRun=false`; в `dist/win-x64/` оставлен
   только `ConfigurationManagement.exe` (80.2 MB).
2. **Linux single-file** — `.\build-linux-single-file.ps1` (кросс-сборка из Windows,
   `-p:ForceLinux=true` → net10.0 + Avalonia); в `dist/linux-x64/` оставлен только файл
   `ConfigurationManagement` (50.1 MB).
3. **.deb** — `publish/build_deb_win_0.3.9.316.py` (копия образца 315; версия читается из
   `InformationalVersion` csproj = 0.3.9.316; бинарь из `dist/linux-x64/`; ar-архив на Windows,
   детерминированно: uid=gid=0, mtime=0, gzip mtime=0). Проверка — `publish/check_deb_win_0.3.9.316.py`.
4. **SHA256SUMS.txt** — сформирован в `publish/out-0.3.9.316/` (hash + два пробела + имя файла,
   LF, три строки), сверен по содержимому скопированных артефактов — совпадение подтверждено.

## Проверки артефактов

### Версия в бинарниках и magic
- Windows: `FileVersion = 0.3.9.316`, `ProductVersion = 0.3.9.316+f6e69fefd8c2447ddc8be300f9477675cdd3b4b1`
  (суффикс SHA коммита добавляется git/SourceLink), ProductName «Управление конфигурациями 1С».
- Magic: EXE = `MZ` (4D 5A), Linux = `7F-45-4C-46` (ELF).

### Запуск (smoke)
- Windows: CLI-режим работает — `--list` печатает список баз (7 баз в текущем профиле) и
  завершается без зависания (процесс завершился в пределах таймаута 20 с).

### .deb (publish/check_deb_win_0.3.9.316.py)
- ar-members: `debian-binary`, `control.tar.gz`, `data.tar.gz`.
- `control Version: 0.3.9.316` — совпадает.
- data.tar.gz: `usr/bin/ConfigurationManagement` (mode 0o755), desktop-файл, иконка, copyright.
- `DEBIAN/md5sums`: 4 записи, совпадают с md5 содержимого data.tar.gz — структура корректна.

## Примечания

- Релиз НЕ создавался, коммит/публикация НЕ выполнялись, issues НЕ закрывались, комментарии НЕ
  публиковались — это Задача 8 цикла 0.3.9.316.
- Скрипты `publish/build_deb_win_0.3.9.316.py`, `publish/check_deb_win_0.3.9.316.py` созданы по
  образцу 315; артефакты (`dist/*`, `package/linux/deb/out/*`, `publish/out-0.3.9.316/*`) в git
  НЕ добавляются (в .gitignore).
- Единичный флаки-сбой теста `ProcessInspectorSelectionTests` (гонка коллекции из фонового
  потока, issue #342) зафиксирован на первом прогоне; повторный полный прогон — зелёный,
  итоговый счётчик: 1851 пройдено / 0 не пройдено / 0 пропущено.