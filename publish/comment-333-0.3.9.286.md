Исправлено в версии **0.3.9.286** (Windows/WPF и Linux/Avalonia).

**Что сделано по замечаниям:**

1. **«Задать основным» перенесена на панель кнопок.** Кнопка больше не занимает колонку в каждой строке — в строках остался только индикатор-флажок «Основная» (только для чтения). Кнопка «Задать основным» теперь на нижней панели рядом с «Добавить» и доступна при выбранной неосновной записи:
   - Windows/WPF — выделите строку в таблице (у основной записи кнопка недоступна);
   - Linux/Avalonia — кликните по строке, затем «Задать основным».
   ([`ItsAccountsWindow.xaml`](Configuration%20Management/Views/ItsAccountsWindow.xaml),
   [`ItsAccountsWindow.xaml.cs`](Configuration%20Management/Views/ItsAccountsWindow.xaml.cs),
   [`ItsAccountsWindow.Avalonia.cs`](Configuration%20Management/Views/ItsAccountsWindow.Avalonia.cs))

2. **Просмотр пароля в редакторе учётной записи.** У поля пароля появилась кнопка-«глаз»: нажатие показывает пароль (и позволяет править), повторное — скрывает обратно маской.
   - Windows/WPF — при показе вместо PasswordBox показывается текстовое поле, правки возвращаются в маску при скрытии;
   - Linux/Avalonia — смена символа маски.
   ([`ItsAccountEditWindow.xaml`](Configuration%20Management/Views/ItsAccountEditWindow.xaml),
   [`ItsAccountEditWindow.xaml.cs`](Configuration%20Management/Views/ItsAccountEditWindow.xaml.cs),
   [`ItsAccountEditWindow.Avalonia.cs`](Configuration%20Management/Views/ItsAccountEditWindow.Avalonia.cs))

3. **Ключ вместо значения в списках выбора.** Проверено: во всех ComboBox выбора учётной записи ИТС — в Настройках и в редакторе типовой конфигурации, на обеих платформах — уже задан `DisplayMemberPath`/`DisplayMemberBinding` на имя записи (`ItsAccountSelectionItem.Name`), поэтому отображается имя («Основная», «Бухгалтерия» и т.п.), а не внутренний идентификатор. Дополнительно гарантировано, что все пункты списка имеют непустое отображаемое имя (включая виртуальный пункт «Основная» и записи с пустым наименованием — для них подставляется плейсхолдер).

**Тесты.** [`ItsAccountSelectionBuilderTests`](ConfigurationManagement.Tests/ItsAccountSelectionBuilderTests.cs) дополнены: `Build_VirtualPrimaryItem_HasDisplayName` и `Build_AllItems_HaveDisplayNamesForComboBox` — отображаемое имя непусто у всех пунктов выбора. Полный набор `dotnet test` зелёный (1587), кросс-сборка Linux (`dotnet build -p:BuildLinux=true`) без ошибок.

**Как проверить:**

1. Установите **0.3.9.286** ([релиз v0.3.9.286](https://github.com/sivatorov/ConfigurationManagement/releases/tag/v0.3.9.286)).
2. Утилиты → Учётные данные ИТС: кнопка «Задать основным» на нижней панели, активна при выборе неосновной записи; в строках — только флажок «Основная».
3. Добавьте/измените запись: у поля «Пароль» кнопка-«глаз» показывает и скрывает пароль.
4. Настройки → Учётные данные ИТС и редактор типовой конфигурации: в списке выбора видно имя записи, а не ключ.