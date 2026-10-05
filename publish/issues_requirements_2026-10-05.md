# Требования к исправлению открытых issues — 2026-10-05 (Задача 2 плана 0.3.9.307)

- Репозиторий: [sivatorov/ConfigurationManagement](https://github.com/sivatorov/ConfigurationManagement)
- Текущая версия программы: **0.3.9.306**. Следующая: **0.3.9.307** (кластер portal.1c.ru), затем **0.3.9.308** (#340).
- Источники: `issues_details.json` (69 комментариев), `issues_analysis.json`, `publish/issues_snapshot_2026-10-05.md`, REST-ответы `publish/issue_<N>.json` / `publish/_comments_<N>.json`, `CHANGELOG.md` (записи 0.3.9.300–306).
- Правило пользователя: последний комментарий не от автора репозитория (`sivatorov`) → требование формулируется по последним комментариям не от автора; комментариев нет → по описанию. Во всех 4 issue последний комментарий — от `7OH`, поэтому требование строится по последним комментариям.

---

## Кластеры общих корней

### Кластер A — вход на portal.1c.ru: #323, #330, #334

Единый симптом после релиза 0.3.9.306 во всех трёх issue (логи от 2026-10-05, ~09:06–09:08 МСК):

```
[Updates] Вход запущен: reason=redirect-login, url='<каталог>', location='login.1c.ru/login?service=…'
[Updates] Вход запущен: результат=Success, повтор исходного запроса=True
[Updates] Редирект 302 (шаг 1): 'login.1c.ru/login?service=…' для '<каталог>'
[WARN] [Updates] retryAfterLoginStill302=true: после «успешного» входа повтор исходного запроса снова дал 302 на login.1c.ru (фантомный успех); второй вход в рамках операции не выполняется.
→ AuthRequired (PlatformUpdate.Error.AuthRequired / «Требуется вход на portal.1c.ru»)
```

Выводы по коду ([`OneCUpdatesService.cs`](Configuration%20Management/Services/OneCUpdatesService.cs), `SendWithAuthAsync`):

1. **Детектор фантомного успеха (0.3.9.306) работает** — маркер `retryAfterLoginStill302` логируется корректно, лимит попыток не тратится.
2. **Но сам вход по-прежнему не даёт cookie, принимаемую releases.1c.ru**: POST формы входа завершается «успехом» по текущим критериям (появилась какая-то session-подобная cookie в контейнере либо пройдена JS/meta-refresh цепочка), однако повтор исходного запроса снова уходит в 302 на login.1c.ru.
3. **Второй вход в рамках операции не выполняется**: в цикле редиректов заведён локальный флаг `loginTried` (не более одной попытки входа за вызов — защита от сжигания лимита из 0.3.9.303). Для нового сценария это стало ограничением: при `retryAfterLoginStill302=true` нужно разрешить **повторную попытку входа со свежей формой (новый `execution`/`lt`)** в рамках той же операции, но с контролем числа повторов и без траты лимита сессии на «фантомные» циклы.
4. Остаются неопровергнутыми гипотезы PLAN-0.3.9.301: изменившаяся форма входа (поля/JS-челлендж), требования сервера к заголовкам (`Referer`/`Origin`/`User-Agent`), HTTP/2 vs HTTP/1.1, домен/атрибуты cookie (`Path`, `Secure`, SameSite), необходимость полной цепочки до `security_check?ticket=…`.

Общий корень = программная CAS-авторизация portal.1c.ru. Различия между issue — только точка входа:

| Issue | Окно / операция | Каталог |
|-------|-----------------|---------|
| #323 | «Проверка обновлений» (F9) | `releases.1c.ru/project/Accounting30` (конфигурация «Бухгалтерия предприятия») |
| #330 | «Скачивание версии платформы 1С» (Утилиты) | `releases.1c.ru/project/Platform83` |
| #334 | «Обновление платформы 1С» (Ctrl+F9) | `releases.1c.ru/project/Platform83` (+ просьба пользователя проверять Platform85) |

### Кластер B — снятие выделения после закрытия контекстного меню: #340

Отдельный корень (оконный стек WPF/Avalonia). Восьмая попытка (0.3.9.306) не помогла: пользователь сообщает «файл не появился (см. выше), текущая строка всё ещё исчезает»; последний комментарий — про согласование механизма трассировки через `trace.json` как файл-флаг. Две независимые части требования: (1) рабочая диагностика, (2) детерминированный фикс.

---

## История попыток 0.3.9.300–306 (что делали и почему не помогло)

### Кластер A (программный вход portal.1c.ru)

| Версия | Что сделано | Что осталось / почему не помогло |
|--------|-------------|----------------------------------|
| 0.3.9.301 | Динамический сбор полей формы (`ExtractFormFields`: `execution`, `lt` и пр.); HTTP/1.1 + `Referer`/`Origin` на POST; анонимизированная диагностика 401; retry-политика (лимит 3) вместо «отравляющего» флага; разделение `AuthFailed`/`AuthRequired`/`NetworkError`; параметризация ника `Platform83`/`Platform85` | Лог: «Вход выполнен (status=200)» ×3 → «Превышен лимит (3)» → `AuthRequired`. HTTP 200 без сессионной cookie трактовался как успех — **фантомный успех**, лимит сжигался впустую |
| 0.3.9.303 | Устранён фантомный успех: `LooksLikeLoginForm()` — 200 с телом формы входа = честный `AuthFailed`; сброс счётчика при успехе; одна попытка входа за вызов (`loginTried`); распознавание страницы входа при 200; кулдаун лимита 10 мин + `LoginLimitReached` | Лог стал «Редирект 302 (шаг 1) → `AuthRequired`» — вход либо не запускался, либо «Вход запущен: результат=Success» с повторным 302. По логу невозможно было понять, где рвётся CAS-цепочка |
| 0.3.9.305 | Пошаговая диагностика входа (причина, статусы GET/POST, имена полей формы, атрибут `action`, итог цепочки); **POST на атрибут `action` формы** (`ExtractFormAction`) вместо URL GET; распознавание OAuth/JS-челленджа (`FormUnavailable`) | Лог: «Вход запущен: результат=Success, повтор исходного запроса=True → Редирект 302 (шаг 1)» — вход «успешен», но повтор снова 302: сервер не принял cookie (фантомный успех не был окончательно отсечён) |
| 0.3.9.306 | Успех входа ТОЛЬКО при установленной сессионной cookie (`sessionCookie=true`) либо целевом контенте с `Set-Cookie`; следование JS/meta-refresh до `security_check?ticket=…`; расширенная диагностика (`contentType`/`bodyLength`/имена cookie/инвентаризация контейнера/маркер `retryAfterLoginStill302`) | Свежие логи 2026-10-05: маркер `retryAfterLoginStill302=true` — детектор сработал, но **сам вход всё ещё не приводит к валидной сессии**; второй вход в рамках операции запрещён (`loginTried=true`) → `AuthRequired` во всех трёх issue |

### Кластер B (снятие выделения, #340)

| Версия | Что сделано | Что осталось / почему не помогло |
|--------|-------------|----------------------------------|
| 0.3.9.277 | Применение выбора в момент `ContextMenu.Closed` (`TryApplyTreeClickAfterMenuClosed`, WPF) | Клик «проглатывался» попапом; выбор применялся к нестабильному состоянию |
| 0.3.9.291 | Устранено двойное применение выбора (штатная логика + отложенный повтор) | Эффект «через секунду строка пропадает» сохранился |
| 0.3.9.299 | Решение по данным (`BatchSelectionHelper.DecideAfterMenuCloseClick`) вместо переиспользуемого контейнера при `VirtualizationMode=Recycling`; подавление повторной доставки `MouseDown` | «Поведение не изменилось»; воспроизводится даже без мультивыделения (достаточно ПКМ + клик по другой строке) |
| 0.3.9.300 | Дедупликация клика по данным события (`IsSameClick`: время + позиция); клик, закрывший меню, не доходит до дерева; обе платформы | «Баг на месте — выделение пропадает» |
| 0.3.9.302 | Выбор применяется синхронно в `Closed`; «хвост» клика гасится по штампу времени события (`Environment.TickCount`); `SelectTreeRowByData` учитывает секцию («Закреплённые» vs список) | «Не помогло» — между «строка активна» и «выбор применён» оставалось окно, в которое вмешивались разметка/виртуализация |
| 0.3.9.304 | Стратегия «штатный выбор + однократный fallback + стабилизация `IsSelected`» (`EnsureSelectionStable`, до 3 срабатываний `LayoutUpdated`/~200 мс); снимок только для простого клика без модификаторов; Avalonia зеркально | «Баг на месте» — стабилизация в одном пути + короткое окно не покрывали отложенную переработку контейнеров |
| 0.3.9.305 | Диагностика `CM_MENUCLOSE_TRACE=1` → `%TEMP%\cm_menuclose_trace.log`; стабилизация во всех путях применения клика (до 10 проходов/1 с); сброс pending при деактивации — после фактического закрытия меню | **Файл лога не создался**: env-переменная в GUI-окружении Windows нетривиально выставляется, `%TEMP%` мог отличаться. Пользователь: «лог файл не найден ни на одном диске»; просьба: писать в `trace.json` рядом с настройками |
| 0.3.9.306 | [`MenuCloseTrace.cs`](Configuration%20Management/Services/MenuCloseTrace.cs): трассировка ВСЕГДА в `menuclose_trace.json` рядом с настройками (`PlatformPaths.AppDataDirectory`: Windows `%APPDATA%\ConfigurationManagement\`, Linux `~/.config/ConfigurationManagement/`; JSONL ~512 КБ; стартовая запись); «догоняющее» восстановление ~800 мс для нереализованного контейнера; стабилизация 15 проходов/1,5 с; grace-период деактивации ~300 мс | Пользователь: «Файл не появился (см. выше). Текущая строка всё ещё исчезает»; затем создал `trace.json` сам в расчёте на семантику файла-флага — «и туда ничего не записывается при закрытии меню». Замечание по коду: `WriteStartupIfNeeded` вызывается только внутри `Log()`, т.е. файл не создаётся, если за сессию ни разу не закрывалось контекстное меню; имя файла (`menuclose_trace.json`) не совпало с ожиданием пользователя (`trace.json` = флаг) |

---

## Требования и критерии проверки по каждому issue

### #323 — Окно «Проверка обновлений» (кластер A)

**Основание (правило «б»):** последний комментарий `7OH`, 2026-10-05T06:14:26Z — лог: «успешный» вход → повтор исходного запроса → снова 302 на login.1c.ru → `retryAfterLoginStill302=true: … второй вход в рамках операции не выполняется` → «Требуется вход на portal.1c.ru».

**Требование.** Проверка обновлений (F9) для конфигурации с ником на releases.1c.ru (например «Бухгалтерия предприятия» → `Accounting30`) должна получить каталог релизов через реальный программный вход на portal.1c.ru (валидная сессионная cookie/тикет CAS, принимаемый releases.1c.ru). При повторном 302 на login.1c.ru после «успешного» входа — выполнять повторную попытку входа со свежей формой (новый `execution`/`lt`) в рамках той же операции, с контролем числа повторов (не тратя лимит сессии на фантомные циклы); при исчерпании — раздельное честное сообщение `AuthFailed` (креды не приняты) / `AuthRequired` (креды не настроены) / `LoginLimitReached` (лимит, подождать ~10 мин) с кнопкой «Учётные данные ИТС…». Ранние пункты описания (модальность окна, обрезанная кнопка, кликабельный адрес) считаются закрытыми (0.3.9.158/246/268) и в объём не входят.

**Критерий проверки пользователем.**
1. Настроить учётную запись ИТС (справочник, запись «Основная»), F9 по базе с типовой конфигурацией.
2. В окне появляется «Последняя версия» и «Ссылка на каталог релизов» (кликабельна), а не «Требуется вход на portal.1c.ru».
3. В журнале приложения отсутствует `retryAfterLoginStill302=true`.
4. С неверным паролем ИТС — понятное сообщение «Вход на portal.1c.ru не подтверждён» (без технических деталей типа «HTTP 302»).

**Проверяемость:** логика — юнит-тестами ([`UpdateCheckCatalogTests.cs`](ConfigurationManagement.Tests/UpdateCheckCatalogTests.cs), [`OneCUpdatesLoginFlowTests.cs`](ConfigurationManagement.Tests/OneCUpdatesLoginFlowTests.cs), сценарии «401 → повторная попытка → успех» и «retryAfterLoginStill302 → повторный вход со свежей формой»); живой вход с реальными кредами ИТС — только пользователем.

---

### #330 — «Скачивание и установка нужной версии платформы 1С из стартера» (кластер A)

**Основание (правило «б»):** последний комментарий `7OH`, 2026-10-05T06:14:06Z — тот же лог для `Platform83`: «успешный» вход → 302 → `retryAfterLoginStill302=true` → `PlatformUpdate.Error.AuthRequired`.

**Требование.** Окно «Скачивание версии платформы 1С» (Утилиты) должно наполнять список версий с `releases.1c.ru/project/Platform83` после реального программного входа на portal.1c.ru. При `retryAfterLoginStill302=true` — повторная попытка входа со свежей формой в рамках операции (с контролем повторов), при неудаче — раздельное честное сообщение (`AuthFailed`/`AuthRequired`/`LoginLimitReached`). После получения каталога сохраняется прежняя функциональность: выбор версии/разрядности/типа дистрибутива (`PlatformDistributionPicker`), скачивание с прогрессом в выбранную папку, кнопки «Открыть папку»/«Запустить установщик». Падения окна при открытии (0.3.9.259/279) считаются закрытыми.

**Критерий проверки пользователем.**
1. Открыть «Утилиты → Скачивание версии платформы 1С» при настроенной записи ИТС.
2. В окне появляется список версий платформы (а не «Получение списка версий… Требуется вход на сайт 1С»).
3. Выбрать версию и разрядность — файл скачивается в выбранную папку; «Открыть папку»/«Запустить установщик» работают.
4. В журнале нет `retryAfterLoginStill302=true`.

**Проверяемость:** логика — юнит-тестами ([`PlatformDownloadViewModelTests.cs`](ConfigurationManagement.Tests/PlatformDownloadViewModelTests.cs), [`OneCUpdatesLoginFlowTests.cs`](ConfigurationManagement.Tests/OneCUpdatesLoginFlowTests.cs)); живой вход — пользователем.

---

### #334 — «Автообновление платформы» (кластер A)

**Основание (правило «б»):** последний комментарий `7OH`, 2026-10-05T06:13:51Z — тот же лог для `Platform83` с `retryAfterLoginStill302=true` → `PlatformUpdate.Error.AuthRequired`.

**Требование.** Автообновление платформы (окно «Обновление платформы 1С», Ctrl+F9) должно получать каталог версий `releases.1c.ru/project/Platform83` через реальный программный вход на portal.1c.ru. По замечанию пользователя (2026-10-03) должна существовать возможность проверки и каталога `Platform85` (ник параметризован с 0.3.9.301 — убедиться, что выбор доступен в интерфейсе). При `retryAfterLoginStill302=true` — повторная попытка входа со свежей формой в рамках операции, при неудаче — раздельное честное сообщение и совет по учётным данным ИТС (запись «Основная», возможная временная блокировка портала ~10 минут).

**Критерий проверки пользователем.**
1. «Утилиты → Автообновление платформы 1С» (Ctrl+F9) при настроенной записи ИТС.
2. Список версий платформы загружается, доступна актуальная версия для сравнения/скачивания.
3. В журнале окна нет `PlatformUpdate.Error.AuthRequired` и `retryAfterLoginStill302=true`.
4. Возможность проверить Platform85 (если она нужна пользователю).

**Проверяемость:** логика — юнит-тестами ([`OneCUpdatesLoginFlowTests.cs`](ConfigurationManagement.Tests/OneCUpdatesLoginFlowTests.cs), [`PlatformUpdateServiceTests.cs`](ConfigurationManagement.Tests/PlatformUpdateServiceTests.cs), [`PlatformUpdateViewModelTests.cs`](ConfigurationManagement.Tests/PlatformUpdateViewModelTests.cs)); живой вход — пользователем.

---

### #340 — «Снятие выделени после мультивыделения» (кластер B)

**Основание (правило «б»):** последний комментарий `7OH`, 2026-10-05T06:15:18Z: «(Идея была в файле trace.json видеть доступные переменные, по которым в логи будут писаться дополнительные отладочные информации…) На всякий случай создал файл сам — типа если он есть — то его наличие является флагом трассировки. Но и туда ничего не записывается при закрытии меню».

**Требование (две части).**
1. **Рабочая диагностика, согласованная с пользователем:** согласовать и задокументировать механизм — либо писать журнал в файл `trace.json` (имя по предложению пользователя) рядом с настройками приложения (Windows `%APPDATA%\ConfigurationManagement\`, Linux `~/.config/ConfigurationManagement/`, тот же каталог, что `settings.json`), либо реализовать семантику «наличие файла-флага `trace.json` → вести подробный журнал». Соглашение указать в комментарии к issue и в README. Журнал должен реально создаваться и наполняться при закрытии контекстного меню кликом по другой строке (после 0.3.9.306 файл `menuclose_trace.json` у пользователя не появился; важно, чтобы стартовая запись/записи появлялись без ручных действий либо строго по согласованному флагу).
2. **Детерминированный фикс снятия выделения:** сценарий «мультивыделение (или просто правый клик) → левый клик по другой строке → строка становится активной → через мгновение выделение пропадает» больше не воспроизводится. Фикс искать по данным из нового журнала (кандидаты: повторная доставка «хвоста» `MouseDown`, виртуализация контейнеров `Recycling`, дубли строк «Закреплённые»/обычный список, окно стабилизации `IsSelected`).

**Критерий проверки пользователем.**
1. Обновиться на новую версию; при необходимости создать/не создавать файл-флаг согласно задокументированному соглашению.
2. Повторить сценарий: выделить несколько строк (Ctrl/Shift) или просто вызвать контекстное меню правой кнопкой → кликнуть левой по другой строке.
3. **Диагностика:** файл `trace.json` (или согласованное имя) появляется рядом с настройками и содержит записи событий (стартовая запись + события клика/закрытия меню) — проверяется один раз.
4. **Фикс:** строка, по которой кликнули, остаётся активной (выделенной); выделение НЕ пропадает «через мгновение» после исчезновения меню; повторные воспроизведения стабильны (10+ раз).

**Проверяемость:** чистая логика — юнит-тестами ([`BatchSelectionHelperTests.cs`](ConfigurationManagement.Tests/BatchSelectionHelperTests.cs), [`MenuCloseTraceFormatTests.cs`](ConfigurationManagement.Tests/MenuCloseTraceFormatTests.cs)); поведение оконного стека (WPF и Avalonia) — только живая проверка пользователем; тесты не покрывают попапы/виртуализацию.

---

## Точки входа в код

### Кластер A (#323/#330/#334)

- [`Configuration Management/Services/OneCUpdatesService.cs`](Configuration%20Management/Services/OneCUpdatesService.cs) — `SendWithAuthAsync` (цикл редиректов, `loginTried`, маркер `retryAfterLoginStill302`), `TryLoginPortalAsync`, `ExtractFormFields`, `ExtractFormAction`, `FollowLoginRedirectsAsync`, `HasPortalSessionCookie`, `LooksLikeLoginForm`, `CanAttemptPortalLogin`/`_portalLoginAttempts`/`MaxPortalLoginAttempts`/`LoginLimitCooldown`, `LogAnonymizedAuthFailure`; параметризация ников `Platform83Nick`/`Platform85Nick`/`SupportedPlatformNicks`.
- [`Configuration Management/Services/PlatformUpdateService.cs`](Configuration%20Management/Services/PlatformUpdateService.cs) — маппинг статусов (`AuthFailed`/`AuthRequired`/`NetworkError`/`LoginLimitReached`).
- Окна (WPF + Avalonia): [`UpdateCheckWindow.xaml`](Configuration%20Management/Views/UpdateCheckWindow.xaml)/[`.xaml.cs`](Configuration%20Management/Views/UpdateCheckWindow.xaml.cs)/[`UpdateCheckWindow.Avalonia.cs`](Configuration%20Management/Views/UpdateCheckWindow.Avalonia.cs); [`PlatformUpdateWindow.xaml`](Configuration%20Management/Views/PlatformUpdateWindow.xaml)/[`.xaml.cs`](Configuration%20Management/Views/PlatformUpdateWindow.xaml.cs); [`PlatformDownloadWindow.xaml`](Configuration%20Management/Views/PlatformDownloadWindow.xaml)/[`.xaml.cs`](Configuration%20Management/Views/PlatformDownloadWindow.xaml.cs)/[`PlatformDownloadWindow.Avalonia.cs`](Configuration%20Management/Views/PlatformDownloadWindow.Avalonia.cs).
- ViewModel: [`PlatformUpdateViewModel.cs`](Configuration%20Management/ViewModels/PlatformUpdateViewModel.cs), [`PlatformDownloadViewModel.cs`](Configuration%20Management/ViewModels/PlatformDownloadViewModel.cs), ViewModel проверки обновлений.
- Локализация: [`ru.json`](Configuration%20Management/Localization/Languages/ru.json)/[`en.json`](Configuration%20Management/Localization/Languages/en.json) (ключи `Updates.*`, `PlatformUpdate.Error.*`, `PlatformUpdate.AuthAdvice`).
- Тесты: [`OneCUpdatesLoginFlowTests.cs`](ConfigurationManagement.Tests/OneCUpdatesLoginFlowTests.cs), [`UpdateCheckCatalogTests.cs`](ConfigurationManagement.Tests/UpdateCheckCatalogTests.cs), [`PlatformUpdateServiceTests.cs`](ConfigurationManagement.Tests/PlatformUpdateServiceTests.cs), [`PlatformUpdateViewModelTests.cs`](ConfigurationManagement.Tests/PlatformUpdateViewModelTests.cs), [`PlatformDownloadViewModelTests.cs`](ConfigurationManagement.Tests/PlatformDownloadViewModelTests.cs).

### Кластер B (#340)

- [`Configuration Management/Views/MainWindow.Hotkeys.cs`](Configuration%20Management/Views/MainWindow.Hotkeys.cs) — `OnContextMenuClosed`, `TryApplyTreeClickAfterMenuClosed`, снимок клика.
- [`Configuration Management/Views/MainWindow.Events.cs`](Configuration%20Management/Views/MainWindow.Events.cs) — `OnInfobaseTree_PreviewMouseLeftButtonDown`, повторная доставка «хвоста» клика, `EnsureSelectionStable`.
- [`Configuration Management/Views/MainWindow.Tree.cs`](Configuration%20Management/Views/MainWindow.Tree.cs) — `SelectTreeRowByData`, `SelectionMatchesTarget`, «догоняющее» восстановление.
- Avalonia-аналоги: `MainWindow.Avalonia.Events.cs`, `MainWindow.Avalonia.Hotkeys.cs`, `MainWindow.Avalonia.Tree.cs` (если есть), `LeveledTreeView.Avalonia.cs` (`FindRowForData` с учётом секции).
- [`Configuration Management/Services/BatchSelectionHelper.cs`](Configuration%20Management/Services/BatchSelectionHelper.cs) — `IsSameClick`, `DecideAfterMenuCloseClick`.
- [`Configuration Management/Services/MenuCloseTrace.cs`](Configuration%20Management/Services/MenuCloseTrace.cs) / [`MenuCloseTraceFormat.cs`](Configuration%20Management/Services/MenuCloseTraceFormat.cs) — файл трассировки (имя/флаг согласовать с пользователем).
- Тесты: [`BatchSelectionHelperTests.cs`](ConfigurationManagement.Tests/BatchSelectionHelperTests.cs), [`MenuCloseTraceFormatTests.cs`](ConfigurationManagement.Tests/MenuCloseTraceFormatTests.cs).

---

## Ограничения проверки

- **Кластер A**: юнит-тесты покрывают логику входной цепочки (формы, редиректы, cookie, статусы), но реальный вход на portal.1c.ru с живыми учётными данными ИТС проверяем только пользователем. Автору недоступны валидные креды ИТС для сквозной проверки.
- **Кластер B**: юнит-тестами покрывается чистая логика (`BatchSelectionHelper`, `MenuCloseTraceFormat`); поведение попапов контекстного меню, захват мыши и виртуализация контейнеров на оконных стеках WPF/Avalonia юнит-тестами не покрываются — обязательна ручная проверка пользователем на обеих платформах.