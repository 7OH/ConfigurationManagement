# Результат сборки 0.3.9.308 (Задача 8, PLAN-0.3.9.307)

Дата сборки: 2026-10-05 (UTC+3, Europe/Moscow)
Коммиты main: `5a616d8` (0.3.9.308, #340) — HEAD; `fda021d` (0.3.9.307, portal.1c.ru)
Версия csproj: `0.3.9.308` (`Version`, `AssemblyVersion`, `FileVersion`, `InformationalVersion`)
Среда: Windows 11, .NET SDK 10.0.401 (>= 10.0.400), кросс-сборка Linux из Windows

## Артефакты

| Файл | Размер (байт) | SHA-256 |
|---|---|---|
| `Configuration Management/dist/win-x64/ConfigurationManagement.exe` | 84 048 366 | `E9D2A00B50AB5A526DFDB2834B5C50FA2588CBC88C8A4DF63F700E19F7D17C47` |
| `Configuration Management/dist/linux-x64/ConfigurationManagement` | 52 536 181 | `053623FA4A1D1DEAB7D6AF2296B41AD1D6C402AB32095453FE6772C5D28B1C9C` |
| `package/linux/deb/out/configuration-management_0.3.9.308_amd64.deb` | 45 309 750 | `AC65BBAD943E6C16BADE0405642F47FD15E27A9F2CBB339B11D0005EA28C1BD6` |

Размеры в MiB: Windows ≈ 80.1 MiB, Linux ≈ 50.1 MiB, .deb ≈ 43.2 MiB.

## Как собиралось

1. **Windows single-file** — `.\build-windows-single-file.ps1` (в каталоге `Configuration Management/`):
   `dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:PublishReadyToRun=false`.
   В `dist/win-x64/` оставлен только один файл `ConfigurationManagement.exe`.
2. **Linux single-file** — `.\build-linux-single-file.ps1` (кросс-сборка из Windows, ключ `-p:ForceLinux=true`, который включает `BuildLinux` в csproj → net10.0 + Avalonia):
   `dotnet publish -c Release -r linux-x64 --self-contained true -p:ForceLinux=true ...`.
   В `dist/linux-x64/` оставлен только один файл `ConfigurationManagement`.
3. **.deb** — `publish/build_deb_win_0.3.9.308.py` (копия образца 306; версия читается из `InformationalVersion` csproj = 0.3.9.308; бинарь из `dist/linux-x64/ConfigurationManagement`; ar-архив собирается на Windows без dpkg-deb, детерминированно).

## Проверки

### Версия в бинарниках
- Windows: `FileVersion = 0.3.9.308`, `ProductVersion = 0.3.9.308+5a616d8a4b983e09728b7886701bc073987f6ba5` (суффикс SHA коммита добавляется git/SourceLink; `VersionInfo.Display()` его отбрасывает), ProductName «Управление конфигурациями 1С».
- Поиск строки «0.3.9.308» в бинарниках: Windows — найдена (offset 83976836), Linux — найдена (offset 52480804).
- Поиск строки «0.3.9.307»: не найдена ни в одном бинарнике (сборка с актуальной версией).

### Запуск
- Windows: процесс `ConfigurationManagement` успешно запускается (PID запущен, жив через 5 с, затем остановлен принудительно).
- Linux: бинарник собран кросс-компиляцией и предназначен для запуска на Linux (на этой машине Linux-рантайма нет — проверка запуска невозможна, как и указано в скрипте).

### .deb (publish/check_deb_win_0.3.9.308.py)
- ar-members: `debian-binary`, `control.tar.gz`, `data.tar.gz`.
- `control Version: 0.3.9.308` — совпадает.
- data.tar.gz: `usr/bin/ConfigurationManagement` (mode 0o755), desktop-файл, иконка, copyright.
- `DEBIAN/md5sums`: 4 записи, совпадают с md5 содержимого data.tar.gz — структура корректна.

## Примечания
- Рабочее дерево содержало чужие untracked-файлы (в т.ч. в `publish/`) — они не изменялись и не коммитились.
- Скрипты `publish/build_deb_win_0.3.9.308.py`, `publish/check_deb_win_0.3.9.308.py` созданы по образцу 306 и НЕ коммитятся (по правилам Задачи 8).
- Коммит артефактов сборки (`dist/*`, `package/linux/deb/out/*`) не выполнялся — они в .gitignore.
- Релиз, комментарии к issues и закрытие issues — вне рамок этой задачи (Задачи 7/9).