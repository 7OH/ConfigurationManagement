Исправлено в версии **0.3.9.142** (Windows/WPF и Linux/Avalonia) — два замечания по «Сценариям» (часть 2).

**1. Новый параметр сценария — «Папка запуска» (WorkingDirectory).**
В редакторе сценария появилось поле «Папка запуска» (с кнопкой выбора каталога):
- заданный каталог передаётся процессу как рабочий (`ProcessStartInfo.WorkingDirectory`)
  при запуске скрипта (F5) — относительные пути внутри скрипта разрешаются от него;
- превью командной строки (в редакторе и окне выбора) показывает его как `cd "…" && …`;
- поле необязательно: если оно пустое, процесс наследует рабочий каталог приложения —
  старые сценарии работают как раньше, их JSON-файлы мигрируют без ошибок.

Файлы: модель
[`Models/ScriptScenario.cs`](Configuration%20Management/Models/ScriptScenario.cs),
редактор Windows/WPF — [`Views/ScriptScenarioEditWindow.xaml`](Configuration%20Management/Views/ScriptScenarioEditWindow.xaml)
и [`Views/ScriptScenarioEditWindow.xaml.cs`](Configuration%20Management/Views/ScriptScenarioEditWindow.xaml.cs),
Linux/Avalonia — [`Views/ScriptScenarioEditWindow.Avalonia.cs`](Configuration%20Management/Views/ScriptScenarioEditWindow.Avalonia.cs),
запуск — [`Services/ExternalCommandRunner.cs`](Configuration%20Management/Services/ExternalCommandRunner.cs)
и [`ViewModels/MainViewModel.Scripts.cs`](Configuration%20Management/ViewModels/MainViewModel.Scripts.cs) /
[`MainViewModel.Avalonia.Scripts.cs`](Configuration%20Management/ViewModels/MainViewModel.Avalonia.Scripts.cs),
превью — [`Services/ScriptParameterResolver.cs`](Configuration%20Management/Services/ScriptParameterResolver.cs).

**2. Кнопка «Изменить» в окне выбора сценария перед запуском.**
Когда при F5 сценариев несколько и открывается окно «Выбор скрипта», рядом с «Выполнить»
появилась кнопка «Изменить»: открывает редактор выбранного сценария, после сохранения
изменения сразу записываются (тот же сценарий, без копий) и список обновляется — можно
править параметры, «Папку запуска» и т.д., не выходя из окна запуска.
Файлы: Windows/WPF — [`Views/ScriptPickWindow.xaml`](Configuration%20Management/Views/ScriptPickWindow.xaml)
и [`Views/ScriptPickWindow.xaml.cs`](Configuration%20Management/Views/ScriptPickWindow.xaml.cs),
Linux/Avalonia — [`Views/ScriptPickWindow.Avalonia.cs`](Configuration%20Management/Views/ScriptPickWindow.Avalonia.cs).

**Как проверить.**

1. Установите версию **0.3.9.142** (Windows или Linux).
2. «Утилиты» → «Настройка сценариев» → «Добавить»/«Изменить»: заполните «Папку запуска»
   (кнопка «Обзор…» рядом с полем) и сохраните. Превью командной строки покажет
   `cd "…" && <команда>`. Запустите сценарий (F5) — скрипт стартует из указанного каталога
   (например, скрипт, читающий соседний файл по относительному пути, найдёт его).
3. При нескольких сценариях нажмите F5: в окне «Выбор скрипта» появилась кнопка «Изменить» —
   выберите сценарий, нажмите её, поменяйте параметры и «Сохранить»: список обновится,
   выделение сохранится, запуск выполнит изменённый сценарий.