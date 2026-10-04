Исправлено в версии **0.3.9.301** (Windows/WPF и Linux/Avalonia).

**Что было.**

При «Проверке обновлений» (F9) для `releases.1c.ru/project/Accounting30` сервер отвечал **302 без заголовка Location** (поведение CAS Spring Security при отсутствии сессии) либо редиректил на login.1c.ru, а программный вход не выполнялся или отклонялся 401 — проверка завершалась голой технической ошибкой `HTTP 302 для '…' (requestUri=<тот же>)` / `Updates.AuthRequired` без объяснений.

**Что сделано** — общий корень с #334/#330 в [`OneCUpdatesService.cs`](Configuration%20Management/Services/OneCUpdatesService.cs):

1. **302 без Location трактуется как требование авторизации** и запускает программный вход на portal.1c.ru; если войти не удалось — проверка завершается **понятным сообщением** (см. п. 3), а не техническим «HTTP 302».
2. **Вход больше не «отравляет» сессию.** Вместо одноразового флага — счётчик `_portalLoginAttempts` с лимитом `MaxPortalLoginAttempts = 3` за сессию службы и сбросом при смене учётной записи: после неудачной проверки следующее нажатие F9 (или другое окно) может попробовать войти снова. Сам вход исправлен: **динамический сбор полей формы** (`execution`, `lt` и пр. — POST строится из фактической формы), **принудительный HTTP/1.1 + Referer/Origin**.
3. **Разделение статусов авторизации**: `AuthFailed` (учётные данные не приняты, 401) отделён от `AuthRequired` (креды не настроены) и `NetworkError` (сеть). Новый ключ `Updates.AuthFailed`: «Вход на portal.1c.ru не подтверждён (401). Проверьте логин/пароль учётной записи ИТС…» ([`ru.json`](Configuration%20Management/Localization/Languages/ru.json)/[`en.json`](Configuration%20Management/Localization/Languages/en.json)); при 401 в журнал пишутся только **анонимизированные признаки причины** (`LogAnonymizedAuthFailure`) — без паролей и токенов.

**Тесты.** Новый [`OneCUpdatesLoginFlowTests.cs`](ConfigurationManagement.Tests/OneCUpdatesLoginFlowTests.cs) — 12 сценариев: динамический сбор полей, 401 → `AuthFailed` (в т.ч. в `CheckForUpdatesAsync` — ключ `Updates.AuthFailed`), повторная попытка после неудачи, лимит попыток, сброс при смене учётной записи, отсутствие кредов → `AuthRequired`; дополнены [`PlatformUpdateServiceTests.cs`](ConfigurationManagement.Tests/PlatformUpdateServiceTests.cs) и [`PlatformUpdateViewModelTests.cs`](ConfigurationManagement.Tests/PlatformUpdateViewModelTests.cs). Полный набор `dotnet test` зелёный: **Windows 1701**, **Linux 1672**.

**Как проверить:**

1. Установите версию **0.3.9.301**.
2. Проверьте учётные данные ИТС входом в браузере в режиме инкогнито; при необходимости обновите их в «Общих настройках».
3. Нажмите F9 по базе «Бухгалтерия предприятия» (ник `Accounting30`): должна загрузиться таблица версий каталога, а не «HTTP 302 … requestUri=<тот же>».
4. Если после первой неудачной проверки нажать F9 ещё раз — вход будет предпринят повторно (счётчик попыток, не блокировка на всю сессию); при 401 в журнале будут анонимизированные признаки причины.