# Результат сборки 0.3.9.309 (issue #346)

Дата сборки: 2026-10-05 (UTC+3, Europe/Moscow)
Версия csproj: `0.3.9.309` (`Version`, `AssemblyVersion`, `FileVersion`, `InformationalVersion`)
Среда: Windows 11, .NET SDK 10.0.401 (>= 10.0.400), кросс-сборка Linux из Windows

## Артефакты

| Файл | Размер (байт) | SHA-256 |
|---|---|---|
| `Configuration Management/dist/win-x64/ConfigurationManagement.exe` | 84 048 746 | `1314a2a98412cd617afeb56ad944d86e3fc980ab4c5109f9ffa56d0532f5170c` |
| `Configuration Management/dist/linux-x64/ConfigurationManagement` | 52 536 437 | `5a361c657d8cc6dbf6c8118a6c62558da665f5969129f84eb278643d1cf28482` |
| `package/linux/deb/out/configuration-management_0.3.9.309_amd64.deb` | 45 310 200 | `2bbf1e91630a657c230d82582acff1022ffff38a58296904f21c462bca3acf34` |

Размеры в MiB: Windows ≈ 80.1 MiB, Linux ≈ 50.1 MiB, .deb ≈ 43.2 MiB.
Копии и `SHA256SUMS.txt` — в `publish/out-0.3.9.309/`.

## Как собиралось

1. **Windows single-file** — `.\build-windows-single-file.ps1`: `dotnet publish -c Release -r win-x64
   --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
   -p:EnableCompressionInSingleFile=true -p:PublishReadyToRun=false`; в `dist/win-x64/` оставлен
   только `ConfigurationManagement.exe`.
2. **Linux single-file** — `.\build-linux-single-file.ps1` (кросс-сборка из Windows,
   `-p:ForceLinux=true` → net10.0 + Avalonia); в `dist/linux-x64/` оставлен только файл
   `ConfigurationManagement`.
3. **.deb** — `publish/build_deb_win_0.3.9.309.py` (копия образца 308; версия из
   `InformationalVersion` csproj = 0.3.9.309; бинарь из `dist/linux-x64/`; ar-архив на Windows,
   детерминированно).

## Проверки

### Версия в бинарниках
- Windows: `FileVersion = 0.3.9.309`, `ProductVersion = 0.3.9.309+a0a65336f17ecca0a0525ee9e48117ef4076c268`
  (суффикс SHA коммита добавляется git/SourceLink; `VersionInfo.Display()` его отбрасывает),
  ProductName «Управление конфигурациями 1С».
- Magic: EXE = `MZ`, Linux = `7F-45-4C-46` (ELF).

### Запуск
- Windows: `--help` завершается кодом 0 (CLI-режим работает).

### .deb (publish/check_deb_win_0.3.9.309.py)
- ar-members: `debian-binary`, `control.tar.gz`, `data.tar.gz`.
- `control Version: 0.3.9.309` — совпадает.
- data.tar.gz: `usr/bin/ConfigurationManagement` (mode 0o755), desktop-файл, иконка, copyright.
- `DEBIAN/md5sums`: 4 записи, совпадают с md5 содержимого data.tar.gz — структура корректна.

## Тесты
- `dotnet test`: **1780 пройдено, 0 не пройдено** (включая 7 новых тестов
  `ConfigTypeMatcherTests` по сценариям issue #346).
- Кросс-сборка Linux (`dotnet build -p:BuildLinux=true`): 0 ошибок.

## Примечания
- Скрипты `publish/build_deb_win_0.3.9.309.py`, `publish/check_deb_win_0.3.9.309.py` созданы
  по образцу 308 и НЕ коммитятся.
- Коммит артефактов сборки (`dist/*`, `package/linux/deb/out/*`) не выполнялся — они в .gitignore.
- Релиз и комментарий к #346 выполняются отдельной задачей (после согласования).