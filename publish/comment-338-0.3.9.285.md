Исправлено в версии **0.3.9.285** (Windows/WPF и Linux/Avalonia).

**Что сделано:** список автодополнения поля «Конфигурация» в окне свойств базы теперь включает значения из колонки «Имя конфигурации» списка типовых конфигураций — даже если такой базы в списке ещё нет.

**Как исправлено:**

1. Общее правило объединения вынесено в [`ConnectionSettingsViewModel.MergeAvailableConfigurations`](Configuration%20Management/ViewModels/ConnectionSettingsViewModel.cs): берутся имена конфигураций всех баз списка + поле `ConfigName` типовых конфигураций из справочника ([`CustomConfigTypesStore.LoadAll()`](Configuration%20Management/Services/CustomConfigTypesStore.cs)); пустые значения отбрасываются, значения обрезаются (`Trim`), дубли убираются без учёта регистра, результат сортируется по алфавиту.
2. Обе платформы используют это правило:
   - Windows/WPF — [`GetAvailableConfigurations()`](Configuration%20Management/ViewModels/MainViewModel.Commands.cs);
   - Linux/Avalonia — [`AvailableConfigurations()`](Configuration%20Management/ViewModels/MainViewModel.Avalonia.cs).
3. Механизм выпадающего списка (`SetAvailableConfigurations`) не менялся — он по-прежнему принимает готовый объединённый список.

**Тесты.** [`ConnectionSettingsViewModelTests`](ConfigurationManagement.Tests/ConnectionSettingsViewModelTests.cs) дополнены: `MergeAvailableConfigurations_AddsConfigNamesFromTypes` (значения из типовых добавляются к базовым), `..._TrimsAndDropsEmptyFromBoth` (пустые/пробельные из обоих источников отбрасываются, Trim применяется), `..._Intersection_DeduplicatedCaseInsensitive` (пересечение значений баз и типовых не даёт дублей), `..._NullSources_ReturnsEmpty`, а также интеграция с `SetAvailableConfigurations`. Полный набор `dotnet test` зелёный (1585), кросс-сборка Linux (`dotnet build -p:BuildLinux=true`) без ошибок.

**Как проверить:**

1. Установите **0.3.9.285** ([релиз v0.3.9.285](https://github.com/sivatorov/ConfigurationManagement/releases/tag/v0.3.9.285)).
2. Откройте окно свойств любой базы (или «Регистрация существующей базы») — поле «Конфигурация».
3. В выпадающем списке должны присутствовать «Имена конфигураций» типовых (например «БухгалтерияПредприятия», «ЗУП» — по справочнику «Типовые конфигурации») наряду с именами из баз списка; дублей нет.