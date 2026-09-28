Реализовано в версии **0.3.9.109** (Windows/WPF и Linux/Avalonia).

**Что сделано.** Новая функциональность «Сценарии» — запуск пользовательских
скриптов/исполняемых файлов для выбранной информационной базы:

1. **Настройка сценариев** — меню «Утилиты» → «Настройка сценариев» (после
   «Выполнить резервирования»): окно списка сценариев с добавлением,
   редактированием и удалением. Сценарий: наименование, путь к файлу
   (кнопка «Обзор…») и параметры (каждая строка — отдельный параметр).
2. **Подстановки свойств базы** — в параметрах поддерживаются токены:
   `%name%` (имя ИБ), `%connection.server%`, `%connection.serverPort%`,
   `%connection.database%`, `%connection.filePath%`, `%connection.webUrl%`,
   `%connection.connectionString%` (вложенные свойства подключения через
   точку), а также текущая дата: `%date%` (ГГГГ-ММ-ДД) или `%date:Формат%`
   (формат .NET, например `%date:yyyyMMdd_HHmm%`). Неизвестный ключ
   остаётся в строке как есть.
3. **Удобство редактирования** — двойной клик по подстановке вставляет её
   в позицию курсора поля параметров; комбобокс «База для примера
   подстановок» показывает живую полную командную строку под текущие поля.
4. **Выполнение** — контекстное меню базы → «Выполнить скрипт» (после
   «Запустить конфигуратор»), хоткей **F5**. При нескольких сценариях
   открывается окно выбора с подсказкой полной командной строки
   (с подстановками для выбранной базы); запуск — двойным кликом или
   кнопкой «Выполнить». Одиночный сценарий выполняется сразу.
5. **Запуск** — команда передаётся системному shell (`cmd /c …` на Windows,
   `sh -c …` на Linux) через `ExternalCommandRunner` без ожидания
   завершения (fire-and-forget); команда пишется в историю запусков базы
   и в журнал приложения, показывается системное уведомление.

**Как реализовано.**

- Модель [`Models/ScriptScenario.cs`](Configuration%20Management/Models/ScriptScenario.cs) —
  `Id`, `Name`, `FilePath`, `Parameters List<string>`.
- Хранилище [`Services/ScriptScenarioStore.cs`](Configuration%20Management/Services/ScriptScenarioStore.cs)
  + `IScriptScenarioStore`: каждый сценарий — отдельный JSON-файл
  `<DataDir>/scripts/scenarios/*.script.json`, имя — санитизированный Id
  (как `BackupScenarioStore`); регистрация в `AppServices.cs`.
- Чистый резолвер [`Services/ScriptParameterResolver.cs`](Configuration%20Management/Services/ScriptParameterResolver.cs):
  `BuildValueMap(Infobase)` + `Resolve(template, values, now, leaveUnknown)`
  + `BuildCommandLine(scenario, values, now)`; покрыт юнит-тестами
  (подстановка `%name%`, `%connection.server%`, вложенные ключи,
  дата/формат, неизвестный ключ остаётся или пустой, построение командной строки).
- Окна: `ScriptScenariosWindow`, `ScriptScenarioEditWindow`, `ScriptPickWindow` —
  Windows/WPF ([`.xaml`](Configuration%20Management/Views/ScriptScenariosWindow.xaml)
  + code-behind) и Linux/Avalonia (`*.Avalonia.cs`), модальность #291,
  темизация ModernMenuItem/ModalWindowBase.
- VM: `MainViewModel.Scripts.cs` + `MainViewModel.Avalonia.Scripts.cs`
  (команды `ShowScriptsSettingsCommand`, `RunScriptForSelectedCommand`, хоткей F5).
- Меню и контекстное меню: `MainWindow.xaml`, `MainWindow.Avalonia.Tree.cs`,
  хоткеи `MainWindow.Hotkeys.cs` / `MainWindow.Avalonia.Hotkeys.cs`.
- Локализация ru/en: ключи `Script.*`, `Notify.ScriptStarted`.

**Как проверить.**

1. Установите версию **0.3.9.109** (Windows или Linux).
2. «Утилиты» → «Настройка сценариев» → «Добавить»: укажите имя, путь к
   скрипту (например, `notepad.exe` / `echo`) и параметры, например
   `%name%` и `%connection.server%`; сохраните.
3. Выберите базу в списке, нажмите **F5** или «Выполнить скрипт» из
   контекстного меню — скрипт запустится с подставленными значениями,
   в истории запусков базы появится команда.
4. Создайте второй сценарий и снова нажмите F5 — откроется окно выбора
   скрипта с подсказкой командной строки; двойной клик — выполнить.

Примечание: issue не закрывается автоматически — финальный релиз сборки
выполняется по завершении цикла 0.3.9.109.