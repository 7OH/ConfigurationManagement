# Результат сборки 0.3.9.323 (issues #349, #324, #340, #323)

Дата: 2026-10-07 (UTC+3, Europe/Moscow)
Версия csproj: `0.3.9.323` (`Version`, `AssemblyVersion`, `FileVersion`, `InformationalVersion`) — проверено, не менялась
HEAD: `be12136` «0.3.9.319: вход portal.1c.ru — ticket CAS через Location POST, без голого security_check (#323)»
Рабочее дерево: исправления цикла 0.3.9.320–0.3.9.323 НЕ закоммичены (правки в csproj, сервисах и тестах)
Среда: Windows 11, .NET SDK 10.0.401 (net10.0-windows / net10.0), кросс-сборка Linux из Windows
План: `plans/PLAN-0.3.9.320-323.md`

## Состав изменений цикла (кратко, незакоммичено)

| Issue | Версия | Изменение |
|---|---|---|
| #349 → 0.3.9.320 | правка в рабочем дереве | Исправление кластера #349 |
| #324 → 0.3.9.321 | правка в рабочем дереве | Исправление кластера #324 |
| #340 → 0.3.9.322 | правка в рабочем дереве | Исправление кластера #340 |
| #323 → 0.3.9.323 | правка в рабочем дереве | Закрытие кластера #323 |

Все четыре исправления присутствуют в рабочем дереве и скомпилированы в обеих ветках (WPF и Avalonia); версия поднята до 0.3.9.323. Коммит не выполнялся — релиз будет следующей задачей.

## Сборка

### Windows (WPF, net10.0-windows)
- Скрипт: `.\build-windows-single-file.ps1` (Release, RID win-x64, self-contained single-file)
- Команда эквивалентна: `dotnet publish "Configuration Management\Configuration Management.csproj" -c Release -r win-x64`
- Результат: **Ошибок: 0** (предупреждения CS8625/CS8602 прежние, не связанные с циклом).

### Кросс-сборка Linux (Avalonia, net10.0)
- Скрипт: `.\build-linux-single-file.ps1` (`-p:ForceLinux=true` → net10.0 + Avalonia, RID linux-x64)
- Результат: **Ошибок: 0** — Avalonia-ветка компилируется со всеми исправлениями цикла (предупреждения CS8604/CS8602 прежние).

### .deb
- Скрипт: `publish/build_deb_win_0.3.9.323.py` (копия образца 319; версия читается из `InformationalVersion` csproj = 0.3.9.323; бинарь из `dist/linux-x64/`; ar-архив на Windows, детерминированно: uid=gid=0, mtime=0, gzip mtime=0).
- Проверка: `publish/check_deb_win_0.3.9.323.py` (адаптирован: путь DEB и `EXPECTED_VERSION` → 0.3.9.323). Результат: **OK** — ar-members корректны, `control Version: 0.3.9.323`, `usr/bin/ConfigurationManagement` mode `0o755`, 4 файла data.tar.gz совпадают с `DEBIAN/md5sums`.

## Артефакты

Исходные (в `dist/` и `package/linux/deb/out/`):

| Файл | Размер (байт) | SHA-256 |
|---|---|---|
| `Configuration Management/dist/win-x64/ConfigurationManagement.exe` | 84 071 006 | `4586861e6dcfa5327de58793fa29daefd721e2f5cc465e38222546c842f4e20a` |
| `Configuration Management/dist/linux-x64/ConfigurationManagement` | 52 557 109 | `9588791daa55aa97cb19bbd60c3241a8a4a312ebe21775b2f7f8e05300c7a822` |
| `package/linux/deb/out/configuration-management_0.3.9.323_amd64.deb` | 45 329 756 | `9e191b76ad2838baf5c38088ca3ac0c079d974fad6f5a5db83e99014f4ec6f4e` |

Размеры в MiB: Windows ≈ 80.2 MiB, Linux ≈ 50.1 MiB, .deb ≈ 43.2 MiB.

В папке `dist/win-x64` — ровно один файл (без .dll/.pdb/папок рядом); в `dist/linux-x64` — тоже один файл.

Копии для релиза:

| Каталог | Содержимое |
|---|---|
| `publish/out-0.3.9.323-windows/` | `ConfigurationManagement.exe`, `SHA256SUMS.txt` |
| `publish/out-0.3.9.323-linux/` | `ConfigurationManagement`, `configuration-management_0.3.9.323_amd64.deb`, `SHA256SUMS.txt` |

`SHA256SUMS.txt` — хэш + два пробела + имя, LF (формат sha256sum). Независимая пересчётная сверка содержимого копий со строками `SHA256SUMS.txt`: **ALL_OK** — все три записи совпадают.

## Проверки артефактов

### Версия в бинарниках и magic
- Windows: `FileVersion = 0.3.9.323`, `ProductVersion = 0.3.9.323+be121367452dd52c834a81acfd9d3a0ad20568ed`
  (суффикс SHA коммита добавляется git/SourceLink от HEAD).
- Magic: EXE = `4D 5A` (MZ), Linux = `7F 45 4C 46` (ELF).

### SHA256
- Независимый пересчёт по содержимому копий в `publish/out-0.3.9.323-windows/` и `publish/out-0.3.9.323-linux/`
  и сверка с `SHA256SUMS.txt`: **ALL_OK** — все три строки совпадают.

### Запуск (smoke)
- **Windows**: `ConfigurationManagement.exe --list` — завершился штатно, в stdout выведен список из 7 баз
  (Accounting, buh_empty, hrmcorp, IRONSKILLS, trade11_empty, ut11.6, Торговля 11), stderr пуст. Бинарь работает.
- **Linux**: запуск невозможен — WSL не установлен на машине (`wsl --status` → «Для системы Windows не
  установлены дистрибутивы для подсистемы Windows для Linux»). Верифицировано структурно: ELF-magic, размер,
  контрольная сумма, сборка с 0 ошибок, состав single-file (один файл).

### .deb (publish/check_deb_win_0.3.9.323.py)
- ar-members: `debian-binary`, `control.tar.gz`, `data.tar.gz`.
- `control Version: 0.3.9.323` — совпадает.
- data.tar.gz: `usr/bin/ConfigurationManagement` (mode 0o755), desktop-файл, иконка, copyright.
- `DEBIAN/md5sums`: 4 записи, совпадают с md5 содержимого data.tar.gz — структура корректна.

## Пропущено

- **AppImage** (`package/linux/appimage.sh`): **пропущен**. Требуется Linux/WSL; WSL на этой машине не
  установлен. AppImage не критичен: exe и linux single-file собраны, .deb собран.
- **Smoke-запуск Linux-бинарника**: пропущен по той же причине (нет Linux-рантайма/подсистемы).
- **Тесты (1897)**: не перезапускались в рамках этой задачи — по условию уже зелёные.

## Примечания

- Релиз НЕ создавался, git push НЕ выполнялся, артефакты/скрипты НЕ коммитились — это сборка цикла 0.3.9.323;
  релиз будет следующей задачей.
- Новые файлы цикла: `publish/build_deb_win_0.3.9.323.py`, `publish/check_deb_win_0.3.9.323.py`,
  `publish/build_result_0.3.9.323.md`; артефакты
  (`dist/*`, `package/linux/deb/out/*`, `publish/out-0.3.9.323-*`) в git НЕ добавляются (в .gitignore).