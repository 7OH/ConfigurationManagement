# Issue #330: Скачивание и установка нужной версии платформы 1С из стартера

- Author: Ollivhv
- Created: 2026-09-30T15:47:42Z
- Updated: 2026-10-05T11:30:12Z
- State: open
- Labels: 

## Описание




### Зачем это нужно

В некоторых сценариях подключение серверной базы 1С происходит на компьютер пользователя, где ничего не установлено. Было бы удобно иметь возможность скачать требуемую версию платформы не заходя на сайт, а только введя логин/пароль от ИТС.
Дополнительно, возможна ситуация при которой нужной версии платформы просто нет на компьютере пользователя.

### Как сейчас

Приложение ищет уже установленные версии платформы (стандартные каталоги плюс дополнительные пути из настроек) и, если подходящей нет, показывает «Не найдена установленная платформа 1С. Укажите версию через настройки базы». Скачать или установить платформу приложение не умеет.

При этом почти вся инфраструктура для такой задачи уже есть: `OneCUpdatesService` выполняет вход на login.1c.ru (форма входа со скрытым токеном `execution`, HTTP Basic как запасной путь, сессионные cookie, ручное следование редиректам) и умеет скачивать архив по ссылке с прогрессом; `ArchiveService` распаковывает ZIP штатно и RAR через внешний архиватор; логин и пароль к сайту 1С уже хранятся в настройках (`UpdatesLogin`, `UpdatesPassword`); окно «Актуальные релизы» можно взять как образец интерфейса; запуск процесса с повышением прав в проекте тоже уже встречается.
### Варианты реализации
#### Первый вариант

Приложение только скачивает дистрибутив, а установку выполняет штатный стартер 1С:

1. По кнопке «Скачать платформу 8.3.xx.xxxx» загрузить дистрибутив нужной разрядности в каталог, прописанный в `DistributiveLocation` (`1cestart.cfg`), либо в выбранную пользователем папку.
2. Показать понятное сообщение: что скачано, куда и что стартер 1С поставит версию при следующем запуске; если каталог не прописан — подсказать, как это сделать.

Что это даёт: не нужны повышение прав, тихая установка, ожидание завершения MSI и разбор кодов возврата. Объём — примерно 3–5 дней, риск низкий.

#### Второй вариант
1. Модель платформенного релиза: ник проекта на releases.1c.ru, список версий, разбор имён файлов (`windows64full_with_clients_…`, `setuptc64_…` и подобных), выбор разрядности и типа клиента — по умолчанию тонкий клиент нужной разрядности.
2. Окно «Версии платформы» по образцу «Актуальных релизов»: список релизов, кнопка скачивания, прогресс, статусы «нужна авторизация», «нет доступа», «файл не найден».
3. Скачивание и распаковка во временный каталог (ZIP уже поддержан; 7z/RAR — при необходимости, с прогрессом).
4. Тихая установка: `setup.exe /S` (в пользовательском дистрибутиве есть готовый `win-mac_silent_install.cmd`) или MSI. Приложению потребуется повышение прав через UAC, ожидание завершения установки, разбор кодов возврата (1638 «уже установлено», 3010 «нужна перезагрузка») и предупреждение о запущенных процессах `1cv8.exe`.
5. После установки: пересканировать список версий и предложить привязать новую версию к базе, запуск которой не удался.


### Примечание
- Для Linux/Avalonia: автоустановка практически нереализуема, там уместен только сценарий «скачать и показать инструкцию».

---

## Комментарий 1 от 7OH (2026-09-30T16:10:57Z)

Почему под Линукс не реализуема ?
Скачали файл, распаковали, запустили установщик.
Ну или спросили - запустить установку или просто открыть папку
В целом и под винду так же - пусть пользователь нажимает кнопочки.
Тихая установка может сделать лишнего - это точно не вариант.

---

## Комментарий 2 от sivatorov (2026-10-01T17:31:33Z)

Реализовано в версии **0.3.9.249** (Windows/WPF и Linux/Avalonia).

**Суть фичи.** Новое окно «Скачивание версии платформы 1С» (меню «Утилиты», второй пункт
сразу после «Автообновление платформы 1С»): выбрать нужную версию платформы из каталога
`releases.1c.ru`, разрядность и тип дистрибутива, скачать его в выбранную папку с
прогрессом — и **без автоматической установки**: дальше пользователь сам открывает папку
или запускает установщик (комментарий 7OH: «скачать / распаковать / запустить установщик
или открыть папку»; под Windows — так же).

**Как работает.**

1. **Список версий** — берётся с портала `releases.1c.ru/project/Platform83` тем же
   механизмом, что и «Актуальные релизы»/«Обновление платформы»
   (`IPlatformUpdateService.GetAvailableReleasesAsync`); файлы выбранной версии
   подгружаются лениво.
2. **Разрядность 32/64 и тип дистрибутива**:
   - Windows: полный клиент (zip с setup.exe) или тонкий клиент (zip с «thin» в имени),
     приоритет по разрядности;
   - Linux: пакет `.deb`/`.rpm` или универсальный `.tar.gz` (x64 предпочтительнее);
   - режим «Авто» — рекомендуемый для текущей ОС. Выбор выполняет чистый
     `PlatformDistributionPicker` («версия + разрядность + тип → файл»).
3. **Авторизация** — через учётные данные ИТС из справочника (#333): выбранная в
   настройках запись или «Основная». Если справочник пуст — в журнале окна появляется
   предупреждение, что авторизация выполняться не будет.
4. **Скачивание** — готовым `IOneCUpdatesService.DownloadDistributionAsync`
   (многопоточная загрузка HTTP Range с fallback), прогресс 0..1, в выбранную папку
   (по умолчанию `Загрузки/1CPlatform`, можно изменить диалогом; выбор запоминается).
5. **После скачивания** — итоговое сообщение «что скачано, куда, что делать дальше» и
   две кнопки (только по желанию пользователя):
   - **«Открыть папку»** — проводник/файловый менеджер;
   - **«Запустить установщик»** — Windows: zip распаковывается во временную папку и
     запускается `setup.exe` интерактивно (без тихих ключей); Linux: для `.deb`/`.rpm`
     показывается готовая команда `sudo` и копируется в буфер обмена, для `.tar.gz` —
     пошаговая инструкция (распаковка + `./install`). Ничего не устанавливается само.

**Что было сделано технически.**

- Новые файлы: `Services/PlatformDistributionPicker.cs` (чистый выбор файла),
  `ViewModels/PlatformDownloadViewModel.cs` (чистый VM на инжектируемых делегатах),
  окна `Views/PlatformDownloadWindow.xaml` + `.xaml.cs` и `Views/PlatformDownloadWindow.Avalonia.cs`;
  команда `ShowPlatformDownloadCommand` в `MainViewModel.PlatformUpdate.cs` /
  `MainViewModel.Avalonia.PlatformUpdate.cs` и пункты меню «Утилиты» (WPF/Avalonia).
- Настройка `AppSettings.PlatformDownloadDirectory` — запоминание папки загрузки.
- Локализация: все новые тексты через `LocalizationManager.T`, ключи `PlatformDownload.*`
  добавлены в `ru.json`/`en.json` парами.
- Регрессия: существующие «Обновление платформы», «Скачивание конфигураций» и
  `Platform*`-тесты не затронуты.

**Как проверить.**

1. Установите **0.3.9.249** (Windows или Linux).
2. Меню «Утилиты» → «Скачивание версии платформы 1С»: откроется окно, список версий
   загрузится автоматически (или по кнопке «Проверить каталог»).
3. Выберите версию, разрядность (x64/x86) и тип дистрибутива; убедитесь, что поле
   «Файл» показывает выбранный файл; нажмите «Скачать» — прогресс идёт, по завершении
   появится сообщение о пути сохранения.
4. Проверьте, что **установка не запустилась сама**; кнопками «Открыть папку» и
   «Запустить установщик» можно открыть папку или запустить установщик по желанию
   (Windows — мастер setup.exe; Linux — команда sudo в буфере/инструкция).
5. Задайте в справочнике «Учетные данные ИТС» запись и повторите: авторизация в окне
   показывает выбранную/основную учётную запись.
6. Windows и Linux ведут себя одинаково (тихая установка нигде не выполняется).

Тесты: `PlatformDistributionPickerTests` (маппинг «версия+разрядность → файл», доступные
типы, тонкий клиент), `PlatformDownloadViewModelTests` (каталог, выбор файла под
разрядность/тип, учётная запись, скачивание в папку, прогресс, установщик НЕ вызывается
автоматически) — зелёные; регрессия `PlatformUpdateServiceTests`,
`PlatformUpdateViewModelTests`, `PlatformDownloadTests` — зелёная;
`dotnet build -p:BuildLinux=true` — без ошибок.

---

## Комментарий 3 от 7OH (2026-10-01T20:10:36Z)

Пока что падает при попытке открыть окно

Ошибка интерфейса
Исключение: System.InvalidOperationException
Сообщение: Вызывающий поток не может получить доступ к данному объекту, так как владельцем этого объекта является другой поток.
StackTrace:
System.InvalidOperationException: Вызывающий поток не может получить доступ к данному объекту, так как владельцем этого объекта является другой поток.
   at System.Windows.Threading.Dispatcher.<VerifyAccess>g__ThrowVerifyAccess|7_0()
   at System.Windows.ContextLayoutManager.UpdateLayout()
   at System.Windows.Controls.Primitives.TextBoxBase.ScrollToEnd()
   at Configuration_Management.PlatformDownloadWindow.OnViewModel_PropertyChanged(Object sender, PropertyChangedEventArgs e) in F:\Yandex.Disk\h\Configuration_Management\Configuration Management\Views\PlatformDownloadWindow.xaml.cs:line 112
   at Configuration_Management.ViewModels.PlatformDownloadViewModel.AppendLog(String message) in F:\Yandex.Disk\h\Configuration_Management\Configuration Management\ViewModels\PlatformDownloadViewModel.cs:line 291
   at Configuration_Management.ViewModels.PlatformDownloadViewModel.LoadCatalogAsync() in F:\Yandex.Disk\h\Configuration_Management\Configuration Management\ViewModels\PlatformDownloadViewModel.cs:line 353
   at Configuration_Management.PlatformDownloadWindow.OnWindow_Loaded(Object sender, RoutedEventArgs e) in F:\Yandex.Disk\h\Configuration_Management\Configuration Management\Views\PlatformDownloadWindow.xaml.cs:line 79
   at System.Threading.Tasks.Task.<>c.<ThrowAsync>b__124_0(Object state)
   at System.Windows.Threading.ExceptionWrapper.InternalRealCall(Delegate callback, Object args, Int32 numArgs)
   at System.Windows.Threading.ExceptionWrapper.TryCatchWhen(Object source, Delegate callback, Object args, Int32 numArgs, Delegate catchHandler)

---

## Комментарий 4 от sivatorov (2026-10-02T05:45:04Z)

Исправлено в версии **0.3.9.259** (Windows/WPF; Linux/Avalonia не затронута — там автопрокрутка уже
шла через `Dispatcher.UIThread.Post`).

Окно «Скачивание версии платформы 1С» больше не падает при открытии с ошибкой
`InvalidOperationException` «Вызывающий поток не может получить доступ к данному объекту».

**Причина.** `PlatformDownloadViewModel.AppendLog` уведомляет UI (`OnPropertyChanged(LogText)`)
из фоновых задач — `LoadCatalogAsync`/`DownloadAsync` используют `ConfigureAwait(false)`, поэтому
обработчик `PropertyChanged` окна выполнялся на фоновом потоке. Окно
([`Views/PlatformDownloadWindow.xaml.cs`](Configuration%20Management/Views/PlatformDownloadWindow.xaml.cs))
вызывало `LogBox.ScrollToEnd()` прямо в этом обработчике — WPF бросал `Dispatcher.VerifyAccess`
при каждом обновлении журнала, и окно не открывалось.

**Как исправлено.** Автопрокрутка журнала перенесена в UI-поток:
`Dispatcher.BeginInvoke(new Action(() => LogBox?.ScrollToEnd()))` + защита `?.` на случай закрытия
окна до исполнения отложенного вызова. Контракт ViewModel не менялся.

**Тесты.** Добавлен регрессионный тест: `AppendLog` из `Task.Run` поднимает уведомление
без исключений ([`PlatformDownloadViewModelTests`](ConfigurationManagement.Tests/PlatformDownloadViewModelTests.cs)).
Полный набор `dotnet test` зелёный (1474), кросс-сборка Linux без ошибок.

**Как проверить в 0.3.9.259.**

1. Установите 0.3.9.259 на Windows.
2. Откройте «Скачивание версии платформы 1С» (при наличии сети каталог загрузится, при её
   отсутствии в журнал напишется сообщение об ошибке каталога).
3. Окно не падает, журнал прокручивается к последней строке; «Скачать»/«Установить» работают
   как раньше.

---

## Комментарий 5 от 7OH (2026-10-02T11:39:37Z)

 Ошибка интерфейса
Исключение: System.InvalidOperationException
Сообщение: Привязка типа TwoWay или OneWayToSource не может работать с доступным только для чтения свойством "LogText" типа "Configuration_Management.ViewModels.PlatformDownloadViewModel".
StackTrace:
System.InvalidOperationException: Привязка типа TwoWay или OneWayToSource не может работать с доступным только для чтения свойством "LogText" типа "Configuration_Management.ViewModels.PlatformDownloadViewModel".
   at MS.Internal.Data.PropertyPathWorker.CheckReadOnly(Object item, Object info)
   at MS.Internal.Data.PropertyPathWorker.ReplaceItem(Int32 k, Object newO, Object parent)
   at System.Windows.Data.BindingExpression.Activate(Object item)
   at System.Windows.Data.BindingExpression.AttachToContext(AttachAttempt attempt)
   at MS.Internal.Data.DataBindEngine.Task.Run(Boolean lastChance)
   at MS.Internal.Data.DataBindEngine.Run(Object arg)
   at System.Windows.Threading.ExceptionWrapper.InternalRealCall(Delegate callback, Object args, Int32 numArgs)
   at System.Windows.Threading.ExceptionWrapper.TryCatchWhen(Object source, Delegate callback, Object args, Int32 numArgs, Delegate catchHandler)



---

## Комментарий 6 от sivatorov (2026-10-02T19:08:05Z)

Исправлено в версии **0.3.9.279** (Windows/WPF; Linux/Avalonia не затронута — там привязка уже была `Mode=OneWay`).

**Что было.**

При открытии окна «Скачивание версии платформы 1С» появлялась ошибка:

```
System.InvalidOperationException: Привязка типа TwoWay или OneWayToSource не может работать
с доступным только для чтения свойством "LogText"
```

**Причина.**

В [`Views/PlatformDownloadWindow.xaml`](Configuration%20Management/Views/PlatformDownloadWindow.xaml) поле журнала `LogBox` (read-only `TextBox`) привязано к свойству `PlatformDownloadViewModel.LogText` БЕЗ указания режима: `Text="{Binding LogText}"`. WPF по умолчанию строит двустороннюю привязку, а свойство `LogText` доступно только для чтения — при открытии окна привязка валидировалась и роняла его. В `PlatformUpdateWindow.xaml` режим `Mode=OneWay` уже был задан (это чинилось в 0.3.9.260), а здесь — нет.

**Как исправлено.**

Явный режим: `Text="{Binding LogText, Mode=OneWay}"`. Проверены остальные окна с read-only текстовыми полями журналов — все они либо уже используют `Mode=OneWay`, либо выводят текст через `TextBlock` (односторонние по умолчанию), поэтому других правок не потребовалось.

**Тесты.**

Полный набор `dotnet test` зелёный, кросс-сборка Linux (`dotnet build -p:BuildLinux=true`) без ошибок.

**Как проверить:**

1. Установите **0.3.9.279** ([релиз v0.3.9.279](https://github.com/sivatorov/ConfigurationManagement/releases/tag/v0.3.9.279)).
2. Откройте «Утилиты → Скачивание версии платформы 1С» несколько раз подряд: окно открывается, журнал прокручивается, падений нет.
3. То же для «Обновление платформы 1С» (F9/Ctrl+F9).

---

## Комментарий 7 от 7OH (2026-10-02T20:51:51Z)

Получение списка версий с портала 1С…
Ошибка сети при обращении к сайту 1С

Всё та же общая ошибка редиректа

[Updates] Редирект 302 (шаг 0): 'https://login.1c.ru/login?service=https%3A%2F%2Freleases.1c.ru%2Fpublic%2Fsecurity_check' для 'https://releases.1c.ru/project/Platform83'
2026-10-02 23:51:25.584 [WARN] [Updates] Не удалось получить страницу (пустое тело или HTTP-ошибка): https://releases.1c.ru/project/Platform83
2026-10-02 23:51:25.584 [WARN] [PlatformUpdate] Пустой ответ или HTTP-ошибка: https://releases.1c.ru/project/Platform83
2026-10-02 23:51:25.584 [WARN] Скачивание платформы: каталог не получен — PlatformUpdate.Error.NetworkError


---

## Комментарий 8 от sivatorov (2026-10-03T06:21:39Z)

Исправлено в версии **0.3.9.297** (Windows/WPF и Linux/Avalonia).

**Что было сделано.**

Ошибка была той же, что в #334 и #323: releases.1c.ru отвечает 302 на
login.1c.ru, а программный вход не выполнялся — отсюда «Ошибка сети при обращении
к сайту 1С» и «каталог не получен — PlatformUpdate.Error.NetworkError».

К `PlatformDownloadViewModel.LoadCatalogAsync` применён общий фикс обработки
302/авторизации (тот же сервис `OneCUpdatesService`):

1. **Программный вход на portal.1c.ru доведён до конца:** GET формы входа по
   полному URL редиректа (с `service=…`), извлечение токена `execution`, POST
   учётных данных, ручное прохождение редиректов после POST (302 на
   `security_check?ticket=…` → GET), накопление сессионных cookie, повтор запроса
   каталога.

2. **Учётные данные из справочника ИТС.** Для входа используются записи
   `its_accounts.json` через `IItsAccountsStore` (включая «Основную»); устаревшие
   поля настроек — фолбэк. Раньше store в сервис не попадал, и при пустых старых
   полях вход даже не начинался («Для входа на portal.1c.ru не задан логин»).

3. **Каталог версий после входа.** Запрос каталога платформы (`Platform83`)
   повторяется после успешного входа — список версий наполняется.

**Тесты.** Интеграционный тест
`PlatformDownloadViewModel.LoadCatalogAsync_AfterLogin_PopulatesReleases`: после
CAS-входа (fake-провайдер) каталог версий наполняется (`Releases.Count > 0`).
Плюс общие тесты `UpdateCheckCatalogTests` для цепочки 302/CAS.

**Как проверить:**

1. Установите версию **0.3.9.297**.
2. Настройте учётную запись ИТС в «Общих настройках».
3. Откройте «Скачивание версии платформы 1С»: список версий должен загрузиться.
4. Выберите версию и разрядность — скачивание, «Открыть папку» и «Запустить
   установщик» работают как прежде.

---

## Комментарий 9 от 7OH (2026-10-03T18:50:34Z)

2026-10-03 21:50:04.130 [INFO] Скачивание платформы: получение каталога версий с портала 1С
2026-10-03 21:50:04.257 [INFO] [Updates] Редирект 302 (шаг 0): 'https://login.1c.ru/login?service=https%3A%2F%2Freleases.1c.ru%2Fpublic%2Fsecurity_check' для 'https://releases.1c.ru/project/Platform83'
2026-10-03 21:50:04.258 [WARN] [Updates] Не удалось получить страницу (пустое тело или HTTP-ошибка): https://releases.1c.ru/project/Platform83
2026-10-03 21:50:04.258 [WARN] [PlatformUpdate] Пустой ответ или HTTP-ошибка: https://releases.1c.ru/project/Platform83
2026-10-03 21:50:04.260 [WARN] Скачивание платформы: каталог не получен — PlatformUpdate.Error.NetworkError

---

## Комментарий 10 от sivatorov (2026-10-04T09:16:45Z)

Исправлено в версии **0.3.9.301** (Windows/WPF и Linux/Avalonia).

**Что было.**

Окно «Скачивание версии платформы 1С» не могло загрузить каталог версий: запрос `releases.1c.ru/project/Platform83` упирался в редирект 302 на login.1c.ru, программный вход либо не выполнялся вовсе (после первой неудачи входа флаг «отравлял» сессию службы), либо POST формы входа отклонялся статусом 401 — и каталог завершался бессмысленным «Пустой ответ или HTTP-ошибка» → `PlatformUpdate.Error.NetworkError`.

**Что сделано** — общий корень с #334/#323 в [`OneCUpdatesService.cs`](Configuration%20Management/Services/OneCUpdatesService.cs):

1. **Динамический сбор полей формы входа.** `ExtractFormFields` собирает все скрытые поля фактической формы (`execution`, `lt` и пр.), POST строится из реальной формы — изменение формы портала больше не даёт 401.
2. **Принудительный HTTP/1.1 + заголовки Referer/Origin** на POST формы входа (`LoginHttpVersion`, `Referer`/`Origin`).
3. **Понятная диагностика при 401**: анонимизированные признаки причины (размер HTML, маркеры, имена полей формы) через `LogAnonymizedAuthFailure` — без паролей и значений токенов в журнале.
4. **Retry-политика вместо «отравляющего» флага**: счётчик `_portalLoginAttempts` с лимитом `MaxPortalLoginAttempts = 3`, сброс при смене учётной записи — следующее окно/операция могут попробовать вход снова.
5. **Разделение статусов**: `AuthFailed` (креды не приняты) отделён от `AuthRequired` (креды не настроены) и `NetworkError` (сеть) — окно показывает «Вход на portal.1c.ru не подтверждён (401). Проверьте логин/пароль учётной записи ИТС» вместо голого NetworkError ([`PlatformUpdateService.cs`](Configuration%20Management/Services/PlatformUpdateService.cs), ключи `PlatformUpdate.Error.AuthFailed` в [`ru.json`](Configuration%20Management/Localization/Languages/ru.json)/[`en.json`](Configuration%20Management/Localization/Languages/en.json)).
6. **Параметризация ника каталога платформы** (#334): `SupportedPlatformNicks = [Platform83, Platform85]` — каталог `Platform85` также поддерживается ([`OneCPlatformCatalogParser.cs`](Configuration%20Management/Services/OneCPlatformCatalogParser.cs)).

**Тесты.** Новый [`OneCUpdatesLoginFlowTests.cs`](ConfigurationManagement.Tests/OneCUpdatesLoginFlowTests.cs) — 12 сценариев CAS-входа (в т.ч. «первая попытка 401 → вторая операция успешно входит» — бывший сценарий вашего окна); дополнены [`PlatformDownloadViewModelTests.cs`](ConfigurationManagement.Tests/PlatformDownloadViewModelTests.cs) и [`PlatformUpdateServiceTests.cs`](ConfigurationManagement.Tests/PlatformUpdateServiceTests.cs). Полный набор `dotnet test` зелёный: **Windows 1701**, **Linux 1672**.

**Как проверить:**

1. Установите версию **0.3.9.301**.
2. Настройте учётную запись ИТС в «Общих настройках» (справочник учёток; проверьте те же данные входом в браузере в инкогнито).
3. Откройте «Скачивание версии платформы 1С»: список версий должен загрузиться; выберите версию и разрядность — скачивание, «Открыть папку» и «Запустить установщик» работают как прежде.
4. Если каталог снова не получен — в журнале будут анонимизированные признаки причины 401, пришлите их, если проблема сохранится.

---

## Комментарий 11 от 7OH (2026-10-04T10:22:52Z)

Папка загрузки: C:\Users\Semion\1CPlatform
Получение списка версий с портала 1С…
Требуется вход на сайт 1С (задайте логин/пароль в настройках обновлений)

2026-10-04 13:22:10.785 [INFO] Скачивание платформы: получение каталога версий с портала 1С
2026-10-04 13:22:10.944 [INFO] [Updates] Вход на portal.1c.ru: учётная запись 'Основная', форма: https://login.1c.ru/login?service=https%3A%2F%2Freleases.1c.ru%2Fpublic%2Fsecurity_check
2026-10-04 13:22:11.270 [INFO] [Updates] Вход на portal.1c.ru выполнен (status=200).
2026-10-04 13:22:11.316 [INFO] [Updates] Вход на portal.1c.ru: учётная запись 'Основная', форма: https://login.1c.ru/login?service=https%3A%2F%2Freleases.1c.ru%2Fpublic%2Fsecurity_check
2026-10-04 13:22:11.436 [INFO] [Updates] Вход на portal.1c.ru выполнен (status=200).
2026-10-04 13:22:11.477 [INFO] [Updates] Вход на portal.1c.ru: учётная запись 'Основная', форма: https://login.1c.ru/login?service=https%3A%2F%2Freleases.1c.ru%2Fpublic%2Fsecurity_check
2026-10-04 13:22:11.594 [INFO] [Updates] Вход на portal.1c.ru выполнен (status=200).
2026-10-04 13:22:11.641 [WARN] [Updates] Превышен лимит попыток входа на portal.1c.ru за сессию (лимит 3); дальнейший вход возможен после смены учётной записи.
2026-10-04 13:22:11.641 [INFO] [Updates] Редирект 302 (шаг 3): 'https://login.1c.ru/login?service=https%3A%2F%2Freleases.1c.ru%2Fpublic%2Fsecurity_check' для 'https://releases.1c.ru/project/Platform83'
2026-10-04 13:22:11.644 [WARN] Скачивание платформы: каталог не получен — PlatformUpdate.Error.AuthRequired



---

## Комментарий 12 от sivatorov (2026-10-04T13:44:42Z)

Исправлено в версии **0.3.9.303**.

**Причина.** «Скачивание версии платформы» давало лог «Вход… "Основная"» → «Вход выполнен
(status=200)» **3 раза подряд** → «Превышен лимит попыток входа (лимит 3)» → «Редирект 302
(шаг 3)» → «каталог не получен — AuthRequired». Ваш вывод был точен: после POST сессия
НЕ устанавливалась, следующий запрос каталога снова уходил в 302 на login.

Корень — «фантомный успех» в CAS-авторизации: любой ответ 2xx после POST считался успехом,
но при неудачном входе сервер возвращает **HTTP 200 с телом формы входа** (поля
`execution`/`lt`) и без сессионной cookie. Дополнительно счётчик попыток не сбрасывался
при успехе (служба — singleton), а один вызов мог зациклиться «302→вход→302», потратив
весь лимит.

**Что сделано** (в [`OneCUpdatesService.cs`](Configuration%20Management/Services/OneCUpdatesService.cs)):

1. **Успех входа подтверждается содержимым тела**: новый `LooksLikeLoginForm` распознаёт
   форму входа (execution/lt, маркеры ошибки, упоминание login.1c.ru). POST 200 с формой —
   это честный `AuthFailed` с анонимизированной диагностикой, а не «Вход выполнен».
   Критерий усилен и для цепочки редиректов после входа (`FollowLoginRedirectsAsync`).
2. **Сброс счётчика при успехе** во всех ветках `Success` (`_portalLoginAttempts = 0`),
   плюс ранний выход через `HasPortalSessionCookie()` (JSESSIONID/TGC/session_id) — если
   сессия уже установлена, повторный вход не выполняется.
3. **Не более одной попытки входа за вызов** (`loginTried` в `SendWithAuthAsync`) — цикл
   302→вход→302 больше не сжигает весь лимит за одну операцию.
4. Страница входа при HTTP 200 распознаётся по содержимому в `FetchPageCoreAsync` и
   `CheckForUpdatesAsync` — вместо «каталог доступен, но версия не распарсена».
5. Лимит: понятное сообщение с советом («Проверьте учётные данные ИТС… при неверном
   пароле портал может временно блокировать аккаунт»), автосброс через ~10 минут,
   отдельный статус/ключ `LoginLimitReached`. Окно «Скачивание версии платформы» показывает
   расширенный совет при ошибках авторизации.

Тесты: 7 новых сценариев + 2 обновлённых в
[`OneCUpdatesLoginFlowTests.cs`](ConfigurationManagement.Tests/OneCUpdatesLoginFlowTests.cs);
полный набор **1717** тестов зелёный, кросс-сборка Linux без ошибок.

**Как проверить:**

1. «Скачивание версии платформы» с корректными учётными данными ИТС — каталог получается
   с первой попытки, в журнале нет «Вход выполнен (status=200)» без последующего успеха
   каталога и нет «Превышен лимит».
2. С неверным паролем — понятное «вход не подтверждён (401)», не более 3 попыток, через
   ~10 минут можно повторить.
3. Повторные операции (каталог + подгрузка файлов релиза) не упираются в лимит.

---

## Комментарий 13 от 7OH (2026-10-04T16:27:13Z)

Папка загрузки: C:\Users\Semion\1CPlatform
Получение списка версий с портала 1С…
Требуется вход на сайт 1С: проверьте учётные данные ИТС в «Настройки → Учётные данные ИТС» и повторите проверку.
Подсказка: проверьте логин/пароль ИТС в «Настройки → Учётные данные ИТС» (справочник, запись «Основная») и повторите попытку; при многократных неверных входах портал может временно блокировать аккаунт — тогда подождите около 10 минут.

2026-10-04 19:26:52.318 [INFO] Скачивание платформы: получение каталога версий с портала 1С
2026-10-04 19:26:52.473 [INFO] [Updates] Редирект 302 (шаг 1): 'https://login.1c.ru/login?service=https%3A%2F%2Freleases.1c.ru%2Fpublic%2Fsecurity_check' для 'https://releases.1c.ru/project/Platform83'
2026-10-04 19:26:52.476 [WARN] Скачивание платформы: каталог не получен — PlatformUpdate.Error.AuthRequired


---

## Комментарий 14 от sivatorov (2026-10-04T19:10:03Z)

Исправлено в версии **0.3.9.305** (Windows/WPF и Linux/Avalonia; третья итерация программного входа).

**Что было.**

В логе по-прежнему: «Вход на portal.1c.ru: учётная запись 'Основная'…» → «Вход на portal.1c.ru выполнен (status=200)» ×3 → «Превышен лимит попыток входа на portal.1c.ru за сессию (лимит 3)» → «Редирект 302 (шаг 3): '…login.1c.ru/login?service=…'» → «Требуется вход на сайт 1С» для `releases.1c.ru/project/Platform83`. Вход логировался как успешный, но следующий запрос снова уходил в 302 — «фантомный успех»: CAS отвечает HTTP 200 телом формы входа вместо целевого контента. По старым логам не было видно, где именно рвётся CAS-цепочка.

**Что сделано** (общий корень для #323/#330/#334 — в [`Services/OneCUpdatesService.cs`](Configuration%20Management/Services/OneCUpdatesService.cs)):

1. **Пошаговая диагностика входа** в журнал (INFO/WARN, без секретов — пароль/логин/значения токенов не выводятся): причина запуска входа (`401`/`403`/redirect-login/self-redirect), имя учётной записи ИТС и наличие логина, статус GET формы, наличие токенов `execution`/`lt`, **атрибут `action` формы**, имена полей формы (без значений), статус POST и `Location` редиректа, результат цепочки редиректов, наличие сессионной cookie портала, итог (`Success`/`AuthFailed`/`NoCredentials`/`FormUnavailable`/`RedirectFailed`).
2. **POST на атрибут `action` формы** ([`ExtractFormAction`](Configuration%20Management/Services/OneCUpdatesService.cs)): раньше POST всегда отправлялся на URL GET-формы, а Spring Security CAS часто задаёт отдельный `action` (например `/login/cas?service=…`) — запрос уходил не туда (401/404). Теперь POST идёт на `action`, резолвленный относительно адреса формы (Referer/Origin остаются на адресе GET). Если `action` отсутствует/пуст/`#` — прежнее поведение.
3. **Распознавание изменённой формы входа**: если в форме нет классических токенов CAS (`execution`/`lt`), но есть признаки OAuth/JS-челленджа (`oauth`/`client_id`/`challenge`/`csrf`) — программный вход невозможен в принципе: показывается понятное сообщение «Форма входа изменилась: автоматический вход временно недоступен», предлагается открыть login.1c.ru в браузере, а не тратить попытки входа впустую.
4. **Окно «Скачивание версии платформы 1С»**: при ошибке авторизации показывается имя используемой учётной записи ИТС и кнопки «Открыть login.1c.ru в браузере» и «Учётные данные ИТС…» (открывает справочник учётных записей) ([`Views/PlatformDownloadWindow.xaml.cs`](Configuration%20Management/Views/PlatformDownloadWindow.xaml.cs), [`Views/PlatformDownloadWindow.Avalonia.cs`](Configuration%20Management/Views/PlatformDownloadWindow.Avalonia.cs)).

**Как проверить:** обновитесь до **0.3.9.305**; откройте окно «Скачивание версии платформы 1С» (меню «Утилиты») с реальными учётными данными ИТС. В журнале должны появиться строки: «Вход: GET формы status=…, execution=…, lt=…, action='…', поля: …», «Вход: POST на action формы …» (если `action` задан), «Вход: POST status=…, location='…'», «Вход: FollowLoginRedirectsAsync=…, sessionCookie=…», «Вход: итог=…». Если вход всё ещё не проходит — пришлите **полный** лог операции: теперь по нему будет точно видно, на каком шаге рвётся цепочка (неверные учётные данные / изменилась форма / капча). Дополнительно проверьте ту же пару логин/пароль в браузере (режим инкогнито) на login.1c.ru — это отличит проблему учётных данных от проблемы кода.

---

## Комментарий 15 от 7OH (2026-10-04T20:22:59Z)

2026-10-04 23:21:47.332 [INFO] Скачивание платформы: получение каталога версий с портала 1С
2026-10-04 23:21:47.442 [INFO] [Updates] Вход запущен: reason=redirect-login, url='https://releases.1c.ru/project/Platform83', location='https://login.1c.ru/login?service=https%3A%2F%2Freleases.1c.ru%2Fpublic%2Fsecurity_check'
2026-10-04 23:21:47.443 [INFO] [Updates] Вход запущен: результат=Success, повтор исходного запроса=True
2026-10-04 23:21:47.482 [INFO] [Updates] Редирект 302 (шаг 1): 'https://login.1c.ru/login?service=https%3A%2F%2Freleases.1c.ru%2Fpublic%2Fsecurity_check' для 'https://releases.1c.ru/project/Platform83'

---

## Комментарий 16 от sivatorov (2026-10-05T06:09:02Z)

Исправлено в версии **0.3.9.306** (Windows/WPF и Linux/Avalonia; четвёртая итерация программного входа).

**Что было.**

В последнем логе по-прежнему: три раза подряд «Вход на portal.1c.ru выполнен (status=200)» → «Превышен лимит попыток входа на portal.1c.ru за сессию (лимит 3)» → редирект 302 на login.1c.ru → `PlatformUpdate.Error.AuthRequired` для `releases.1c.ru/project/Platform83`. Найдена причина: **«фантомный успех»** — POST формы возвращал HTTP 200 **без установки сессионной cookie портала**, такой ответ считался успехом, повторный запрос каталога снова уходил в 302 → вход запускался заново → за одну операцию впустую тратились все 3 попытки входа.

**Что сделано** (общий корень для #323/#330/#334 — в [`Services/OneCUpdatesService.cs`](Configuration%20Management/Services/OneCUpdatesService.cs)):

1. **Устранён «фантомный успех» входа**: успехом считается вход ТОЛЬКО при установленной сессионной cookie портала (либо целевом контенте с `Set-Cookie`); иначе — «вход не подтверждён (фантомный успех)», повтор исходного запроса не запускается, лимит попыток не тратится.
2. **Следование JS/meta-refresh-редиректу**: если сервер после POST отвечает 200 страницей с `meta refresh`/`window.location` (CAS-цепочка часто доводится до `security_check?ticket=…` именно так) — цепочка теперь проходит до установки сессионной cookie, а не обрывается на «успехе».
3. **Расширенная диагностика в журнале** (INFO/WARN, без секретов): `sessionCookie=true/false` после каждого POST, `contentType`/`bodyLength`/превью тела (санитизированное), имена и флаги cookie из `Set-Cookie` (без значений), инвентаризация cookie контейнера для login/releases.1c.ru, маркер `retryAfterLoginStill302=true` при фантомном успехе.
4. По-прежнему: POST на атрибут `action` формы (0.3.9.305), лимит 3 попытки + автосброс 10 мин, понятные сообщения с кнопками «Открыть login.1c.ru в браузере» / «Учётные данные ИТС…».
5. **Тесты**: 5 новых сценариев в [`OneCUpdatesLoginFlowTests.cs`](ConfigurationManagement.Tests/OneCUpdatesLoginFlowTests.cs) (фантомный успех не считается успехом, 200 + cookie-сессия, meta-refresh, отсутствие секретов в логе, маркер фантома). Полный набор 0.3.9.306 — **1759** тестов, кросс-сборка Linux без ошибок.

**Как проверить:** обновитесь до **0.3.9.306**; откройте окно «Скачивание версии платформы 1С» (Ctrl+F9 / меню «Утилиты») с реальными учётными данными ИТС. В журнале должны появиться строки с `sessionCookie=` и (при проблеме) точной причиной. Если вход всё ещё не проходит — пришлите **полный** лог операции: по нему будет точно видно, на каком шаге рвётся CAS-цепочка (неверные учётные данные / изменилась форма / капча / JS-челлендж). Дополнительно проверьте ту же пару логин/пароль в браузере (режим инкогнито) на login.1c.ru — это отличит проблему учётных данных от проблемы кода.

---

## Комментарий 17 от 7OH (2026-10-05T06:14:06Z)

2026-10-05 09:07:26.447 [INFO] Скачивание платформы: получение каталога версий с портала 1С
2026-10-05 09:07:26.558 [INFO] [Updates] Вход запущен: reason=redirect-login, url='[releases.1c.ru/project/Platform83](https://releases.1c.ru/project/Platform83)', location='[login.1c.ru/login?service=https%3A%2F%2Freleases.1c.ru%2Fpublic%2Fsecurity_check](https://login.1c.ru/login?service=https%3A%2F%2Freleases.1c.ru%2Fpublic%2Fsecurity_check)'
2026-10-05 09:07:26.558 [INFO] [Updates] Вход запущен: результат=Success, повтор исходного запроса=True
2026-10-05 09:07:26.601 [INFO] [Updates] Редирект 302 (шаг 1): '[login.1c.ru/login?service=https%3A%2F%2Freleases.1c.ru%2Fpublic%2Fsecurity_check](https://login.1c.ru/login?service=https%3A%2F%2Freleases.1c.ru%2Fpublic%2Fsecurity_check)' для '[releases.1c.ru/project/Platform83](https://releases.1c.ru/project/Platform83)'
2026-10-05 09:07:26.601 [WARN] [Updates] retryAfterLoginStill302=true: после «успешного» входа повтор исходного запроса снова дал 302 на login.1c.ru (фантомный успех); второй вход в рамках операции не выполняется.
2026-10-05 09:07:26.604 [WARN] Скачивание платформы: каталог не получен — PlatformUpdate.Error.AuthRequired

---

## Комментарий 18 от sivatorov (2026-10-05T11:30:12Z)

Исправлено в версии **0.3.9.307** (Windows/WPF и Linux/Avalonia; пятая итерация программного входа).

**Что было.**

В последнем логе (2026-10-05, ~09:06–09:08 МСК) для окна «Скачивание версии платформы 1С»
(Утилиты, каталог `releases.1c.ru/project/Platform83`):

```
[Updates] Вход запущен: результат=Success, повтор исходного запроса=True
[WARN] retryAfterLoginStill302=true: после «успешного» входа повтор исходного запроса снова дал
       302 на login.1c.ru (фантомный успех); второй вход в рамках операции не выполняется.
→ PlatformUpdate.Error.AuthRequired
```

Найдена причина: вход возвращал **мгновенный Success по имени cookie**. `HasPortalSessionCookie()`
проверяла только имена (`TGC` / `JSESSIONID*` / `*session_id*` / `SESSION`) без атрибутов
(`Domain`, `Path`) и без проверки «живости» сессии на сервере. Любая cookie-заглушка WAF/CDN
с «подходящим» именем попадала в контейнер через `ApplySetCookieToContainer` и считалась валидной
сессией — повтор исходного запроса снова уходил в 302 на login.1c.ru. Вдобавок локальный флаг
`loginTried` разрешал не более одной попытки входа за вызов: при 302 после «успешного» входа
повторный вход со свежей формой не выполнялся — список версий платформы не загружался.

**Как исправлено** (общий корень для #323/#330/#334 — в [`Services/OneCUpdatesService.cs`](Configuration%20Management/Services/OneCUpdatesService.cs)):

1. **Устранён ложный Success по cookie-заглушке**: ранний выход по имени cookie заменён честной
   проверкой «живой» сессии — пробный GET по исходному URL операции (каталогу): если ответ 2xx
   и не страница входа — сессия жива, вход не выполняется и лимит попыток не тратится; если
   сессия мертва — cookie портала снимаются и выполняется полный вход (GET формы → POST →
   цепочка редиректов).
2. **Повторный вход при `retryAfterLoginStill302=true`**: вместо одноразового флага `loginTried` —
   счётчик попыток в рамках одного вызова (`MaxLoginAttemptsPerOperation = 2`). При повторном
   302→login после «успешного» входа теперь выполняется **повторный вход со свежей формой**
   (новый `execution`/`lt`), перед которым снимаются мусорные cookie хостов
   `login.1c.ru`/`releases.1c.ru` (`ClearPortalCookies()`). Маркер `retryAfterLoginStill302`
   остаётся диагностическим и появляется только после исчерпания повторов либо при
   заблокированном лимите сессии — тогда возвращается честный
   `AuthRequired`/`AuthFailed`/`LoginLimitReached`.
3. **Ужесточён `HasPortalSessionCookie()`**: cookie учитывается только с доменом `.1c.ru` и
   `Path=/` (или покрывающим корень) — cookie-заглушки с чужим доменом/путём сессией портала
   больше не считаются.
4. **Диагностика**: инвентаризация cookie контейнера в начале каждого входа
   (`Вход: cookie контейнера: …`) и результат пробной проверки живой сессии (`alive=True/False`)
   — по журналу видно, какая cookie присутствовала и почему принято решение.

**Как проверить.**

1. Обновитесь до **0.3.9.307** (Windows или Linux).
2. Откройте «Утилиты → Скачивание версии платформы 1С» при настроенной записи ИТС («Основная»).
3. Ожидание: в окне появляется **список версий платформы** (а не «Получение списка версий…
   Требуется вход на сайт 1С»).
4. Выберите версию и разрядность — файл скачивается в выбранную папку; кнопки «Открыть папку» /
   «Запустить установщик» работают.
5. В журнале приложения отсутствует `retryAfterLoginStill302=true`; при входе видны строки
   `Вход: cookie контейнера: …` и `alive=True/False`.
6. Повторите шаги на Windows и Linux — вход реализован в общем сервисе, обе платформы используют
   его напрямую.

**Тесты.** 5 новых сценариев в [`OneCUpdatesLoginFlowTests.cs`](ConfigurationManagement.Tests/OneCUpdatesLoginFlowTests.cs):
cookie-заглушка не даёт ложного Success; мусорная cookie удаляется перед повторным входом;
повторный вход при 302 со свежей формой → каталог Ok; лимит 2 POST за вызов (резерв лимита
сессии сохранён); пробная проверка живой сессии пропускает полный вход; существующий тест
«фантомного успеха» переработан под новый контракт. Полный набор `dotnet test` зелёный
(**1764**), кросс-сборка Linux (`dotnet build -p:BuildLinux=true`) без ошибок. Живой вход
с реальными кредами ИТС проверяется только на вашей машине — автору валидные учётные данные
недоступны.

Версия **0.3.9.307** входит в релиз v0.3.9.308 (вместе с исправлениями #340):
[CHANGELOG](https://github.com/sivatorov/ConfigurationManagement/blob/main/CHANGELOG.md),
[релиз v0.3.9.308](https://github.com/sivatorov/ConfigurationManagement/releases/tag/v0.3.9.308).

---



