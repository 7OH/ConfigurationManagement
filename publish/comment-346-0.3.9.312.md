Исправлено в версии **0.3.9.312** (Windows/WPF и Linux/Avalonia).

**Что сделано по вашему предложению.**

Привязка базы к типовой конфигурации теперь видна и управляется прямо в свойствах базы. На вкладке
«Платформа», под группой «Конфигурация», добавлена группа **«Привязка»**:

1. **Сводка текущей связи.** Отображается, к какой типовой привязана база: наименование типовой,
   редакция (определяется по номеру релиза из свойств базы) и адрес каталога релизов. Если база
   не привязана — вместо сводки показывается «—».
2. **Кнопка «Связать…».** Открывает существующее окно «Связать с конфигурацией» для выбора или
   смены привязки. Поля связи пишутся в базу и репозиторий сразу, как при привязке из контекстного
   меню.
3. **Кнопка «Очистить».** Активна, только если у базы есть привязка. Сбрасывает код типовой
   конфигурации, ручную ссылку и персональный сегмент с подтверждением.

Реализация симметрична для WPF и Avalonia, все тексты локализованы ru/en.

Ключевые файлы:

- [`ConnectionSettingsViewModel.cs`](https://github.com/sivatorov/ConfigurationManagement/blob/main/Configuration%20Management/ViewModels/ConnectionSettingsViewModel.cs) —
  новые [`BuildLinkSummary`](https://github.com/sivatorov/ConfigurationManagement/blob/main/Configuration%20Management/ViewModels/ConnectionSettingsViewModel.cs),
  [`HasLink`](https://github.com/sivatorov/ConfigurationManagement/blob/main/Configuration%20Management/ViewModels/ConnectionSettingsViewModel.cs),
  [`ClearLink`](https://github.com/sivatorov/ConfigurationManagement/blob/main/Configuration%20Management/ViewModels/ConnectionSettingsViewModel.cs) и
  [`RefreshLinkState`](https://github.com/sivatorov/ConfigurationManagement/blob/main/Configuration%20Management/ViewModels/ConnectionSettingsViewModel.cs);
- [`ConnectionSettingsWindow.xaml`](https://github.com/sivatorov/ConfigurationManagement/blob/main/Configuration%20Management/Views/ConnectionSettingsWindow.xaml),
  [`ConnectionSettingsWindow.xaml.cs`](https://github.com/sivatorov/ConfigurationManagement/blob/main/Configuration%20Management/Views/ConnectionSettingsWindow.xaml.cs),
  [`ConnectionSettingsWindow.Avalonia.cs`](https://github.com/sivatorov/ConfigurationManagement/blob/main/Configuration%20Management/Views/ConnectionSettingsWindow.Avalonia.cs) —
  группа «Привязка» на вкладке «Платформа»;
- [`InfobaseLinkStorage.cs`](https://github.com/sivatorov/ConfigurationManagement/blob/main/Configuration%20Management/Services/InfobaseLinkStorage.cs) —
  общий helper сохранения связи (логика PersistLink окна связи без изменения его контракта);
- [`ConfigTypeMatcher.cs`](https://github.com/sivatorov/ConfigurationManagement/blob/main/Configuration%20Management/Services/ConfigTypeMatcher.cs) —
  новый чистый метод [`FindByCode`](https://github.com/sivatorov/ConfigurationManagement/blob/main/Configuration%20Management/Services/ConfigTypeMatcher.cs)
  (поиск типовой по коду связи, регистронезависимо).

**Как проверить.**

1. Установите **0.3.9.312** (Windows или Linux) и откройте свойства базы → вкладка «Платформа».
2. Если база привязана к типовой — в группе «Привязка» видна сводка (наименование типовой,
   редакция, адрес каталога) и активна кнопка «Очистить».
3. «Связать…» открывает окно «Связать с конфигурацией»: можно выбрать другую типовую — после
   сохранения новая привязка сразу отражается в сводке.
4. «Очистить» сбрасывает привязку (с подтверждением): сводка заменяется на «—», кнопка
   «Очистить» становится неактивной.
5. Проверьте обе платформы — WPF (Windows) и Avalonia (Linux) реализованы симметрично.

**Тесты.** 11 новых сценариев в
[`ConnectionSettingsViewModelTests.cs`](https://github.com/sivatorov/ConfigurationManagement/blob/main/ConfigurationManagement.Tests/ConnectionSettingsViewModelTests.cs)
(перенос полей привязки в LoadFrom/ApplyTo, BuildLinkSummary, HasLink/RefreshLinkState, очистка
связи) и
[`ConfigTypeMatcherTests.cs`](https://github.com/sivatorov/ConfigurationManagement/blob/main/ConfigurationManagement.Tests/ConfigTypeMatcherTests.cs)
(FindByCode: регистронезависимость, null-кейсы). Полный набор `dotnet test` зелёный (**1803**,
0 не пройдено); кросс-сборка Linux (`dotnet build -p:BuildLinux=true`) без ошибок.

Версия **0.3.9.312** входит в релиз v0.3.9.312:
[CHANGELOG](https://github.com/sivatorov/ConfigurationManagement/blob/main/CHANGELOG.md),
[релиз v0.3.9.312](https://github.com/sivatorov/ConfigurationManagement/releases/tag/v0.3.9.312).