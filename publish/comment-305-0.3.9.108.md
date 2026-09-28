Реализовано в версии **0.3.9.108** (Windows/WPF и Linux/Avalonia).

**Что сделано.** Окно создания серверной информационной базы переработано:

1. **Компактный макет без прокрутки** — поля параметров клиент-серверной
   базы разложены в две колонки: слева сервер 1С, имя базы на сервере,
   тип СУБД и галочки (создание базы данных, блокировка фоновых заданий,
   запрет распознавания речи); справа — подключение к СУБД (адрес и порт,
   имя базы данных, пользователь, пароль). Вертикальный размер панели
   заметно уменьшен.
2. **Порт СУБД в одной строке с адресом** — рядом с полем «Сервер СУБД»
   появилось поле порта; значение попадает в параметр `DBSrvr` команды
   `CREATEINFOBASE`.
3. **Живая подсказка-пример** — под полем адреса сервера СУБД показывается
   формат значения `DBSrvr` для выбранной СУБД: для PostgreSQL —
   «localhost port=5433» (порт через пробел, как принимает платформа),
   для MSSQL Server — «localhost,1433». Если адрес и порт заполнены,
   подсказка показывает, что именно будет передано в `CREATEINFOBASE`.

**Как реализовано.**

- Модель [`Models/CreateInfobaseRequest.cs`](Configuration%20Management/Models/CreateInfobaseRequest.cs) —
  новое свойство `DbPort`.
- Чистый helper
  [`Services/CreateInfobaseService.cs`](Configuration%20Management/Services/CreateInfobaseService.cs):
  `BuildDbServerString(dbms, server, port)` — PostgreSQL → `host port=NNNN`,
  MSSQL Server → `host,NNNN`, остальные СУБД → просто `host`; пустой порт
  строку не меняет. Helper покрыт юнит-тестами.
- Windows/WPF — [`Views/CreateInfobaseWindow.xaml`](Configuration%20Management/Views/CreateInfobaseWindow.xaml):
  двухколоночная панель, поле `DbPortBox` рядом с `DbServerBox`, подсказка
  `DbServerHint`; логика обновления — 
  [`Views/CreateInfobaseWindow.xaml.cs`](Configuration%20Management/Views/CreateInfobaseWindow.xaml.cs).
- Linux/Avalonia — [`Views/CreateInfobaseWindow.Avalonia.cs`](Configuration%20Management/Views/CreateInfobaseWindow.Avalonia.cs):
  та же двухколоночная раскладка, поле `_dbPortBox` и живая подсказка
  `_dbServerHint` (обновляется по выбору/вводу СУБД и значениям адреса/порта).
- Локализация ru/en: ключи `CreateInfobase.DbPortTooltip`,
  `CreateInfobase.DbServerHintPostgres/Mssql/Other`, `CreateInfobase.DbServerPreview`.

**Как проверить.**

1. Установите версию **0.3.9.108** (Windows или Linux).
2. Создание базы → тип «Клиент-серверная».
3. Выберите СУБД **PostgreSQL**, укажите адрес `localhost` и порт `5433` —
   под полем появится подсказка «В DBSrvr будет передано: localhost port=5433».
4. Выберите СУБД **MSSQLServer** — формат подсказки сменится на «localhost,1433».
5. Создайте базу — она создастся с корректным значением `DBSrvr`.

Примечание: issue не закрывается автоматически — финальный релиз сборки
выполняется по завершении цикла 0.3.9.109.