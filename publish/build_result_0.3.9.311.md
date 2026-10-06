# Результат сборки 0.3.9.311 (issue #340)

Дата сборки: 2026-10-05 (UTC+3, Europe/Moscow)
Версия csproj: `0.3.9.311` (`Version`, `AssemblyVersion`, `FileVersion`, `InformationalVersion`)
Среда: Windows 11, .NET SDK 10.0.401 (>= 10.0.400), кросс-сборка Linux из Windows

## Артефакты

| Файл | Размер (байт) | SHA-256 |
|---|---|---|
| `Configuration Management/dist/win-x64/ConfigurationManagement.exe` | 84 051 982 | `0151a61de9cbf35bb98df2486b211bfc1f0f86d0af5fe1bbc95d006f5179c3da` |
| `Configuration Management/dist/linux-x64/ConfigurationManagement` | 52 539 189 | `d58e54fbeac56bbf330cb5905b06a62aa5ffcfde449f1289b28cf0a09c559b28` |
| `package/linux/deb/out/configuration-management_0.3.9.311_amd64.deb` | 45 312 752 | `013ac8ac4f302d8c8cde0726280bd44a54d5597dc0c0ebe51543910f73190c2d` |

Размеры в MiB: Windows ≈ 80.1 MiB, Linux ≈ 50.1 MiB, .deb ≈ 43.2 MiB.
Копии и `SHA256SUMS.txt` — в `publish/out-0.3.9.311/`.

## Как собиралось

1. **Windows single-file** — `.\build-windows-single-file.ps1`: `dotnet publish -c Release -r win-x64
   --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
   -p:EnableCompressionInSingleFile=true -p:PublishReadyToRun=false`; в `dist/win-x64/` оставлен
   только `ConfigurationManagement.exe`.
2. **Linux single-file** — `.\build-linux-single-file.ps1` (кросс-сборка из Windows,
   `-p:ForceLinux=true` → net10.0 + Avalonia); в `dist/linux-x64/` оставлен только файл
   `ConfigurationManagement`.
3. **.deb** — `publish/build_deb_win_0.3.9.311.py` (копия образца 310; версия из
   `InformationalVersion` csproj = 0.3.9.311; бинарь из `dist/linux-x64/`; ar-архив на Windows,
   детерминированно).

## Проверки

### Версия в бинарниках
- Windows: `FileVersion = 0.3.9.311`, `ProductVersion = 0.3.9.311+a32a30e77009c987c6dcae55124e482c9b57376f`
  (суффикс SHA коммита добавляется git/SourceLink; `VersionInfo.Display()` его отбрасывает),
  ProductName «Управление конфигурациями 1С».
- Magic: EXE = `MZ`, Linux = `7F-45-4C-46` (ELF).

### Запуск
- Windows: `--help` завершается кодом 0 (CLI-режим работает).

### .deb (publish/check_deb_win_0.3.9.311.py)
- ar-members: `debian-binary`, `control.tar.gz`, `data.tar.gz`.
- `control Version: 0.3.9.311` — совпадает.
- data.tar.gz: `usr/bin/ConfigurationManagement` (mode 0o755), desktop-файл, иконка, copyright.
- `DEBIAN/md5sums`: 4 записи, совпадают с md5 содержимого data.tar.gz — структура корректна.

## Тесты
- `dotnet test`: **1792 пройдено, 0 не пройдено** (включая 5 новых тестов по issue #340 —
  единый формат строки клика WPF/Avalonia, форматирование модификаторов, `target=null` при
  промахе, новый лимит усечения 1 МБ, валидная JSON-сериализация полей события клика).
- Кросс-сборка Linux (`dotnet build -p:BuildLinux=true`): 0 ошибок; single-file Linux собран
  без ошибок (только предупреждения nullable, как и ранее).

## Примечания
- Скрипты `publish/build_deb_win_0.3.9.311.py`, `publish/check_deb_win_0.3.9.311.py` созданы
  по образцу 310 и НЕ коммитятся.
- Коммит артефактов сборки (`dist/*`, `package/linux/deb/out/*`) не выполнялся — они в .gitignore.
- Релиз и комментарий к #340 выполняются отдельной задачей (T10/T11).