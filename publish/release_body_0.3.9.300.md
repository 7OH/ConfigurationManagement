# Управление конфигурациями 1С — v0.3.9.300

Дата сборки: 2026-10-03. Версия в csproj: **0.3.9.300** (`Version`/`AssemblyVersion`/`FileVersion`/`InformationalVersion`).

## Исправлено

- **Серверы 1С: парсер rac «cluster list» ([#324](https://github.com/sivatorov/ConfigurationManagement/issues/324))**: кластеры больше не теряются на новых версиях rac, когда вывод команды `cluster list` приходит не таблицей, а блоками «ключ : значение» с выравниванием пробелами и двоеточием: `ToClusters` при пустом табличном разборе переключается на разбор блоков по повторяющемуся ключу `cluster` (GUID) с извлечением `name`/`port`/`host`, значения снимаются с кавычек; единый helper `TryParseKeyValueBlocks` устойчив к обеим версиям rac ([`RacOutputParser.cs`](Configuration%20Management/Services/RacOutputParser.cs), [`RacOutputParserTests.cs`](ConfigurationManagement.Tests/RacOutputParserTests.cs)).

- **Инспектор процессов ([#342](https://github.com/sivatorov/ConfigurationManagement/issues/342))**: выделение строки сохраняется при автообновлении списка (раз в 5 секунд): перед перезаполнением запоминается PID выбранного процесса, после — строка с тем же PID восстанавливается (сравнение по идентификатору, а не по ссылке); если процесс завершился — выделение снимается; «Завершить процесс» без выделенной строки показывает подсказку «Выберите процесс из списка» вместо молчаливого возврата ([`ProcessInspectorViewModel.cs`](Configuration%20Management/ViewModels/ProcessInspectorViewModel.cs), [`ProcessInspectorSelectionTests.cs`](ConfigurationManagement.Tests/ProcessInspectorSelectionTests.cs)).

- **Создание серверной базы ([#305](https://github.com/sivatorov/ConfigurationManagement/issues/305))**: сравнение серверов учитывает порт (`localhost:1541` и `localhost:1545` — разные серверы); в предупреждении о различии версий платформы выводится полный адрес найденной базы с портом; цепочка сохранения/восстановления «Сервера СУБД» при повторном открытии окна создания восстановлена ([`CreateInfobaseService.cs`](Configuration%20Management/Services/CreateInfobaseService.cs), [`CreateInfobaseWindow.xaml.cs`](Configuration%20Management/Views/CreateInfobaseWindow.xaml.cs), [`CreateInfobaseDbServerStringTests.cs`](ConfigurationManagement.Tests/CreateInfobaseDbServerStringTests.cs)).

- **Горизонтальный скрол ([#309](https://github.com/sivatorov/ConfigurationManagement/issues/309))**: ширина прокручиваемой области считается строго по сумме видимых колонок (`max(total, viewport)` без «хвоста» от extent) — справа от последней колонки больше нет пустого места; `EnsureHorizontalReach` не докручивает полосу вправо при старте и после удаления колонки, горизонтальная позиция сохраняется при смене набора колонок ([`MainWindow.Columns.cs`](Configuration%20Management/Views/MainWindow.Columns.cs), [`MainWindow.Scroll.cs`](Configuration%20Management/Views/MainWindow.Scroll.cs), [`ListMinWidthCalculator.cs`](Configuration%20Management/Views/ListMinWidthCalculator.cs), [`ListMinWidthCalculatorTests.cs`](ConfigurationManagement.Tests/ListMinWidthCalculatorTests.cs)).

- **Окно «Типовые конфигурации» ([#321](https://github.com/sivatorov/ConfigurationManagement/issues/321))**: окно правки редакции стало выше — поле URL видно без прокрутки; поля формы и окно поиска больше не выглядят «серыми»/недоступными; главному окну списка добавлена стандартная кнопка максимизации ([`EditionEditWindow.xaml`](Configuration%20Management/Views/EditionEditWindow.xaml), [`EditionEditWindow.Avalonia.cs`](Configuration%20Management/Views/EditionEditWindow.Avalonia.cs), [`ConfigTypesEditWindow.xaml`](Configuration%20Management/Views/ConfigTypesEditWindow.xaml)).

- **Снятие выделения после мультивыделения ([#340](https://github.com/sivatorov/ConfigurationManagement/issues/340))**: клик, которым закрыли контекстное меню, дедуплицируется по данным события (время + позиция, helper `IsSameClick` с допуском) и не доходит до дерева как новое действие выбора; выбор строки применяется однократно по данным реального `MouseDown` — строка остаётся активной, выделение не пропадает «через мгновение» ([`BatchSelectionHelper.cs`](Configuration%20Management/Services/BatchSelectionHelper.cs), [`MainWindow.Events.cs`](Configuration%20Management/Views/MainWindow.Events.cs), [`MainWindow.Hotkeys.cs`](Configuration%20Management/Views/MainWindow.Hotkeys.cs), [`BatchSelectionHelperTests.cs`](ConfigurationManagement.Tests/BatchSelectionHelperTests.cs)).

## Установка

**Windows:** запустите `ConfigurationManagement.exe` — self-contained single-file, .NET не требуется.

**Linux:** сделайте бинарник исполняемым и запустите:
`chmod +x ConfigurationManagement-linux-x64 && ./ConfigurationManagement-linux-x64`

или установите deb-пакет: `sudo dpkg -i configuration-management_0.3.9.300_amd64.deb`.

## Скачать

- Windows (self-contained single-file): [ConfigurationManagement.exe](https://github.com/sivatorov/ConfigurationManagement/releases/download/v0.3.9.300/ConfigurationManagement.exe)
- Linux x64 (single-file): [ConfigurationManagement-linux-x64](https://github.com/sivatorov/ConfigurationManagement/releases/download/v0.3.9.300/ConfigurationManagement-linux-x64)
- Debian/Ubuntu: [configuration-management_0.3.9.300_amd64.deb](https://github.com/sivatorov/ConfigurationManagement/releases/download/v0.3.9.300/configuration-management_0.3.9.300_amd64.deb)
- Контрольные суммы: [SHA256SUMS.txt](https://github.com/sivatorov/ConfigurationManagement/releases/download/v0.3.9.300/SHA256SUMS.txt)

### SHA-256

```
d0838f7ce99e9821b717640a31b7d688d52fc43dc4a507c1bdb3fd48a1cc28d7  ConfigurationManagement.exe
d5a6ab7bc099c27e24d26c9ba89ea509706ab24e011e107a0021357c43e17928  ConfigurationManagement-linux-x64
ecf69c75cea6e33d3fa8ff3a77bf020fd23267524b7a0f0fe28b512f4e185b60  configuration-management_0.3.9.300_amd64.deb