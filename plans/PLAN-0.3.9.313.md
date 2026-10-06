# PLAN 0.3.9.313 — Кластер A: #323 «Окно Проверка обновлений» (программный вход portal.1c.ru)

- Режим: **architect** — план; код НЕ изменяется до одобрения.
- Текущая версия: **0.3.9.312**. Целевая версия кластера: **0.3.9.313**.
- Общий корень с #330/#334 (автообновление/скачивание платформы): правки только в общем сервисе `OneCUpdatesService`, регрессия проверяется прогоном существующих тестов, отдельные комментарии в #330/#334 НЕ публикуются (последний комментарий там от sivatorov).

---

## 1. Диагноз (по фактическим данным)

### 1.1 Лог пользователя на 0.3.9.310 (комментарий 22, 2026-10-05T18:08Z)

```
[Updates] Вход: POST status=200, location='<нет>', sessionCookie=True
[Updates] Вход: POST Set-Cookie: __ddg8_; Domain=.1c.ru; Path=/ | __ddg10_; ... | __ddg9_; ...
[Updates] Вход: POST 2xx диагностика status=200, bodyLength=21690, title='Личные данные', ...
[WARN] Вход на portal.1c.ru не подтверждён (status=200: в теле форма входа).
       (body_len=21690, title='Личные данные', признаки: неверный логин/пароль, execution,
        поля формы: _eventId, anotherComputer, execution, geolocation, inviteCode, inviteType, rememberMe)
→ AuthFailed → «Редирект 302 (шаг 0)» → «Требуется вход на portal.1c.ru»
```

Ключевые факты:
1. POST вернул **HTTP 200 со страницей личного кабинета** (`<title>Личные данные</title>`) — вход ФАКТИЧЕСКИ успешен.
2. Код объявил AuthFailed: в POST-ветке `TryLoginPortalAsync` проверка `LooksLikeLoginForm(postBody)` идёт **раньше** `DetectPersonalAreaPage(postBody)` и сработала ложно.
3. Ложное срабатывание детектора: на странице кабинета нашлась **фраза отказа** (`HasAuthFailureTextMarker`, «Неверный логин»/«Неверные учётные данные» — вероятно, в JS-валидаторе встроенной формы смены аккаунта) плюс поле `execution` как поле формы → второй путь `LooksLikeLoginForm` (execution/lt + маркеры) вернул true. Фикс 0.3.9.310 сузил поиск «execution», но **не ограничил фразы отказа контекстом формы входа**.
4. В контейнере cookie после входа — только WAF-заглушки `__ddg*`; сессионная `SESSION` для `releases.1c.ru` старая и «мёртвая» (пробный GET → 302, alive=False). Свежая сессия для releases при этом варианте входа НЕ устанавливается — повторный запрос каталога снова уходит в 302.

### 1.2 Эталон: рабочий код пользователя на 1С (комментарий 23, 2026-10-05T21:06Z)

CAS-цепочка рабочего кода:
1. GET `releases.1c.ru/<ресурс>` → из `Set-Cookie` берутся `JSESSIONID`(или `SESSION`) **и `SERVERID`** → строка `Cookie: JSESSIONID;SERVERID`.
2. `Location` → GET `login.1c.ru/login?service=...` → из тела парсится `execution`, из `Set-Cookie` — `SESSION`.
3. POST **на полный URL формы с `service=`** (а НЕ на action формы), тело `inviteCode=&username=...&password=...&execution=...&_eventId=submit&geolocation=&submit=Войти&rememberMe=on`, заголовок `Cookie: SESSION`.
4. `Location` ответа → GET `releases.1c.ru/public/security_check?ticket=...` **с Cookie `JSESSIONID;SERVERID`** — на этом звене ставится сессионная cookie releases.

Расхождения текущей .NET-реализации с эталоном:
- **A.** POST уходит на `action` формы (`https://login.1c.ru/login`) **без параметра `service`** (эталон POSTит на полный URL GET-формы `login?service=https%3A%2F%2Freleases.1c.ru%2Fpublic%2Fsecurity_check`). Spring Security CAS может не выпускать билет для нужного service при POST без него.
- **B.** Cookie `SERVERID` (sticky-session балансировщика) в .NET-инвентаризации не видна; эталон явно передаёт её в запросы к releases.1c.ru. Есть риск, что запросы без SERVERID уходят на другой узел и «мёртвая SESSION» не принимается.
- **C.** Порядок детекторов в POST-ветке: кабинет должен распознаваться РАНЬШЕ формы входа.

---

## 2. Изменения

### 2.1 Детекторы (корень ложного AuthFailed)

Файл: [`Configuration Management/Services/OneCUpdatesService.cs`](../Configuration%20Management/Services/OneCUpdatesService.cs)

1. **Порядок интерпретации POST 2xx** в `TryLoginPortalAsync`: 
   `DetectPersonalAreaPage(postBody)` → `Success` (сейчас идёт вторым); затем `LooksLikeLoginForm(postBody)` → `AuthFailed`; затем `ExtractBodyRedirectUrl` → `FollowLoginRedirectsAsync`. Перенос блоков местами, поведение остальных веток не меняется.
2. **Сужение `LooksLikeLoginForm`**: во втором пути (токены `execution`/`lt` как поля формы) убрать `HasAuthFailureTextMarker(body)` из критериев — фраза отказа учитывается ТОЛЬКО вместе с полями `username`+`password` (первый путь). Остаются критерии: `action` содержит «login» ИЛИ `ContainsLoginFormMarker`. Это закрывает ложное срабатывание на кабинете, где есть execution-форма смены аккаунта и JS-подсказка «Неверный логин».
3. `DetectPersonalAreaPage` — расширить маркеры при необходимости («Учётные данные», «Пользователь» — оставить минимально, по фактическому логу достаточно «Личные данные»).

### 2.2 POST-адрес формы (расхождение с эталоном 1С)

4. **`ResolveFormPostUrl`**: если `action` формы пуст/`#`/относительный без своего пути (`/login`, `/login?…`) И у URL GET-формы есть параметр `service` — POST выполнять на **полный URL GET-формы с `service=`** (поведение эталона 1С), а не на action. Если `action` несёт собственный путь/параметры (например `/login/cas?service=…`) — использовать его (прежнее поведение). Диагностика: логировать итоговый `postUrl` и причину выбора.
5. Referer/Origin остаются на адресе GET-формы (как сейчас).

### 2.3 Sticky-cookie SERVERID (диагностика + мягкая поддержка)

6. Добавить имя `SERVERID` в диагностику: `DescribeContainerCookies`/инвентаризация уже выводят имена — добавить `SERVERID` в набор имён, которые считаются портальными в `IsPortalDomainCookie` (для HasPortalSessionCookie НЕ включать — сессией не является; только диагностика и гарантия, что cookie из `Set-Cookie` сохраняется через `ApplySetCookieToContainer`, что уже реализовано).
7. **Без живых кредов** принудительное клеение Cookie-строки (как в 1С) не тестируется — фиксируем как опциональную доработку «по результатам нового лога»: если в новом логе SERVERID появится в `Set-Cookie` первого GET, но повторный запрос каталога всё равно 302 — тогда переносить значения вручную в Cookie-заголовок запросов к releases.1c.ru.

### 2.4 Запрос пользователю (комментарий после релиза)

8. В комментарий включить: фикс детекторов + POST с service; просьбу прислать **полный** лог F9 (строки «Вход: …», «POST на …») и проверить ту же пару кредов в браузере в режиме инкогнито на login.1c.ru.

---

## 3. Тесты

Файл: [`ConfigurationManagement.Tests/OneCUpdatesLoginFlowTests.cs`](../ConfigurationManagement.Tests/OneCUpdatesLoginFlowTests.cs)

Новые сценарии:
1. `LooksLikeLoginForm_PersonalAreaWithExecutionAndJsFailureMarker_False` — страница «Личные данные» с `<form id="loginForm">`, полем `execution` и фразой «Неверный логин или пароль» в `<script>` → false.
2. `LooksLikeLoginForm_RealLoginFormWithFailureMarker_True` — реальная форма (username+password) с фразой отказа → true (не сломать первый путь).
3. `Post200PersonalArea_WithExecutionAndFailureMarker_Success` — регресс точного лога 7OH (POST 200, title «Личные данные», поля формы кабинета) → PortalLoginResult.Success, повтор исходного запроса.
4. `LoginPost_UsesFormUrlWithService_WhenActionIsPlainPath` — action `/login` + GET-форма с `service=` → POST идёт на полный URL с service.
5. `LoginPost_ActionWithOwnPath_StillPreferred` — action `/login/cas?service=…` → POST на action.
6. Обновить существующие: `LoginPost_UsesFormActionUrl`, `Post200PersonalArea_NotTreatedAsAuthFailed`, `LooksLikeLoginForm_DetectsRealForms_NotPersonalAreaOrPlainExecution` под новый порядок.

Регрессия: полный набор `dotnet test` зелёный; кросс-сборка Linux `dotnet build -p:BuildLinux=true`.

---

## 4. Файлы

| Файл | Изменение |
|---|---|
| [`Configuration Management/Services/OneCUpdatesService.cs`](../Configuration%20Management/Services/OneCUpdatesService.cs) | Порядок POST-ветки; сужение `LooksLikeLoginForm`; `ResolveFormPostUrl` (service); SERVERID в диагностике |
| [`ConfigurationManagement.Tests/OneCUpdatesLoginFlowTests.cs`](../ConfigurationManagement.Tests/OneCUpdatesLoginFlowTests.cs) | 5 новых + обновление существующих сценариев |
| [`Configuration Management/Configuration Management.csproj`](../Configuration%20Management/Configuration%20Management.csproj) | Версия → 0.3.9.313 (4 поля) |
| `CHANGELOG.md`, `README.md` | Запись о версии |

---

## 5. Риски

| Риск | Влияние | Митигация |
|---|---|---|
| Живой вход с реальными кредами автору недоступен | Критерий «каталог получен» — только пользователь | Юнит-тесты по фактическим логам; комментарий с чек-листом инкогнито |
| Портал изменит форму/введёт капчу/2FA | Программный вход невозможен | Уже есть `Updates.CaptchaRequired`, FormUnavailable, лимит попыток + автосброс |
| Фикс POST с service не влияет (сервер принимает и без него) | Проблема останется в звене security_check/cookie | Диагностика `Вход: POST на … (выбран …)` + SERVERID в инвентаризации; следующая итерация — ручное клеение Cookie-строки |
| Регресс статусных цепочек #334/#330 | Неверный статус в окнах платформы | Существующие тесты `PlatformUpdateServiceTests`/`PlatformUpdateViewModelTests` без правок |

---

## 6. Критерии приёмки

1. `dotnet test` зелёный (включая новые сценарии по логу 7OH); кросс-сборка Linux без ошибок.
2. По логу пользователя: `Вход: POST 2xx … title='Личные данные'` → `Вход на portal.1c.ru выполнен`, БЕЗ `не подтверждён (status=200: в теле форма входа)`.
3. В журнале виден выбранный POST-адрес (с service либо action) — по нему понятно поведение.
4. Релиз v0.3.9.313, комментарий в #323 после релиза (issue не закрывать; #330/#334 не комментировать).