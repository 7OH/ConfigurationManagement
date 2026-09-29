Исправлено в версии **0.3.9.157** (Windows/WPF и Linux/Avalonia).

**Что было сломано.** Команды встроенного монитора серверов 1С завершались ошибкой:
в журнале «RAC: --host=localhost --port=27540 cluster list», rac отвечал кодом -1
«Ошибка разбора параметра: --host=localhost», хотя ручной запуск
`rac.exe localhost:27545 cluster list` работал. Дополнительно в окне монитора
тексты строк и заголовков колонок были прижаты к верхнему краю.

**Причина.** `RacClient.BuildArguments` собирал точку подключения двумя раздельными
аргументами `--host=addr` и `--port=N`. На версии rac (новой платформы 1С) этот
синтаксис не разбирается — rac принимает точку подключения единым токеном
`host:port` первым аргументом.

**Как исправлено.**

- Точка подключения передаётся rac ОДНИМ токеном `host:port` первым аргументом:
  `rac.exe localhost:27545 cluster list`. Если порт ≤ 0 — остаётся только `host`;
  если адрес пуст — токен подключения опускается. Логин/пароль по-прежнему
  передаются как `--user=`/`--password=`, поэтому маскирование пароля в журнале
  (`SensitiveDataMasker.MaskRacPassword`) продолжает работать: rac-команда пишется
  в лог без значения пароля. Адрес с пробелами остаётся одним токеном (передача
  через `ArgumentList` без shell). Формат `host:port` — штатный для rac во всех
  версиях платформы (используется в примерах 1С), раздельный `--host`/`--port`
  не поддерживался.
  [`Services/RacClient.cs`](Configuration%20Management/Services/RacClient.cs) —
  `BuildArguments`; тесты обновлены:
  [`ConfigurationManagement.Tests/RacClientTests.cs`](ConfigurationManagement.Tests/RacClientTests.cs)
  (включая «адрес с пробелами — единый токен» и «пустой адрес — без токена»).
- Вертикальное выравнивание текстов монитора серверов по центру:
  - Windows/WPF — [`Views/ServerMonitorWindow.xaml`](Configuration%20Management/Views/ServerMonitorWindow.xaml):
    для всех таблиц окна задан явный стиль заголовков колонок
    (`CenteredColumnHeader`, `VerticalContentAlignment=Center`);
    сами ячейки уже центрировались глобальным стилем `DataGridCell`.
  - Linux/Avalonia — [`Views/ServerMonitorWindow.Avalonia.cs`](Configuration%20Management/Views/ServerMonitorWindow.Avalonia.cs):
    контейнеры строк `ListBoxItem` получили `VerticalContentAlignment=Center` и
    единую высоту `MinHeight=36` (аналог `RowHeight` WPF-версии), поэтому текст
    в строках больше не прижимается к верхнему краю.
- Полный прогон тестов зелёный (708/708), кросс-сборка
  `dotnet build -p:BuildLinux=true` без ошибок.

**Как проверить.**

1. Установите версию **0.3.9.157** (Windows или Linux).
2. Откройте «Серверы 1С», введите адрес/порт сервера (например, `localhost:27545`
   для RAS) и нажмите «Подключить». В журнале rac-команда теперь имеет вид
   `RAC: localhost:27545 cluster list`, кластеры загружаются без ошибки разбора
   параметров (если к серверу есть доступ).
3. Откройте вкладки «Рабочие процессы / Сеансы / Соединения / Блокировки» —
   тексты в колонках и заголовках выровнены по вертикали по центру.
4. Если задан пароль администратора кластера, убедитесь, что в журнале он
   по-прежнему скрыт (`--password=***`).

Файлы для установки — на странице релиза
[v0.3.9.157](https://github.com/sivatorov/ConfigurationManagement/releases/tag/v0.3.9.157).