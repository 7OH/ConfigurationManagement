# Управление конфигурациями 1С — v0.3.9.297 (отчёт о сборке)

Дата сборки: 2026-10-03. Версия в csproj: **0.3.9.297** (`Version`/`AssemblyVersion`/`FileVersion`/`InformationalVersion`).

Собрано по процессу прецедента 0.3.9.296 (задача 5 общего процесса): Windows/WPF — single-file
self-contained, Linux/Avalonia — single-file self-contained (кросс-сборка из Windows с
`-p:ForceLinux=true`), дополнительно — .deb-пакет (стандарт прошлых релизов).

## Артефакты (publish/out-0.3.9.297/)

| Платформа | Файл | Размер (байт) | SHA-256 |
|-----------|------|---------------|---------|
| Windows x64 (self-contained, один файл) | `ConfigurationManagement.exe` | 84 020 655 | `61D9170963FAF115BFF653339C74382EDC9E56B2013382D5B764C89D22611C68` |
| Linux x64 (self-contained, один файл)   | `ConfigurationManagement-linux-x64` | 52 509 109 | `F7242FB135CFD70500A818CECF88C12F93B37A093E21D534E7AFE4F662F5A1F5` |
| Linux x64 (deb-пакет)                   | `configuration-management_0.3.9.297_amd64.deb` | 45 281 960 | `94FB16FB4A1BEFCC16AEBCF0E101FA694386736BD0475B3DCD900AE3271CA1D1` |

Контрольные суммы продублированы в `publish/out-0.3.9.297/SHA256SUMS.txt`.

Linux: `chmod +x ConfigurationManagement-linux-x64 && ./ConfigurationManagement-linux-x64`
или установка пакета: `sudo dpkg -i configuration-management_0.3.9.297_amd64.deb`.

## Команды сборки

```powershell
# 1. Windows/WPF (net10.0-windows, win-x64)
powershell -NoProfile -ExecutionPolicy Bypass -File "Configuration Management\build-windows-single-file.ps1"
#    → Configuration Management/dist/win-x64/ConfigurationManagement.exe (80.1 MB)

# 2. Linux/Avalonia (net10.0, linux-x64, кросс-сборка с ForceLinux=true)
powershell -NoProfile -ExecutionPolicy Bypass -File "Configuration Management\build-linux-single-file.ps1"
#    → Configuration Management/dist/linux-x64/ConfigurationManagement (50.1 MB)

# 3. .deb-пакет (версия берётся из csproj автоматически)
python publish/build_deb_win_0.3.9.270.py
#    → package/linux/deb/out/configuration-management_0.3.9.297_amd64.deb

# 4. Копирование в каталог артефактов
#    ConfigurationManagement.exe, ConfigurationManagement-linux-x64,
#    configuration-management_0.3.9.297_amd64.deb → publish/out-0.3.9.297/
```

Флаги публикации (из скриптов и csproj): `--self-contained true`,
`-p:PublishSingleFile=true`, `-p:IncludeNativeLibrariesForSelfExtract=true`,
`-p:EnableCompressionInSingleFile=true`, `-p:PublishReadyToRun=false`; для Linux —
дополнительно `-p:ForceLinux=true`. Лишние файлы (.pdb, .json, Localization\Languages)
удаляются — в каталоге остаётся только один исполняемый файл.

## Проверки

- **Windows**: PE-magic `4D 5A` (MZ); `FileVersion = 0.3.9.297`,
  `ProductVersion = 0.3.9.297+2d28b11…`; smoke-тест запуска —
  `ConfigurationManagement.exe --help` завершается кодом `0`, GUI не открывается
  (CLI-вход обрабатывается headless; stdout у WinExe-приложения в перенаправленный поток
  не пишется — особенность GUI-подсистемы Windows, не дефект сборки).
- **Linux**: ELF-magic `7F 45 4C 46` (\x7FELF); контроль целостности размера
  (52 509 109 б, соответствует прецеденту 296 — 52 506 037 б).
- **.deb**: `publish/check_deb_win_0.3.9.297.py` — ar-члены `debian-binary/control.tar.gz/data.tar.gz`,
  `Version: 0.3.9.297` в control, состав data.tar.gz корректен
  (`usr/bin/ConfigurationManagement` mode `0o755` + desktop/icon/copyright),
  md5sums совпадают с содержимым data.tar.gz.

Ничего не коммитилось, issues не закрывались. Коммит и публикация релиза — следующая задача.