Исправлено в версии **0.3.9.278** (Windows/WPF и Linux/Avalonia).

**Что было.**

После фикса 0.3.9.271 первая строка списка скриптов становилась текущей и Enter работал, но пока не кликнешь по списку мышью, клавиатура на него «не реагировала» — стрелки не двигали выбор.

**Причина.**

`Focus()` на событии загрузки окна срабатывал до полной активации/первой отрисовки окна: WPF/Avalonia успевали отдать фокус другому элементу окна, и фактический клавиатурный фокус оставался вне списка (выделение первого пункта было, а фокус ввода — нет).

**Как исправлено.**

Перенос фокуса повторяется после полной отрисовки/активации окна и дополнительно фокусируется контейнер выбранной строки:

- **Windows/WPF** — [`Views/ScriptPickWindow.xaml.cs`](Configuration%20Management/Views/ScriptPickWindow.xaml.cs): `FocusScenarioList()` дополнительно ставит фокус через `Dispatcher.BeginInvoke(ApplicationIdle)` и фокусирует `ListBoxItem` первой строки (`ItemContainerGenerator.ContainerFromIndex`);
- **Linux/Avalonia** — [`Views/ScriptPickWindow.Avalonia.cs`](Configuration%20Management/Views/ScriptPickWindow.Avalonia.cs): `FocusList()` вызывает `_list.Focus(NavigationMethod.Keyboard)` и через `Dispatcher.UIThread.Post(Background)` фокусирует контейнер (`ContainerFromIndex`) выбранной строки.

**Тесты.**

Полный набор `dotnet test` зелёный, кросс-сборка Linux (`dotnet build -p:BuildLinux=true`) без ошибок. Поведение зависит от оконного стека и юнит-тестами не покрывается.

**Как проверить:**

1. Установите **0.3.9.278** ([релиз v0.3.9.278](https://github.com/sivatorov/ConfigurationManagement/releases/tag/v0.3.9.278)).
2. Вызовите «Выполнить скрипт» для базы (F5 при нескольких сценариях): окно выбора открывается, первая строка выделена.
3. Сразу, не трогая мышь, нажмите стрелки — выбор двигается; Enter выполняет выбранный скрипт.