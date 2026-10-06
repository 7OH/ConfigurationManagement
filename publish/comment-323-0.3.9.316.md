Исправлено в версии **0.3.9.316** (Windows/WPF и Linux/Avalonia; восьмая итерация программного входа на portal.1c.ru).

**Что было.**

В вашем логе на 0.3.9.313 (07:58:39Z) вход POST кабинета («Личные данные») прошёл успешно **дважды**, SESSION для `login.1c.ru` выпущена, но для `releases.1c.ru` валидная сессия не устанавливалась: повтор исходного запроса снова давал 302 → `retryAfterLoginStill302=true` → `AuthRequired`. Не отрабатывало звено CAS **`security_check`**/билета — а в вашем рабочем коде на 1С (комментарий 23) именно это звено доводит сессию до конца: GET `releases.1c.ru/` (или `public/security_check`), JSESSIONID из Set-Cookie, затем исходный запрос с Cookie.

**Что сделано** (в общем сервисе [`OneCUpdatesService.cs`](https://github.com/sivatorov/ConfigurationManagement/blob/main/Configuration%20Management/Services/OneCUpdatesService.cs), польза сразу для #323/#330/#334):

1. **Новое звено CAS `security_check` после «кабинетного» успеха POST.** После POST кабинета «Личные данные» (POST 200) выполняется GET звена — URL берётся из параметра `service` GET-формы входа (в вашем логе — `https://releases.1c.ru/public/security_check`), fallback — корень `https://releases.1c.ru/` (эталон кода 1С).
2. **Редиректы звена проходятся вручную** (с защитой от петель), все **Set-Cookie `SESSION`/`JSESSIONID` для `releases.1c.ru` сохраняются** в общий cookie-контейнер.
3. **Пробная проверка живой сессии** после звена (`IsPortalSessionAliveAsync`): `alive=True` → выполняется повтор исходного запроса каталога **без** `retryAfterLoginStill302`. Если звено недоступно / не отдало cookie / сессия мертва — возвращается **честный `AuthFailed`** (без «фантомного успеха»).
4. **Диагностика** под флагом `CM_REDIRECT`: ожидаемые строки лога:
   ```
   [Updates] Вход: security_check 'https://releases.1c.ru/public/security_check' (источник: <service формы>)
   [Updates] Вход: security_check '<адрес>' => status=200, setCookie=SESSION; Domain=releases.1c.ru...
   [Updates] Вход: alive=True после security_check
   ```
5. **Регресс вашего лога 0.3.9.313 покрыт тестом**: POST 200 «Личные данные» → звено `security_check` → `alive=True` → повтор исходного запроса каталога без `retryAfterLoginStill302=true`.

**Как проверить.**

1. Установите **0.3.9.316** (Windows или Linux).
2. Включите `CM_REDIRECT` в `trace.json` (каталог профиля, рядом с `settings.json` — подробности в #347); перезапуск не нужен.
3. Нажмите **F9 по базе с типовой конфигурацией** (например «Зарплата и управление персоналом» → `HRM30`).
4. Ожидание: окно «Последняя версия» показывает актуальную версию, кликабельная ссылка каталога; в журнале — цепочка `Вход: security_check … => status=200, setCookie=SESSION; Domain=releases.1c.ru…` → `Вход: alive=True после security_check` → повтор исходного запроса, **без** `retryAfterLoginStill302=true`.
5. Пришлите, пожалуйста, полный лог операции (строки `Вход: …`, `Вход: security_check …`, `Вход: alive=…`, `Редирект …`) — для подтверждения на реальном портале.

**Тесты.** Новые сценарии в [`OneCUpdatesLoginFlowTests.cs`](https://github.com/sivatorov/ConfigurationManagement/blob/main/ConfigurationManagement.Tests/OneCUpdatesLoginFlowTests.cs): звено `security_check` — URL из `service` формы и fallback на корень releases.1c.ru; ручное следование редиректам звена; сохранение Set-Cookie `SESSION`/`JSESSIONID` для `releases.1c.ru`; `alive=True` после звена → повтор исходного запроса каталога без `retryAfterLoginStill302`; звено недоступно → честный `AuthFailed`; **регресс точного лога 0.3.9.313** (POST 200 «Личные данные» 2× → звено → повтор исходного запроса). Полный набор `dotnet test` зелёный (**1851**, 0 не пройдено); кросс-сборка Linux (`dotnet build -p:BuildLinux=true`) без ошибок; регрессия статусных цепочек #334/#330 пройдена без правок кода. Живой вход с реальными кредами ИТС проверяется только на вашей машине — автору валидные учётные данные недоступны.

Версия **0.3.9.316** — исправление вошло в релиз **v0.3.9.316** (в [CHANGELOG](https://github.com/sivatorov/ConfigurationManagement/blob/main/CHANGELOG.md) изменения отражены секцией 0.3.9.316): [релиз v0.3.9.316](https://github.com/sivatorov/ConfigurationManagement/releases/tag/v0.3.9.316).