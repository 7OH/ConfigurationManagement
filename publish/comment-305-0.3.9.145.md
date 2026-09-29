Исправлено в версии **0.3.9.145** (Windows/WPF и Linux/Avalonia).

По вашим пожеланиям к окну создания серверной базы сделано три доработки:

1. **Запоминание «Сервер СУБД» (+ порт) между сессиями.** После успешного создания
   клиент-серверной базы введённые сервер СУБД и порт сохраняются в настройках приложения
   и подставляются по умолчанию при следующем открытии окна — по тому же механизму, что уже
   работает для версии платформы
   ([`Models/AppSettings.cs`](Configuration%20Management/Models/AppSettings.cs),
   [`Services/CreateInfobaseService.cs`](Configuration%20Management/Services/CreateInfobaseService.cs),
   [`Views/CreateInfobaseWindow.xaml.cs`](Configuration%20Management/Views/CreateInfobaseWindow.xaml.cs),
   [`Views/CreateInfobaseWindow.Avalonia.cs`](Configuration%20Management/Views/CreateInfobaseWindow.Avalonia.cs)).

2. **Клик по подсказке «Например localhost».** Подсказка формата DBSrvr под полем
   «Сервер СУБД» стала кликабельной (курсор-рука): клик подставляет пример `localhost`
   в пустое поле «Сервер СУБД» — как вы и просили.

3. **Выбор сервера 1С вместе с портом — как в окне правки свойств базы.** В окне создания
   рядом с «Сервер 1С» появилось поле «Порт сервера 1С», а выпадающий список содержит
   строки вида `server:port` (формируются из уже зарегистрированных клиент-серверных баз).
   При выборе значение разносится на имя сервера и порт: имя попадает в команду
   CREATEINFOBASE, порт — в параметры подключения созданной базы. Порт сервера 1С отдельный
   и не смешивается с портом СУБД
   ([`ViewModels/MainViewModel.Commands.cs`](Configuration%20Management/ViewModels/MainViewModel.Commands.cs),
   [`ViewModels/MainViewModel.Avalonia.cs`](Configuration%20Management/ViewModels/MainViewModel.Avalonia.cs),
   [`Models/CreateInfobaseRequest.cs`](Configuration%20Management/Models/CreateInfobaseRequest.cs)).

Локализация новых подписей добавлена в ru/en
([`Localization/Languages/ru.json`](Configuration%20Management/Localization/Languages/ru.json),
[`Localization/Languages/en.json`](Configuration%20Management/Localization/Languages/en.json));
тесты форматов `server:port` добавлены в
[`CreateInfobaseDbServerStringTests.cs`](ConfigurationManagement.Tests/CreateInfobaseDbServerStringTests.cs)
(все 648 тестов зелёные).

**Как проверить.**

1. Установите версию **0.3.9.145** (Windows или Linux).
2. Создайте серверную базу, указав сервер СУБД (например `localhost`) и порт — закройте окно
   и откройте создание ещё раз: значения подставятся автоматически.
3. В клиент-серверном типе кликните по подсказке «Например localhost» — пример подставится
   в поле «Сервер СУБД».
4. Откройте список «Сервер 1С»: пункты вида `server:port`; при выборе порт сервера 1С
   появится в соседнем поле и будет сохранён у созданной базы.