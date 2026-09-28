Исправлено в версии **0.3.9.119** (Windows/WPF и Linux/Avalonia) — все 5 замечаний по «Сценариям».

**1. Двойной клик по подстановке вставляет только токен, а не «токен — описание».**
Список подстановок теперь содержит объекты `ScriptTokenHint`, а строка
«%name% — имя базы» собирается шаблоном отображения (DataTemplate/ItemTemplate);
двойной клик вставляет в поле параметров только сам токен (`%name%`).
Windows/WPF — [`Views/ScriptScenarioEditWindow.xaml`](Configuration%20Management/Views/ScriptScenarioEditWindow.xaml)
(DataTemplate), Linux/Avalonia — [`Views/ScriptScenarioEditWindow.Avalonia.cs`](Configuration%20Management/Views/ScriptScenarioEditWindow.Avalonia.cs)
(ItemTemplate через `FuncDataTemplate<ScriptTokenHint>`).

**2. Резолвер принимает все ключи + пароль.**
[`Services/ScriptParameterResolver.cs`](Configuration%20Management/Services/ScriptParameterResolver.cs):
- добавлены токены `%connection.password%` и `%password%` (пароль подключения базы);
- добавлены явные ключи `connection.blockScheduledJobs`, `connection.forbidSpeechRecognition`,
  `connection.authenticationMode`, `connection.useOsAuthentication`;
- карта значений дополнена динамическим проходом рефлексией по публичным свойствам
  `ConnectionSettings`/`Infobase` (кэшированные списки свойств): ключи
  `connection.<имя>` и плоские `<имя>` в нижнем регистре генерируются для ЛЮБЫХ
  текущих и будущих свойств моделей автоматически; явные ключи пишутся поверх
  динамических. Поведение неизвестного ключа не изменилось (остаётся как есть).

**3. Комбобокс «База для примера подстановок» показывает имя базы.**
На Linux/Avalonia задан ItemTemplate с выводом `Infobase.Name` (раньше показывалось
имя типа, т.к. `Infobase` не переопределяет `ToString()`); на Windows/WPF при открытии
окна выставляется `SelectedIndex = 0`, чтобы комбобокс и превью командной строки
показывали одну и ту же базу.

**4. Редактирование сценария больше не создаёт копию.**
Окна редактирования (WPF и Avalonia) сохраняют переданный сценарий и применяют поля
к нему же: `Id` сохраняется, `ScriptScenarioStore.Save` по тому же Id перезаписывает
тот же файл — дублей в списке сценариев нет.

**5. Новое свойство «Скрывать окно скрипта».**
- `ScriptScenario.HideWindow` (по умолчанию `true` — окно скрыто); чекбокс в окне
  редактирования (WPF + Avalonia), переносится через `ApplyTo`;
- `ExternalCommandRunner`: публичный `CreateProcessStartInfo(command, createNoWindow = true)`
  и параметр `createNoWindow` в `RunAsync`/`RunDetached` (поведение pre/post-команд
  функции №8 не меняется — по умолчанию `true`);
- запуск сценария использует `createNoWindow: !scenario.HideWindow`: при снятой галке
  на Windows появляется консольное окно `cmd`. На Linux `/bin/sh` выполняется без
  терминала, поэтому видимое окно зависит от окружения (ограничение платформы).

Локализация ru/en: новые ключи `Script.HideWindow`, `Script.TokenConnectionPassword`,
`Script.TokenPassword`; в списке подстановок появились токены `%connection.password%`
и `%password%`.

**Как проверить.**

1. Установите версию **0.3.9.119** (Windows или Linux).
2. «Утилиты» → «Настройка сценариев» → «Добавить»: в списке подстановок дважды
   кликните по строке «%name% — имя базы» — в поле параметров вставится только
   `%name%`.
3. В комбобоксе «База для примера подстановок» выберите базу — отображается её имя,
   превью командной строки соответствует выбранной базе.
4. В параметрах используйте `%connection.password%` или `%password%` — при запуске
   (F5) подставится пароль подключения базы (если задан).
5. Отредактируйте существующий сценарий и сохраните — в списке останется один
   сценарий с тем же именем/параметрами (копия не появится).
6. Снимите галку «Скрывать окно скрипта» и запустите сценарий на Windows —
   появится консольное окно `cmd`; при установленной галке окно скрыто.