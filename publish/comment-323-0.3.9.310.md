Исправлено в версии **0.3.9.310** (Windows/WPF и Linux/Avalonia; шестая итерация программного входа).

**Что было.**

В вашем логе на 0.3.9.308 (комментарий от 2026-10-05) после отправки формы входа сервер вернул
HTTP 200 со страницей **личного кабинета** — `<title>Личные данные</title>` — то есть вход фактически
был успешным. Но код ошибочно объявлял «вход не подтверждён (AuthFailed)»:

```
[Updates] Вход: POST status=200, location='<нет>', sessionCookie=True
[Updates] Вход: POST 2xx диагностика status=200, contentType='text/html; charset=UTF-8', bodyLength=21690,
          bodyPreview=' <title>Личные данные</title> …'
[WARN] Вход на portal.1c.ru не подтверждён (status=200: в теле форма входа).
→ AuthFailed / «Требуется вход на portal.1c.ru»
```

Причина — ложное срабатывание детектора формы входа: страница считалась «формой входа» при любом
поле `execution` (оно присутствует и на странице личного кабинета во встроенных формах), а маркеры
причины («неверный логин/пароль», «incorrect», «execution») искались подстрокой по всему HTML,
включая JS-скрипты и подсказки валидации.

**Что сделано** (общий корень #323/#334/#330 — в общем сервисе
[`OneCUpdatesService.cs`](https://github.com/sivatorov/ConfigurationManagement/blob/main/Configuration%20Management/Services/OneCUpdatesService.cs)):

1. **Уточнён детектор формы входа** ([`LooksLikeLoginForm`](https://github.com/sivatorov/ConfigurationManagement/blob/main/Configuration%20Management/Services/OneCUpdatesService.cs)):
   главный признак — поля ввода логина и пароля (`name="username"` + `name="password"`); скрытые токены
   CAS (`execution`/`lt`) учитываются только как поля формы и только вместе с маркером формы входа.
   Страница с полем `execution`, но без полей логина/пароля формой входа больше не считается.
2. **Страница личного кабинета = успешный вход**: добавлено распознавание
   ([`DetectPersonalAreaPage`](https://github.com/sivatorov/ConfigurationManagement/blob/main/Configuration%20Management/Services/OneCUpdatesService.cs):
   «Личные данные», «Личный кабинет», «Главная», «Профиль», «Мои данные»). В POST-ветке такая страница
   трактуется как успешный вход — счётчик попыток сбрасывается и выполняется повтор исходного запроса
   каталога.
3. **Сужены маркеры причины отказа** ([`DetectAuthFailureMarkers`](https://github.com/sivatorov/ConfigurationManagement/blob/main/Configuration%20Management/Services/OneCUpdatesService.cs)):
   «execution» ищется как поле формы (`name="execution"`), а не как любое вхождение в HTML; фразы
   «неверный логин/пароль» — только точные фразы отказа. JS-подсказки личного кабинета больше не дают
   ложных признаков.
4. **Новый признак капчи**: если портал запросил подтверждение, показывается понятное сообщение
   с советом выполнить вход в браузере на login.1c.ru (`Updates.CaptchaRequired`; ключ добавлен в
   [`ru.json`](https://github.com/sivatorov/ConfigurationManagement/blob/main/Configuration%20Management/Localization/Languages/ru.json)
   / [`en.json`](https://github.com/sivatorov/ConfigurationManagement/blob/main/Configuration%20Management/Localization/Languages/en.json)).
5. **Диагностика**: в журнал POST 2xx добавлен `<title>` страницы ответа и домены сессионных cookie —
   по логу сразу видно «Личные данные» vs «Вход».

**Как проверить.**

1. Установите **0.3.9.310** (Windows или Linux) и повторите «Проверку обновлений» (F9) по базе
   с типовой конфигурацией (например «Бухгалтерия предприятия» → `Accounting30`).
2. Ожидание: в окне «Последняя версия» и кликабельная ссылка каталога; в журнале — строка
   `Вход на portal.1c.ru выполнен`, БЕЗ `Вход на portal.1c.ru не подтверждён (status=200: в теле форма входа)`.
3. Если снова появится `AuthFailed` — теперь в логе будет виден `<title>` страницы ответа: это сразу
   покажет, вернул ли сервер форму входа или личный кабинет.
4. Если портал запросит капчу — приложение покажет сообщение `Updates.CaptchaRequired` с советом
   выполнить вход в браузере, а не техническую строку.
5. Повторите шаги на Windows и Linux — вход реализован в общем сервисе, обе платформы используют его напрямую.

**Тесты.** 7 новых сценариев в
[`OneCUpdatesLoginFlowTests.cs`](https://github.com/sivatorov/ConfigurationManagement/blob/main/ConfigurationManagement.Tests/OneCUpdatesLoginFlowTests.cs),
включая «POST 200 → страница „Личные данные" → Success (не AuthFailed)» и цепочку из вашего лога
(повторный вход при повторном 302 после личного кабинета). Полный набор `dotnet test` зелёный
(**1787**, 0 не пройдено); кросс-сборка Linux (`dotnet build -p:BuildLinux=true`) без ошибок;
регрессия статусных цепочек #334/#330 пройдена без правок кода.

**Запрос к вам:** проверьте ту же пару логин/пароль учётной записи ИТС на **login.1c.ru в браузере
в режиме инкогнито** и сообщите результат — входит ли портал вручную, просит ли капчу, отклоняет ли
пароль. Это позволит отличить ошибку кода от неверного пароля/блокировки/капчи на стороне 1С.

Версия **0.3.9.310** входит в релиз v0.3.9.310:
[CHANGELOG](https://github.com/sivatorov/ConfigurationManagement/blob/main/CHANGELOG.md),
[релиз v0.3.9.310](https://github.com/sivatorov/ConfigurationManagement/releases/tag/v0.3.9.310).