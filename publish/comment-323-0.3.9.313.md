Исправлено в версии **0.3.9.313** (Windows/WPF и Linux/Avalonia; седьмая итерация программного входа).

**Что было.**

В вашем логе на 0.3.9.310 (комментарий от 2026-10-05) после отправки формы входа сервер вернул
HTTP 200 со страницей **личного кабинета** — `<title>Личные данные</title>` (bodyLength=21690) —
то есть вход фактически был успешным. Но код ошибочно объявлял «вход не подтверждён (AuthFailed)»:

```
[Updates] Вход: POST status=200, location='<нет>', sessionCookie=True
[Updates] Вход: POST 2xx диагностика status=200, ... bodyLength=21690, title='Личные данные', ...
[WARN] Вход на portal.1c.ru не подтверждён (status=200: в теле форма входа).
       (body_len=21690, title='Личные данные', признаки: неверный логин/пароль, execution,
        поля формы: _eventId, anotherComputer, execution, geolocation, inviteCode, inviteType, rememberMe)
→ AuthFailed / «Требуется вход на portal.1c.ru»
```

Причина — два фактора:
1. детектор формы входа проверялся РАНЬШЕ детектора личного кабинета: на странице кабинета есть
   `execution`-форма смены аккаунта и JS-подсказка «Неверный логин или пароль», из-за которых
   страница кабинета ошибочно считалась формой входа;
2. POST формы уходил на атрибут `action` (`/login`) БЕЗ параметра `service` исходного каталога —
   в вашем рабочем коде на 1С (комментарий 23) POST идёт на полный URL GET-формы с `service=`,
   и Spring Security CAS выпускает билет для нужного service только при его наличии в POST.

**Что сделано** (общий корень #323/#334/#330 — в общем сервисе
[`OneCUpdatesService.cs`](https://github.com/sivatorov/ConfigurationManagement/blob/main/Configuration%20Management/Services/OneCUpdatesService.cs)):

1. **Кабинет распознаётся раньше формы входа.** В POST-ветке страница личного кабинета
   ([`DetectPersonalAreaPage`](https://github.com/sivatorov/ConfigurationManagement/blob/main/Configuration%20Management/Services/OneCUpdatesService.cs)):
   «Личные данные» и др. проверяется ПЕРВОЙ и трактуется как успешный вход (сброс счётчика
   попыток, повтор исходного запроса каталога). Ложное AuthFailed на вашем логе устранено.
2. **Сужен детектор формы входа** ([`LooksLikeLoginForm`](https://github.com/sivatorov/ConfigurationManagement/blob/main/Configuration%20Management/Services/OneCUpdatesService.cs)):
   фразы отказа («Неверный логин или пароль» и т.п.) учитываются ТОЛЬКО вместе с полями
   `username`+`password` (главный признак формы входа). Наличие `execution` + JS-фразы на
   странице кабинета больше не делает её «формой входа».
3. **POST на полный URL GET-формы с `service=`** ([`ResolveFormPostUrl`](https://github.com/sivatorov/ConfigurationManagement/blob/main/Configuration%20Management/Services/OneCUpdatesService.cs)):
   если `action` формы не несёт собственного пути (`/login`, `/login?…`) и у адреса GET-формы есть
   параметр `service` — POST выполняется на полный URL GET-формы с `service=` (как в вашем коде на 1С);
   если `action` несёт собственный путь/параметры (`/login/cas?service=…`) — используется он
   (прежнее поведение). Итоговый адрес и причина выбора всегда видны в журнале:
   `Вход: POST на '<url>' (выбран: <причина>)`.
4. **Sticky-cookie SERVERID** (из вашего кода 1С — она передаётся в запросы к releases.1c.ru):
   теперь сохраняется из `Set-Cookie` и видна в инвентаризации cookie
   (`Вход: cookie контейнера: …`), хотя сессией портала не считается — по новому логу будет видно,
   выдаёт ли портал SERVERID и достаточно ли cookie для звена `security_check`.

**Как проверить.**

1. Установите **0.3.9.313** (Windows или Linux) и повторите «Проверку обновлений» (F9) по базе
   с типовой конфигурацией (например «Бухгалтерия предприятия» → `Accounting30`).
2. Ожидание: в окне «Последняя версия» и кликабельная ссылка каталога; в журнале — строка
   `Вход на portal.1c.ru выполнен` (POST 200, title='Личные данные'), БЕЗ
   `Вход на portal.1c.ru не подтверждён (status=200: в теле форма входа)`.
3. В журнале теперь видна строка `Вход: POST на '<адрес>' (выбран: <причина>)` — по ней понятно,
   куда именно ушёл POST (с service либо на action).
4. Если вход всё ещё не проходит — пришлите, пожалуйста, **полный** лог операции (строки
   `Вход: …`, `Вход: POST на …`, `Вход: cookie контейнера: …`, `Редирект …`), а также проверьте
   ту же пару логин/пароль учётной записи ИТС на **login.1c.ru в браузере в режиме инкогнито**
   и сообщите результат — входит ли портал вручную, просит ли капчу, отклоняет ли пароль.

**Тесты.** +6 новых сценариев в
[`OneCUpdatesLoginFlowTests.cs`](https://github.com/sivatorov/ConfigurationManagement/blob/main/ConfigurationManagement.Tests/OneCUpdatesLoginFlowTests.cs),
включая регресс ТОЧНОГО вашего лога (POST 200 «Личные данные» + execution + JS-фраза → Success,
повтор исходного запроса), POST на полный URL GET-формы с `service=` при `action="/login"` и
приоритет action с собственным путём. Полный набор `dotnet test` зелёный (**1809**, 0 не пройдено);
кросс-сборка Linux (`dotnet build -p:BuildLinux=true`) без ошибок; регрессия статусных цепочек
#334/#330 пройдена без правок кода.

Версия **0.3.9.313** входит в релиз v0.3.9.313:
[CHANGELOG](https://github.com/sivatorov/ConfigurationManagement/blob/main/CHANGELOG.md),
[релиз v0.3.9.313](https://github.com/sivatorov/ConfigurationManagement/releases/tag/v0.3.9.313).