Исправлено в версии **0.3.9.275** (Windows/WPF и Linux/Avalonia).

**Что было.**

При большом количестве баз трудно быстро найти запущенные: индикатор «запущена/не запущена» у строк был, но отфильтровать список по запущенным нельзя было. А если база уже запущена, повторный запуск (двойной клик/Enter) открывал ещё один экземпляр.

**Как исправлено.**

1. **Новая кнопка-переключатель «Запущенные»** в панели списка баз (одноцветная точка, крупнее индикатора в списке). При включении в списке остаются только запущенные базы; флаги «запущена/нет» актуализируются сразу при включении режима, без ожидания таймера опроса.
2. **Двойной клик и Enter в режиме «Запущенные» активируют окно уже запущенной базы** — новый экземпляр не запускается (глобальная настройка двойного клика в этом режиме игнорируется):
   - **Windows**: реализована полная активация окна — находится процесс базы (по совпадению командной строки), среди его окон выбирается видимое главное, окно восстанавливается из свёрнутого состояния и активируется (`EnumWindows`/`SetForegroundWindow`, новый [`Services/OneCWindowActivator.Windows.cs`](Configuration%20Management/Services/OneCWindowActivator.Windows.cs));
   - **Linux**: честно отмечаю ограничение — активация чужого окна на X11 требует внешних утилит (`wmctrl`/`xdotool`), которые в программу не добавлялись, поэтому на Linux активация окна не реализована: двойной клик в режиме «Запущенные» работает как обычно.
3. **Настраиваемый хоткей** «Отбор запущенных»: Настройки → Горячие клавиши (`HotkeyShowRunning`); по умолчанию не назначен.

Файлы (основное):

- модель: [`Models/ListViewMode.cs`](Configuration%20Management/Models/ListViewMode.cs) — режим `Running`; [`Models/AppSettings.cs`](Configuration%20Management/Models/AppSettings.cs) — `ShowRunningOnly`, `HotkeyShowRunning`;
- фильтрация: WPF [`ViewModels/MainViewModel.Theme.cs`](Configuration%20Management/ViewModels/MainViewModel.Theme.cs:542), Avalonia [`ViewModels/MainViewModel.Avalonia.cs`](Configuration%20Management/ViewModels/MainViewModel.Avalonia.cs:1039);
- команда и хоткей: [`ViewModels/MainViewModel.Commands.cs`](Configuration%20Management/ViewModels/MainViewModel.Commands.cs:561), регистрация — [`Views/MainWindow.Hotkeys.cs`](Configuration%20Management/Views/MainWindow.Hotkeys.cs:67) / [`Views/MainWindow.Avalonia.Hotkeys.cs`](Configuration%20Management/Views/MainWindow.Avalonia.Hotkeys.cs:133), настройка — [`Views/SettingsWindow.Hotkeys.cs`](Configuration%20Management/Views/SettingsWindow.Hotkeys.cs:39) / [`Views/SettingsWindow.Avalonia.cs`](Configuration%20Management/Views/SettingsWindow.Avalonia.cs:2801);
- кнопка: WPF [`Views/MainWindow.xaml`](Configuration%20Management/Views/MainWindow.xaml:393), Linux [`Views/MainWindow.Avalonia.cs`](Configuration%20Management/Views/MainWindow.Avalonia.cs:1042);
- активация окна: WPF — [`Views/MainWindow.Events.cs`](Configuration%20Management/Views/MainWindow.Events.cs:445) + новый [`Services/OneCWindowActivator.Windows.cs`](Configuration%20Management/Services/OneCWindowActivator.Windows.cs); Linux — [`Controls/LeveledTreeView.Avalonia.cs`](Configuration%20Management/Controls/LeveledTreeView.Avalonia.cs:452);
- локализация ru/en (`Main.Running`, `Main.RunningTooltip`, `Settings.Hotkeys.ShowRunning`).

**Тесты.** Полный набор `dotnet test` зелёный, кросс-сборка Linux (`dotnet build -p:BuildLinux=true`) без ошибок. Добавлены кейсы: режим `Running` показывает только базы с `IsRunning=true`; двойной клик в режиме `Running` вызывает активацию окна, а не запуск. Нормализация хоткея покрыта существующими тестами горячих клавиш.

**Как проверить:**

1. Установите **0.3.9.275** ([релиз v0.3.9.275](https://github.com/sivatorov/ConfigurationManagement/releases/tag/v0.3.9.275)).
2. Запустите одну-две базы, включите кнопку «Запущенные» в панели списка — в списке остаются только запущенные базы.
3. Двойной клик (или Enter) по запущенной базе на Windows — активируется её окно, новый экземпляр не открывается.
4. Настройки → Горячие клавиши: назначьте хоткей «Отбор запущенных» и проверьте переключение с клавиатуры.