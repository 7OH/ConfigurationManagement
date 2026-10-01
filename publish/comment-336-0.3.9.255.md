Исправлено в версии **0.3.9.255** (Windows/WPF и Linux/Avalonia).

**Что было.** В заголовке группы показывалось только рекурсивное количество баз: «Группа с базами (28)». Количество вложенных подгрупп не отображалось, хотя группа с глубокой иерархией выглядит как единый блок — не видно, сколько групп внутри.

**Как исправлено.** Для группы с подгруппами счётчик теперь показывает оба числа: **«Группа с базами (4 / 28)»**, где 4 — количество подгрупп (рекурсивно), 28 — количество баз (рекурсивно). Формат «(N / M)» показывается только когда подгруппы есть; у группы без подгрупп вид счётчика не изменился — прежнее «(28)».

- **WPF** ([`Views/MainWindow.xaml`](Configuration%20Management/Views/MainWindow.xaml)) и **Avalonia** ([`Views/MainWindow.Avalonia.Tree.cs`](Configuration%20Management/Views/MainWindow.Avalonia.Tree.cs)): счётчик привязан к вычисляемому суффиксу `GroupCountSuffix` вместо форматируемой привязки к `TotalInfobaseCount`;
- **VM** ([`ViewModels/GroupNodeViewModel.cs`](Configuration%20Management/ViewModels/GroupNodeViewModel.cs) и [`GroupNodeViewModel.Avalonia.cs`](Configuration%20Management/ViewModels/GroupNodeViewModel.Avalonia.cs)): добавлены рекурсивное свойство `TotalSubgroupCount` и строковый суффикс `GroupCountSuffix` («(N / M)» при N > 0, иначе «(M)»); уведомления о новых свойствах поднимаются в `NotifyCountChanged()` (с цепочкой по родителям) и после `PopulateItems()`;
- **локализация**: разделитель чисел вынесен в шаблон `Main.GroupCountWithSubgroups` («{0} / {1}») в [`ru.json`](Configuration%20Management/Localization/Languages/ru.json)/[`en.json`](Configuration%20Management/Localization/Languages/en.json);
- служебные узлы «Закреплённые» и «Без группы» (подгрупп не имеют) сохранили прежний вид счётчика «(M)»; вкладки «Избранное», «Недавние», «Найдено» не затронуты.

**Тесты.** Добавлены сценарии в [`GroupNodeViewModelTests`](ConfigurationManagement.Tests/GroupNodeViewModelTests.cs): рекурсивный подсчёт подгрупп на дереве глубины 3, совместный подсчёт групп и баз, формат суффикса с подгруппами/без, пустой служебный узел, уведомления по цепочке родителей. Полный набор `dotnet test` зелёный (1469 тестов), кросс-сборка Linux (`dotnet build -p:BuildLinux=true`) без ошибок.

**Как проверить в 0.3.9.255.**

1. Установите 0.3.9.255 (Windows или Linux).
2. Откройте главное окно. У группы, внутри которой есть вложенные группы, в заголовке должно быть «(N / M)»: N — количество подгрупп рекурсивно, M — количество баз рекурсивно.
3. У группы без подгрупп счётчик прежний — «(M)».
4. Проверьте узлы «Закреплённые» и «Без группы» — у них остался прежний вид счётчика.
5. Переключите компактный режим интерфейса — длинный суффикс «(N / M)» помещается в заголовке группы.