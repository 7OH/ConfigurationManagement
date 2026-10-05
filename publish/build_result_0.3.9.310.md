# Результат сборки 0.3.9.310 (issue #323)

Дата сборки: 2026-10-05 (UTC+3, Europe/Moscow)
Версия csproj: `0.3.9.310` (`Version`, `AssemblyVersion`, `FileVersion`, `InformationalVersion`)
Среда: Windows 11, .NET SDK 10.0.401 (>= 10.0.400), кросс-сборка Linux из Windows

## Артефакты

| Файл | Размер (байт) | SHA-256 |
|---|---|---|
| `Configuration Management/dist/win-x64/ConfigurationManagement.exe` | 84 049 778 | `a01d8e1092648c5137ec9d475820fe9cbb43128f5600d30140a07d8873776367` |
| `Configuration Management/dist/linux-x64/ConfigurationManagement` | 52 537 845 | `62ca10cd59fb25bee7da0cba30e4bf097f259fde260e5afb0bda400eaabace23` |
| `package/linux/deb/out/configuration-management_0.3.9.310_amd64.deb` | 45 311 298 | `b0fd96216fa9b9beb0e205744a8b772aff35c6af02855fd5598d266f7e918c24` |

Размеры в MiB: Windows ≈ 80.1 MiB, Linux ≈ 50.1 MiB, .deb ≈ 43.2 MiB.
Копии и `SHA256SUMS.txt` — в `publish/out-0.3.9.310/`.

## Как собиралось

1. **Windows single-file** — `.\build-windows-single-file.ps1`: `dotnet publish -c Release -r win-x64
   --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
   -p:EnableCompressionInSingleFile=true -p:PublishReadyToRun=false`; в `dist/win-x64/` оставлен
   только `ConfigurationManagement.exe`.
2. **Linux single-file** — `.\build-linux-single-file.ps1` (кросс-сборка из Windows,
   `-p:ForceLinux=true` → net10.0 + Avalonia); в `dist/linux-x64/` оставлен только файл
   `ConfigurationManagement`.
3. **.deb** — `publish/build_deb_win_0.3.9.310.py` (копия образца 309; версия из
   `InformationalVersion` csproj = 0.3.9.310; бинарь из `dist/linux-x64/`; ar-архив на Windows,
   детерминированно).

## Проверки

### Версия в бинарниках
- Windows: `FileVersion = 0.3.9.310`, `ProductVersion = 0.3.9.310+323924801ad0d2d88ce89f87405836aa79f84146`
  (суффикс SHA коммита добавляется git/SourceLink; `VersionInfo.Display()` его отбрасывает),
  ProductName «Управление конфигурациями 1С».
- Magic: EXE = `MZ`, Linux = `7F-45-4C-46` (ELF).

### Запуск
- Windows: `--help` завершается кодом 0 (CLI-режим работает).

### .deb (publish/check_deb_win_0.3.9.310.py)
- ar-members: `debian-binary`, `control.tar.gz`, `data.tar.gz`.
- `control Version: 0.3.9.310` — совпадает.
- data.tar.gz: `usr/bin/ConfigurationManagement` (mode 0o755), desktop-файл, иконка, copyright.
- `DEBIAN/md5sums`: 4 записи, совпадают с md5 содержимого data.tar.gz — структура корректна.

## Тесты
- `dotnet test`: **1787 пройдено, 0 не пройдено** (включая новые тесты
  `OneCUpdatesLoginFlowTests` по сценариям issue #323, например «POST 200 → страница
  „Личные данные“ → Success», а также регрессия статусных цепочек #334/#330).
- Кросс-сборка Linux (`dotnet build -p:BuildLinux=true`): 0 ошибок; single-file Linux собран
  без ошибок (только предупреждения nullable, как и ранее).

## Примечания
- Скрипты `publish/build_deb_win_0.3.9.310.py`, `publish/check_deb_win_0.3.9.310.py` созданы
  по образцу 309 и НЕ коммитятся.
- Коммит артефактов сборки (`dist/*`, `package/linux/deb/out/*`) не выполнялся — они в .gitignore.
- Релиз и комментарий к #323 выполняются отдельной задачей (T7/T8).