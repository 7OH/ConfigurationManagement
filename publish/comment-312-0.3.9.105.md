Исправлено в версии **0.3.9.105** (Windows/WPF и Linux/Avalonia).

**Что было сломано.** Нажатие Ctrl+K (командная палитра) падало с ошибкой —
окно палитры не открывалось. Отдельного пункта в меню у палитры не было,
поэтому о функции можно было забыть.

**Причина.** Окно палитры (Windows/WPF) ссылалось в XAML на именованный стиль
`ModernListBox`, которого в приложении не существует (темы задают только
неявный стиль `ListBox` и `ModernListBoxItem` для строк). При создании окна
`InitializeComponent()` бросал `XamlParseException` — палитра падала при каждом
открытии. Та же латентная ошибка была в окнах выбора базы
(`BaseSelectionWindow`) и пакетного обновления (`RepositoryBatchUpdateWindow`).

**Как исправлено.**

- Windows/WPF — [`Views/CommandPaletteWindow.xaml`](Configuration%20Management/Views/CommandPaletteWindow.xaml):
  ссылка на несуществующий `ModernListBox` убрана, применяется неявный стиль
  `ListBox` темы. Аналогично починены [`Views/BaseSelectionWindow.xaml`](Configuration%20Management/Views/BaseSelectionWindow.xaml)
  и [`Views/RepositoryBatchUpdateWindow.xaml`](Configuration%20Management/Views/RepositoryBatchUpdateWindow.xaml).
- Обе платформы — [`ViewModels/MainViewModel.Commands.cs`](Configuration%20Management/ViewModels/MainViewModel.Commands.cs)
  и [`ViewModels/MainViewModel.Avalonia.Commands.cs`](Configuration%20Management/ViewModels/MainViewModel.Avalonia.Commands.cs):
  `ExecuteShowCommandPalette` обёрнут в try-catch с логированием — ошибка
  показывается диалогом, а не роняет приложение. Пустые и null-источники
  элементов обрабатываются безопасно (`CommandPaletteViewModel.SetSource`).
- Добавлен пункт меню **«Командная палитра»** в «Утилиты» (первым пунктом)
  с подсказкой горячей клавиши Ctrl+K:
  Windows/WPF — [`Views/MainWindow.xaml`](Configuration%20Management/Views/MainWindow.xaml);
  Linux/Avalonia — [`Views/MainWindow.Avalonia.Tree.cs`](Configuration%20Management/Views/MainWindow.Avalonia.Tree.cs).
  Сама палитра в свой список команд не добавляется — рекурсии нет.
- Юнит-тесты дополнены: пустой/null-источник, замена источника, семантика
  Enter/Esc ([`CommandPaletteViewModelTests.cs`](ConfigurationManagement.Tests/CommandPaletteViewModelTests.cs)).

**Как проверить.**

1. Установите версию **0.3.9.105** (Windows или Linux).
2. Нажмите Ctrl+K — открывается командная палитра с поиском по базам и командам;
   Enter выполняет выбранный элемент, Shift+Enter запускает базу в Конфигураторе,
   Esc закрывает.
3. «Утилиты» → первым пунктом «Командная палитра» (Ctrl+K) — палитра открывается
   и из меню.
4. Меню «Настройки → Клавиши» — сочетание «Командная палитра» по-прежнему
   настраивается.