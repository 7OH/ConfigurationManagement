# Результат сборки 0.3.9.312 (issue #346)

Дата сборки: 2026-10-05 (UTC+3, Europe/Moscow)
Версия csproj: `0.3.9.312` (`Version`, `AssemblyVersion`, `FileVersion`, `InformationalVersion`)
Среда: Windows 11, .NET SDK 10.0.401 (>= 10.0.400), кросс-сборка Linux из Windows

## Артефакты

| Файл | Размер (байт) | SHA-256 |
|---|---|---|
| `Configuration Management/dist/win-x64/ConfigurationManagement.exe` | 84 054 658 | `5963c250cb8e94555594d02b1b11e93aa305d0ddc67acdfc769a00e12fb14f7d` |
| `Configuration Management/dist/linux-x64/ConfigurationManagement` | 52 541 685 | `e3f50aa6fe1f3312cf352062d8905b17ca782394947eb332536b1cae86261f83` |
| `package/linux/deb/out/configuration-management_0.3.9.312_amd64.deb` | 45 315 078 | `628cdecf7dd292628be96f28d644599169f58de6d01b492f29afab1266a18a8a` |

Размеры в MiB: Windows ≈ 80.2 MiB, Linux ≈ 50.1 MiB, .deb ≈ 43.2 MiB.
Копии и `SHA256SUMS.txt` — в `publish/out-0.3.9.312/`.

## Как собиралось

1. **Windows single-file** — `.\build-windows-single-file.ps1`: `dotnet publish -c Release -r win-x64
   --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
   -p:EnableCompressionInSingleFile=true -p:PublishReadyToRun=false`; в `dist/win-x64/` оставлен
   только `ConfigurationManagement.exe`.
2. **Linux single-file** — `.\build-linux-single-file.ps1` (кросс-сборка из Windows,
   `-p:ForceLinux=true` → net10.0 + Avalonia); в `dist/linux-x64/` оставлен только файл
   `ConfigurationManagement`.
3. **.deb** — `publish/build_deb_win_0.3.9.312.py` (копия образца 311; версия из
   `InformationalVersion` csproj = 0.3.9.312; бинарь из `dist/linux-x64/`; ar-архив на Windows,
   детерминированно).

## Проверки

### Версия в бинарниках
- Windows: `FileVersion = 0.3.9.312`, `ProductVersion = 0.3.9.312+2f109d7ff922c0974a432d88f9bacc69b20eadae`
  (суффикс SHA коммита добавляется git/SourceLink; `VersionInfo.Display()` его отбрасывает),
  ProductName «Управление конфигурациями 1С».
- Magic: EXE = `MZ`, Linux = `7F-45-4C-46` (ELF).

### Запуск
- Windows: `--help` завершается кодом 0 (CLI-режим работает).

### .deb (publish/check_deb_win_0.3.9.312.py)
- ar-members: `debian-binary`, `control.tar.gz`, `data.tar.gz`.
- `control Version: 0.3.9.312` — совпадает.
- data.tar.gz: `usr/bin/ConfigurationManagement` (mode 0o755), desktop-файл, иконка, copyright.
- `DEBIAN/md5sums`: 4 записи, совпадают с md5 содержимого data.tar.gz — структура корректна.

## Тесты
- `dotnet test`: **1803 пройдено, 0 не пройдено** (включая 11 новых тестов по issue #346 —
  перенос полей привязки в LoadFrom/ApplyTo, BuildLinkSummary, HasLink/RefreshLinkState, очистка
  связи, FindByCode).
- Кросс-сборка Linux (`dotnet build -p:BuildLinux=true`): 0 ошибок; single-file Linux собран
  без ошибок (только предупреждения nullable, как и ранее).

## Примечания
- Скрипты `publish/build_deb_win_0.3.9.312.py`, `publish/check_deb_win_0.3.9.312.py` созданы
  по образцу 311 и НЕ коммитятся.
- Коммит артефактов сборки (`dist/*`, `package/linux/deb/out/*`) не выполнялся — они в .gitignore.
- Релиз и комментарий к #346 выполняются отдельной задачей (T13/T14).