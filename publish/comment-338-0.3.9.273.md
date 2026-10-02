Исправлено в версии **0.3.9.273** (Windows/WPF и Linux/Avalonia).

**Что было.**

В окне добавления/правки базы поле «Конфигурация» было обычным текстовым полем: наименование конфигурации приходилось вводить вручную целиком, даже если такая конфигурация уже указана у другой базы в списке.

**Как исправлено.**

Поле «Конфигурация» стало **редактируемым выпадающим списком** (по образцу поля «Сервер 1С»):

1. Варианты для автодополнения собираются из конфигураций, указанных у баз в списке: уникальные, без учёта регистра, отсортированные.
2. Поле остаётся свободным для ручного ввода: значение сохраняется из текста, а не из выбранного пункта — введённое вручную наименование не теряется (регресс issue #164 исключён).
3. Работает на обеих платформах — Windows/WPF и Linux/Avalonia.

Файлы:

- [`ViewModels/ConnectionSettingsViewModel.cs`](Configuration%20Management/ViewModels/ConnectionSettingsViewModel.cs:343) — `AvailableConfigurations` + `SetAvailableConfigurations(...)`: фильтр пустых, `Trim()`, дедупликация без учёта регистра, сортировка.
- [`Views/ConnectionSettingsWindow.xaml`](Configuration%20Management/Views/ConnectionSettingsWindow.xaml:1152) — TextBox «Конфигурация» заменён на редактируемый ComboBox (стиль `ModernComboBox`, `IsTextSearchEnabled=False` — как у сервера).
- [`Views/ConnectionSettingsWindow.Avalonia.cs`](Configuration%20Management/Views/ConnectionSettingsWindow.Avalonia.cs:1057) — аналогичная замена на редактируемый ComboBox.
- [`ViewModels/MainViewModel.Commands.cs`](Configuration%20Management/ViewModels/MainViewModel.Commands.cs:146) — `GetAvailableConfigurations()` и передача списка в окно (добавление и правка базы); зеркало [`ViewModels/MainViewModel.Avalonia.cs`](Configuration%20Management/ViewModels/MainViewModel.Avalonia.cs:1258).

**Тесты.** Полный набор `dotnet test` зелёный, кросс-сборка Linux (`dotnet build -p:BuildLinux=true`) без ошибок. Добавлены тесты `SetAvailableConfigurations`: дедупликация, сортировка, отсев пустых значений.

**Как проверить:**

1. Установите **0.3.9.273** ([релиз v0.3.9.273](https://github.com/sivatorov/ConfigurationManagement/releases/tag/v0.3.9.273)).
2. Откройте добавление/правку базы: в поле «Конфигурация» появится выпадающий список с вариантами из других баз (стрелка раскрытия).
3. Выберите вариант из списка или введите произвольное наименование вручную — сохраняется именно то, что введено.
4. Повторите на Linux (Avalonia).