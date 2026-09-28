Реализовано в версии **0.3.9.107** (Windows/WPF и Linux/Avalonia).

**Что сделано.** В окне создания серверной информационной базы рядом с флажком
«Блокировка фоновых заданий» появился второй флажок — «Запретить локальное
распознавание речи» (как в типовом стартере). При создании клиент-серверной
базы с установленным флажком команда `CREATEINFOBASE` получает документированный
параметр строки подключения `disstt="Y"`, который устанавливает запрет
локального распознавания речи на сервере 1С. Значение флага также сохраняется
в настройках подключения базы.

**Как реализовано.**

- Модель [`Models/CreateInfobaseRequest.cs`](Configuration%20Management/Models/CreateInfobaseRequest.cs) —
  новое свойство `ForbidSpeechRecognition`.
- Windows/WPF — [`Views/CreateInfobaseWindow.xaml`](Configuration%20Management/Views/CreateInfobaseWindow.xaml):
  флажок `ForbidSpeechCheck` рядом с `BlockJobsCheck`; проброс в запрос —
  [`Views/CreateInfobaseWindow.xaml.cs`](Configuration%20Management/Views/CreateInfobaseWindow.xaml.cs).
- Linux/Avalonia — [`Views/CreateInfobaseWindow.Avalonia.cs`](Configuration%20Management/Views/CreateInfobaseWindow.Avalonia.cs):
  аналогичный флажок `_forbidSpeechCheck`.
- Проброс: [`Services/CreateInfobaseService.cs`](Configuration%20Management/Services/CreateInfobaseService.cs) →
  `OneCLauncher.CreateInfoBase` (обе платформы). Строка подключения собирается
  общим чистым helper'ом `BuildClientServerCreateConnectionString`
  ([`Services/OneCLauncher.Create.cs`](Configuration%20Management/Services/OneCLauncher.Create.cs)),
  который добавляет `;disstt="Y"` и покрыт юнит-тестами.
- Настройки подключения: [`Models/ConnectionSettings.cs`](Configuration%20Management/Models/ConnectionSettings.cs) —
  свойство `ForbidSpeechRecognition` + обратный разбор параметра `disstt`
  из строки подключения.
- Локализация ru/en: ключ `CreateInfobase.ForbidSpeechRecognition`.

**Как проверить.**

1. Установите версию **0.3.9.107** (Windows или Linux).
2. Создание базы → тип «Клиент-серверная», заполните поля.
3. Установите флажок «Запретить локальное распознавание речи» и создайте базу.
4. В консоли кластера 1С у созданной базы (или при подключении к ней) запрет
   локального распознавания речи установлен.

Примечание: issue не закрывается автоматически — финальный релиз сборки
выполняется по завершении цикла 0.3.9.109.