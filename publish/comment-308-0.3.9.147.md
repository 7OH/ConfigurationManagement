Исправлено в версии **0.3.9.147** (Windows/WPF и Linux/Avalonia) — два замечания: «окно выполнения скрипта не появляется» (14:43) и «лексема `&&` недопустима в PowerShell» (15:05).

**1. Снятая галка «Скрывать окно» теперь действительно показывает консольное окно.**
Причиной была инверсия флага при запуске: `createNoWindow` получал `!HideWindow`, т.е. снятая галка (`HideWindow=false`) скрывала окно, а установленная — наоборот показывала. Флаг исправлен на прямой `scenario.HideWindow` в обеих платформенных ветках запуска:
- Windows/WPF — [`ViewModels/MainViewModel.Scripts.cs`](Configuration%20Management/ViewModels/MainViewModel.Scripts.cs) (`RunScriptAsync`);
- Linux/Avalonia — [`ViewModels/MainViewModel.Avalonia.Scripts.cs`](Configuration%20Management/ViewModels/MainViewModel.Avalonia.Scripts.cs).

**2. Командная строка для PowerShell собирается с разделителем `;` вместо `&&`.**
`powershell.exe` 5.1 не поддерживает лексему `&&` («не является допустимым разделителем операторов»). Теперь разделитель между префиксом `cd "…"` и командой зависит от выбранного интерпретатора ([`Services/ScriptParameterResolver.cs`](Configuration%20Management/Services/ScriptParameterResolver.cs), `BuildCommandLine`/`BuildShellCommandLine`):
- **PowerShell** → `cd "…"; …` (например, `powershell -NoProfile -Command cd "C:\Tools"; report.ps1`);
- **cmd / sh** (включая «Авто» на обеих платформах) → как раньше, `cd "…" && …`.

Правка автоматически распространилась на все места сборки строки: превью в редакторе сценария и окне выбора ([`ViewModels/ScriptScenarioEditViewModel.cs`](Configuration%20Management/ViewModels/ScriptScenarioEditViewModel.cs), `BuildExampleCommandLine`), лог запуска и историю команд в `MainViewModel.Scripts.cs` / `MainViewModel.Avalonia.Scripts.cs`.

Тесты дополнены и полностью зелёные (`dotnet test`: 670/670):
- [`ScriptParameterResolverTests.cs`](ConfigurationManagement.Tests/ScriptParameterResolverTests.cs) — `BuildCommandLine` с рабочей папкой: PowerShell → `;`, Cmd/Sh/Auto (Windows и Linux) → `&&`; без рабочей папки разделитель не появляется; полная обёртка `powershell -NoProfile -Command cd … ; …` и `cmd.exe /c cd … && …` для «Авто» на Windows;
- [`ExternalCommandRunnerTests.cs`](ConfigurationManagement.Tests/ExternalCommandRunnerTests.cs) — `CreateProcessStartInfo.CreateNoWindow` соответствует параметру `createNoWindow` (отсутствие инверсии; `RunDetached` строит StartInfo тем же путём).

**Как проверить (Windows).**
1. «Утилиты» → «Настройка сценариев» → сценарий, **снимите** галку «Скрывать окно» → F5 → появляется консольное окно cmd/PowerShell с выполняющимся скриптом.
2. В том же сценарии задайте «Папку запуска» и интерпретатор **PowerShell** → в логе/истории команда вида `powershell -NoProfile -Command cd "…"; …` — без `&&`.
3. Скопируйте строку из лога в `powershell.exe` вручную — ошибки про `&&` нет.
4. Интерпретатор «Авто» на Windows — по-прежнему `cmd.exe /c … && …`.