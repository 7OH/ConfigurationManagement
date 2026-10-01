Исправлено в версии **0.3.9.242** (Windows/WPF и Linux/Avalonia) — создание серверной базы снова работает с портом 1С, выбор сервера из списка заполняет поле.

**Что было сломано (регресс после 0.3.9.150).**

1. **Выбор сервера из выпадающего списка ничего не давал** — поле «Сервер 1С» оставалось пустым.
2. **База не создавалась даже с ручным указанием `server:port`** — подозрение на неподстановку порта 1С (1540/1541).
3. **Нет понятного предупреждения/ошибки при неудаче.**

**Причины и как исправлено.**

1. Обработчик выбора в редактируемом ComboBox сразу сбрасывал выделение (`SelectedItem = null`), а WPF/Avalonia синхронизируют `Text` с `SelectedItem` в обе стороны: только что подставленная строка `server:port` затиралась обратно в пустое значение. Сброс выделения отложен на следующий проход диспетчера — повторный выбор того же пункта по-прежнему срабатывает, текст в поле сохраняется ([`Views/CreateInfobaseWindow.xaml.cs`](Configuration%20Management/Views/CreateInfobaseWindow.xaml.cs), [`Views/CreateInfobaseWindow.Avalonia.cs`](Configuration%20Management/Views/CreateInfobaseWindow.Avalonia.cs)).

2. Порт 1С разбирался из поля `server:port` ([`CreateInfobaseService.ParseServerPort`](Configuration%20Management/Services/CreateInfobaseService.cs)) и сохранялся в параметры подключения созданной базы, но **в саму команду CREATEINFOBASE не передавался**: `BuildClientServerCreateConnectionString` собирала `Srvr="server";Ref="…"` без порта, и платформа подключалась к порту по умолчанию (1540) — на сервере с портом кластера 1541 создание падало. Добавлен сквозной параметр `serverPort`: порт попадает в `Srvr="server:port"` строки подключения команды создания ([`Services/OneCLauncher.Create.cs`](Configuration%20Management/Services/OneCLauncher.Create.cs), обе платформы — [`OneCLauncher.Arguments.cs`](Configuration%20Management/Services/OneCLauncher.Arguments.cs) и [`OneCLauncher.Linux.Process.cs`](Configuration%20Management/Services/OneCLauncher.Linux.Process.cs)), а также, как и раньше, в настройки подключения созданной базы (порт виден при следующем открытии свойств базы).

3. При неудачном создании к сообщению об ошибке платформы добавляется понятная подсказка: «Если сервер недоступен — проверьте порт кластера 1С (по умолчанию 1541; порт агента — 1540) и укажите его как «сервер:порт», например `srv1c:1541`» (ключ `CreateInfobase.CreateFailedPortHint`, ru/en).

**Тесты.** Юнит-тесты сборки строки подключения CREATEINFOBASE с портом сервера 1С / без порта / с параметрами СУБД и маппинга «ввод `server:port` → параметры создания → строка подключения» ([`CreateInfobaseDbServerStringTests.cs`](ConfigurationManagement.Tests/CreateInfobaseDbServerStringTests.cs)). Полный прогон `dotnet test` и сборка Linux-ветки (`dotnet build -p:BuildLinux=true`) — без ошибок.

**Как проверить.**

1. Установите версию **0.3.9.242**.
2. «Создать базу» → тип «Клиент-серверная» → поле «Сервер 1С»: выберите сервер из выпадающего списка — строка `server:port` остаётся в поле целиком (раньше оставалась пустой).
3. Создайте базу с нестандартным портом, например `srv1c:1541`: порт попадает в `Srvr="srv1c:1541"` команды CREATEINFOBASE и в параметры подключения созданной базы (свойства базы → «Сервер» и «Порт»).
4. При недоступном сервере/неуспешном создании показывается сообщение платформы + подсказка про порт кластера/агента.
5. Повторите на Linux (Avalonia).