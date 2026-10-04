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