# Управление конфигурациями 1С — v0.3.9.296

Серия из **шести исправлений** (цикл 0.3.9.291–0.3.9.296) по обращениям и задачам проекта:
снятие выделения после мультивыделения (#340), свёртка групп — горячие клавиши и Ctrl+клик
(#341), инспектор процессов — «неизвестная база» и завершение процесса (#342), серверы 1С —
подключение к введённому адресу и понятные ошибки (#324), создание серверной базы (#305),
горизонтальный скролл списка баз, 10-я попытка (#309). Каждое исправление реализовано на
обеих платформах (Windows/WPF и Linux/Avalonia), где проблема воспроизводится, с
юнит-тестами; полный набор `dotnet test` зелёный, кросс-сборка Linux без ошибок.

## Изменения

### 0.3.9.291 — [#340](https://github.com/sivatorov/ConfigurationManagement/issues/340)

Снятие выделения после мультивыделения (Windows/WPF, регресс фикса 0.3.9.277): отложенное
применение клика после закрытия контекстного меню больше не «перевыбирает» строку — если
штатная логика (клик дошёл до дерева после закрытия попапа) уже выбрала строку и сняла
мультивыделение, повторное применение не выполняется. Если строка уже выбрана, но набор
мультивыделения ещё «висит» — снимается только набор, выделение текущей строки не трогается
([`MainWindow.Hotkeys.cs`](Configuration%20Management/Views/MainWindow.Hotkeys.cs) —
`TryApplyTreeClickAfterMenuClosed`). Просьба к пользователю: проверить сценарий
«мультивыделение → правый клик → левый клик в другом месте» — выбор должен оставаться на
последней строке и не пропадать «через секунду».

### 0.3.9.292 — [#341](https://github.com/sivatorov/ConfigurationManagement/issues/341)

Свёртка групп: горячие клавиши и Ctrl+клик по группе (регресс фикса 0.3.9.289):

- **Ctrl+Alt++ / Ctrl+Alt+- срабатывают с первого нажатия на обеих платформах**: физическое
  нажатие «плюс» на основной клавиатуре содержит Shift и раньше перехватывалось обработчиком
  «Ctrl+Shift++ — развернуть всё»; ветка Ctrl+Alt разбирается явно, из ветки «развернуть/
  свернуть всё» исключён Alt ([`MainWindow.Hotkeys.cs`](Configuration%20Management/Views/MainWindow.Hotkeys.cs),
  [`MainWindow.Avalonia.Hotkeys.cs`](Configuration%20Management/Views/MainWindow.Avalonia.Hotkeys.cs));
- **Ctrl+клик по группе на Linux/Avalonia**: добавлена недостающая ветка в
  [`LeveledTreeView.Avalonia.cs`](Configuration%20Management/Controls/LeveledTreeView.Avalonia.cs) —
  разворот/сворачивание ветки (группа + все подгруппы), текущая строка не меняется
  (зеркало WPF-ветки);
- **ветка разворачивается даже у свёрнутой группы**: команда применяется к выбранной группе
  независимо от её состояния; повторный toggle сворачивает ветку вместе со вложенными группами;
- **тесты**: [`GroupNodeViewModelTests`](ConfigurationManagement.Tests/GroupNodeViewModelTests.cs) —
  полный цикл toggle свёрнутой группы.

### 0.3.9.293 — [#342](https://github.com/sivatorov/ConfigurationManagement/issues/342)

Инспектор процессов: «неизвестная база» и завершение процесса:

- **сопоставление базы с командной строкой процесса стало устойчивым**
  ([`RunningInfobaseMatcher.cs`](Configuration%20Management/Services/RunningInfobaseMatcher.cs)):
  порт в адресе сервера базы («localhost:1541») игнорируется, хост и имя базы сравниваются без
  учёта регистра, кавычки и пробелы внутри имени базы не ломают разбор (`MatchesServerValue`,
  `TryExtractValue`);
- **кнопка «Завершить процесс» показывает причину отказа**
  ([`IOneCProcessKiller.cs`](Configuration%20Management/Services/IOneCProcessKiller.cs)):
  добавлено свойство `LastError`; на Windows сохраняется текст исключения («Отказано в
  доступе» и т.п.) с подсказкой про запуск приложения от имени администратора;
- **тесты**: [`RunningInfobaseMatcherTests`](ConfigurationManagement.Tests/RunningInfobaseMatcherTests.cs) —
  серверная база с портом в адресе, регистр имени базы, кавычки/пробелы, чужие сервер/база.

### 0.3.9.294 — [#324](https://github.com/sivatorov/ConfigurationManagement/issues/324)

Серверы 1С: подключение к введённому адресу и понятные ошибки:

- **сообщение об ошибке подключения содержит целевой адрес:порт**: «Не удалось подключиться к
  {0}:{1}. {2}» — видно, куда реально шёл запрос, даже если поля были заполнены сохранёнными
  значениями ([`ServerMonitorViewModel.cs`](Configuration%20Management/ViewModels/ServerMonitorViewModel.cs));
- **текст ошибки rac-команды дополнен точкой подключения** «rac (адрес:порт) завершился с кодом N»
  ([`RacClient.cs`](Configuration%20Management/Services/RacClient.cs));
- **подсказка о происхождении значений полей**: адрес/порт префиллятся из настроек последнего
  успешного подключения; на поле адреса добавлен ToolTip «Значение из настроек (последнее
  успешное подключение)» (WPF XAML и Avalonia).

### 0.3.9.295 — [#305](https://github.com/sivatorov/ConfigurationManagement/issues/305)

Создание серверной базы (регресс фикса 0.3.9.282):

- **разрядность базы при выборе версии без суффикса**: корень найден — `ParseVariant` без
  суффикса возвращал «32» по умолчанию, и прежняя логика трактовала его как явно выбранную
  разрядность (в базу записывалось «8.3.27 [x86]» при фактическом запуске x64). Теперь
  используется `ParseVariantOptionalArch`, а без суффикса база наследует режим «Разрядности
  по умолчанию» из настроек (`ResolveStoredArchitecture`/`ResolveCleanPlatform` в
  [`CreateInfobaseService.cs`](Configuration%20Management/Services/CreateInfobaseService.cs));
- **предупреждение о версии на сервере**: сравнение серверов устойчиво к порту и регистру
  (`SameServer`: «localhost:1541» ≡ «localhost»); уточнено сообщение — версия берётся из
  вашего списка баз приложения на том же сервере;
- **выбор сервера 1С из списка**: подстановка «server:port» в поле усилена на обеих
  платформах ([`CreateInfobaseWindow.xaml.cs`](Configuration%20Management/Views/CreateInfobaseWindow.xaml.cs),
  [`CreateInfobaseWindow.Avalonia.cs`](Configuration%20Management/Views/CreateInfobaseWindow.Avalonia.cs));
- **тесты**: [`CreateInfobaseDbServerStringTests`](ConfigurationManagement.Tests/CreateInfobaseDbServerStringTests.cs) —
  `ResolveStoredArchitecture`, `ResolveCleanPlatform`, `SameServer`.

### 0.3.9.296 — [#309](https://github.com/sivatorov/ConfigurationManagement/issues/309)

Горизонтальный скролл списка баз (Windows/WPF, 10-я попытка):

- **эксперимент B — учёт вертикального скроллбара**: по логу пользователя
  (`hdrRight ≈ 1102,8` при `viewport ≈ 1094,4`, `scrollable=2,4`) правый край последней
  колонки уходит под вертикальный скроллбар внутреннего ScrollViewer дерева. Когда
  вертикальная полоса видна и сумма колонок вместе с её шириной не помещается во вьюпорт —
  минимум контента прокрутки расширяется на ширину скроллбара (+2 px запаса), и
  горизонтальная прокрутка дотягивает до конца последней колонки
  ([`MainWindow.Columns.cs`](Configuration%20Management/Views/MainWindow.Columns.cs) —
  `UpdateTreeMinWidth`); лишняя полоса не появляется, пока контент помещается (анти-регресс #255);
- **диагностика**: в лог `CM_COLUMNS` добавлена видимость вертикального скроллбара
  (`vSb=Visible/Collapsed`); ждём от пользователя новый лог для подтверждения гипотезы;
- Avalonia не изменялась (проблема на ней не воспроизводится).

## Файлы

| Платформа | Файл | Размер |
|-----------|------|--------|
| Windows x64 (self-contained, один файл) | `ConfigurationManagement.exe` | ~80 МБ (84 015 055 б) |
| Linux x64 (self-contained, один файл)   | `ConfigurationManagement-linux-x64` | ~50 МБ (52 506 037 б) |
| Linux x64 (deb-пакет)                   | `configuration-management_0.3.9.296_amd64.deb` | ~43 МБ (45 279 362 б) |

Linux: `chmod +x ConfigurationManagement-linux-x64 && ./ConfigurationManagement-linux-x64`
или установка пакета: `sudo dpkg -i configuration-management_0.3.9.296_amd64.deb`.

Версия сборки: **0.3.9.296**. Полная история — в [CHANGELOG.md](https://github.com/sivatorov/ConfigurationManagement/blob/main/CHANGELOG.md).

Тесты: полный набор `dotnet test` зелёный, обе платформы (Windows/WPF и Linux/Avalonia)
собраны без ошибок.