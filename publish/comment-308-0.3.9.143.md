Исправлено в версии **0.3.9.143** (Windows/WPF и Linux/Avalonia) — выбор интерпретатора сценария (п.9) и развёрнутый ответ на вопрос о подстановках свойств базы (п.2).

**1. Новый параметр сценария — «Интерпретатор» (п.9).**
В редакторе сценария появилось поле «Интерпретатор» с вариантами:
- **Авто** (по умолчанию) — команда оборачивается по платформе: Windows — `cmd.exe /c …`, Linux — `/bin/sh -c …`. Это прежнее поведение, старые сценарии работают как раньше (их JSON-файлы мигрируют без ошибок);
- **cmd** — `cmd.exe /c …`;
- **PowerShell** — `powershell -NoProfile -Command …`;
- **sh** — `/bin/sh -c …`.

Теперь скрипт **не форсируется cmd**: если выбран PowerShell или sh, команда запускается именно указанным интерпретатором (PowerShell можно использовать и на Linux, если установлен `pwsh`; `cmd`/`sh` — там, где доступны). Превью командной строки (в редакторе и окне выбора перед запуском) показывает полную команду с обёрткой выбранного интерпретатора, например `powershell -NoProfile -Command "C:\tools\report.ps1" -Server %connection.server%`. Окно «Скрывать окно скрипта» и «Папка запуска» продолжают работать независимо от выбора шелла.

Файлы: модель [`Models/ScriptScenario.cs`](Configuration%20Management/Models/ScriptScenario.cs) + новый [`Models/ScriptShell.cs`](Configuration%20Management/Models/ScriptShell.cs), редактор Windows/WPF — [`Views/ScriptScenarioEditWindow.xaml`](Configuration%20Management/Views/ScriptScenarioEditWindow.xaml) и [`Views/ScriptScenarioEditWindow.xaml.cs`](Configuration%20Management/Views/ScriptScenarioEditWindow.xaml.cs), Linux/Avalonia — [`Views/ScriptScenarioEditWindow.Avalonia.cs`](Configuration%20Management/Views/ScriptScenarioEditWindow.Avalonia.cs), сборка обёртки и запуск — [`Services/ExternalCommandRunner.cs`](Configuration%20Management/Services/ExternalCommandRunner.cs) и [`ViewModels/MainViewModel.Scripts.cs`](Configuration%20Management/ViewModels/MainViewModel.Scripts.cs) / [`MainViewModel.Avalonia.Scripts.cs`](Configuration%20Management/ViewModels/MainViewModel.Avalonia.Scripts.cs), превью — [`Services/ScriptParameterResolver.cs`](Configuration%20Management/Services/ScriptParameterResolver.cs).

**2. Ответ на п.2: будут ли доступны все свойства базы в подстановках?**
Да. Помимо явных токенов (`%name%`, `%connection.server%`, `%connection.database%`, `%connection.filePath%`, `%connection.webUrl%`, `%connection.connectionString%`, `%connection.password%`/`%password%`, `%date%`/`%date:Формат%` и т.д.) работает **динамическая подстановка через рефлексию** (реализована в [`Services/ScriptParameterResolver.cs`](Configuration%20Management/Services/ScriptParameterResolver.cs), `BuildValueMap`): в карту значений автоматически попадают **все публичные скалярные свойства** моделей `Infobase` и `ConnectionSettings` — плоские ключи `%<имя>%` и с префиксом подключения `%connection.<имя>%` (в нижнем регистре). Это значит, что любые текущие и будущие свойства базы/подключения сразу доступны в подстановках — без обновления списка токенов в редакторе. Неизвестный токен по умолчанию остаётся в строке как есть (параметр `leaveUnknown=false` заменяет его пустой строкой). Подставляемые значения — инвариантная строка значения свойства (для дат/чисел/перечислений — их текстовое представление).

**Как проверить.**

1. Установите версию **0.3.9.143** (Windows или Linux).
2. «Утилиты» → «Настройка сценариев» → «Добавить»/«Изменить»: выберите «Интерпретатор» (например, PowerShell или sh), заполните путь и параметры, сохраните. Превью командной строки сразу покажет полную команду с обёрткой выбранного интерпретатора.
3. Запустите сценарий (F5) — скрипт выполнится выбранным интерпретатором (например, `powershell -NoProfile -Command` вместо `cmd`).
4. Проверьте подстановки: используйте в параметрах любой ключ из доступных свойств базы, например `%connection.authenticationMode%` или `%connection.useOsAuthentication%` — значение подставится автоматически.