Исправлено в версии **0.3.9.113** (Windows/WPF и Linux/Avalonia).

**Что было не так.**

1. Окно создания ИБ имело фиксированную высоту (640 px): при клиент-серверном типе
   содержимое не всегда влезало, появлялась вертикальная прокрутка.
2. Поле «Сервер 1С» было обычным текстовым полем — известный сервер приходилось
   вводить вручную каждый раз, хотя список серверов уже есть в списке баз.

**Как исправлено.**

1. **Авторазмер окна.** Фиксированная высота заменена на авторазмер по содержимому:
   окно само подгоняет высоту под текущий набор полей, поэтому всё влезает без
   вертикальной прокрутки. При переключении типа «Файловая ↔ Клиент-серверная» и при
   показе/скрытии панели шаблона высота пересчитывается автоматически. Сохранены
   `Width=620`, `MinHeight=420`, `MaxHeight=800` и возможность изменения размера окна.
   - Windows/WPF — [`Views/CreateInfobaseWindow.xaml`](Configuration%20Management/Views/CreateInfobaseWindow.xaml):
     `Height=640` → `SizeToContent="Height"`.
   - Linux/Avalonia — [`Views/CreateInfobaseWindow.Avalonia.cs`](Configuration%20Management/Views/CreateInfobaseWindow.Avalonia.cs):
     шаблонный режим (с деревом шаблонов) переведён с фикс. `Height=640` на
     `SizeToContent.Height` + `MaxHeight=800`, `CanResize` сохранён; пустой режим
     и раньше был `SizeToContent.Height` — не менялся.

2. **Выбор сервера 1С из списка.** Поле «Сервер 1С» стало редактируемым выпадающим
   списком (как «СУБД»): можно выбрать уже известный сервер из зарегистрированных
   клиент-серверных баз (список формируется тем же способом, что в окне настройки
   подключения — `GetAvailableServers()`/`AvailableServers()`) или ввести адрес вручную.
   - Windows/WPF — `ServerBox` (TextBox) → `ComboBox IsEditable="True"`,
     `ItemsSource = availableServers` ([`Views/CreateInfobaseWindow.xaml`](Configuration%20Management/Views/CreateInfobaseWindow.xaml),
     [`Views/CreateInfobaseWindow.xaml.cs`](Configuration%20Management/Views/CreateInfobaseWindow.xaml.cs)).
   - Linux/Avalonia — `_serverBox` (TextBox) → `ComboBox IsEditable`
     ([`Views/CreateInfobaseWindow.Avalonia.cs`](Configuration%20Management/Views/CreateInfobaseWindow.Avalonia.cs)).
   - Список пробрасывается в конструктор окна из
     [`ViewModels/MainViewModel.Commands.cs`](Configuration%20Management/ViewModels/MainViewModel.Commands.cs)
     (`GetAvailableServers()`) и
     [`ViewModels/MainViewModel.Avalonia.cs`](Configuration%20Management/ViewModels/MainViewModel.Avalonia.cs)
     (`AvailableServers()`).

Компактный макет 0.3.9.108, живая подсказка формата DBSrvr и порт СУБД не затронуты;
существующие тесты `CreateInfobaseDbServerStringTests` остаются зелёными.

**Как проверить.**

1. Установите версию **0.3.9.113** (Windows или Linux).
2. Откройте окно создания ИБ: содержимое влезает полностью, вертикальной прокрутки нет.
3. Переключите тип «Файловая ↔ Клиент-серверная» — высота окна меняется под содержимое.
4. В клиент-серверном типе откройте поле «Сервер 1С» — в списке серверы из уже
   зарегистрированных клиент-серверных баз; можно также ввести адрес вручную.
5. Создайте серверную ИБ с выбранным из списка сервером — значение сервера попадает
   в команду CREATEINFOBASE как раньше.