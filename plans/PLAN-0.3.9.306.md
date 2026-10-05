# PLAN 0.3.9.306 — #340, #323, #330, #334 (открытые issues; последний комментарий — от 7OH)

Дата: 2026-10-04. Текущая версия: **0.3.9.305** (выпущена). Следующая микро-версия: **0.3.9.306**.
Источник: комментарии 7OH от 2026-10-04 (11:21Z «Не помогло» по #340; 20:18Z/20:29Z — просьба про trace.json;
10:21–10:23Z — логи CAS по #334/#330/#323 с тройным «Вход выполнен (status=200)» → лимит → AuthRequired).
Двуплатформенный проект: Windows/WPF (`#if WINDOWS`, `*.xaml`/`*.xaml.cs`) и Linux/Avalonia (`#if LINUX`, `*.Avalonia.cs`).

**В скоупе:** исправления кода + юнит-тесты по #340 и #323/#330/#334; комментарии в issues — на этапе релиза;
issues НЕ закрываются. Каждый пункт плана — ОТДЕЛЬНАЯ новая задача (см. раздел 8 «Задачи и зависимости»).

---

## 1. Краткое резюме

| № | Issue | Суть | Что выяснено анализом | Направление 0.3.9.306 |
|---|-------|------|------------------------|----------------------|
| 1 | **#340** | Выделение пропадает после закрытия контекстного меню (7-я попытка; 0.3.9.277/291/299/300/302/304) | 7OH: «Не помогло». Диагностика `CM_MENUCLOSE_TRACE=1` → `%TEMP%\cm_menuclose_trace.log` НЕ создала файл (env не сработала) | Заменить env-диагностику на **trace.json рядом с settings.json** + доработка стабилизации выбора (обе платформы) |
| 2 | **#323/#330/#334** | Программный вход на portal.1c.ru не проходит (4-я итерация) | Лог 7OH: три подряд «Вход на portal.1c.ru выполнен (status=200)» → «Превышен лимит попыток» → `AuthRequired`. Это **«фантомный успех»**: POST 200, но сессионная cookie НЕ установлена, каталог снова 302 | Расширенная диагностика «фантомного успеха» + защитные фиксы (успех = только с сессионной cookie; следование meta-refresh/JS-редиректу в теле; не тратить лимит на «фантом») |

---

## 2. Issue #340 — «Снятие выделения после мультивыделения»

### 2.1 Текущее состояние кода (0.3.9.305)

- [`MenuCloseTrace.cs`](Configuration Management/Services/MenuCloseTrace.cs:13) — статический класс: env-гейт `CM_MENUCLOSE_TRACE=1` + `Path.GetTempPath()\cm_menuclose_trace.log`. **Корень проблемы диагностики**: переменная окружения не была выставлена/не прочиталась → файл не создан; путь в %TEMP% неудобен для пользователя.
- Точки вызова трассировки (общий список, WPF + Avalonia):
  - [`MainWindow.Hotkeys.cs:796`](Configuration Management/Views/MainWindow.Hotkeys.cs:796) — снимок (TryApplyTreeClickAfterMenuClosed);
  - [`MainWindow.Hotkeys.cs:818`](Configuration Management/Views/MainWindow.Hotkeys.cs:818) — контрольный дамп 500 мс;
  - [`MainWindow.Hotkeys.cs:840/855/866`](Configuration Management/Views/MainWindow.Hotkeys.cs:840) — fallback (ran=false / userReselected / ran=true);
  - [`MainWindow.Events.cs:657`](Configuration Management/Views/MainWindow.Events.cs:657) — MouseDown путь A/C; [`MainWindow.Events.cs:851`](Configuration Management/Views/MainWindow.Events.cs:851) — MouseUp;
  - [`MainWindow.Tree.cs:869/878/893`](Configuration Management/Views/MainWindow.Tree.cs:869) — EnsureSelectionStable;
  - [`MainWindow.xaml.cs:94`](Configuration Management/Views/MainWindow.xaml.cs:94) — Deactivated;
  - [`MainWindow.Avalonia.Events.cs:194/221/239/255/271/286/294/341/349/357`](Configuration Management/Views/MainWindow.Avalonia.Events.cs:194) — зеркальные точки;
  - [`MainWindow.Avalonia.cs:234`](Configuration Management/Views/MainWindow.Avalonia.cs:234) — Deactivated (Avalonia).
- Логика стабилизации: [`EnsureSelectionStable`](Configuration Management/Views/MainWindow.Tree.cs:852) (WPF) и зеркало в [`MainWindow.Avalonia.Events.cs`](Configuration Management/Views/MainWindow.Avalonia.Events.cs:320): до 10 проходов LayoutUpdated / 1000 мс; восстановление через `SelectTreeRowByData`; `SelectionMatchesTarget` требует реализованный контейнер ([`MainWindow.Tree.cs:908`](Configuration Management/Views/MainWindow.Tree.cs:908)). Fallback: [`ApplyMenuCloseFallback`](Configuration Management/Views/MainWindow.Hotkeys.cs:836). Деактивация: [`MainWindow.xaml.cs:79`](Configuration Management/Views/MainWindow.xaml.cs:79) — отложенный сброс при `_openContextMenus.Count == 0`.

### 2.2 Причина сбоя диагностики (подтверждена кодом)

`Enabled` вычисляется один раз при первой загрузке типа из `Environment.GetEnvironmentVariable`; файл пишется в `%TEMP%`. Обе точки отказа: (а) пользователь не смог/не выставил env-переменную (в GUI-окружении Windows это нетривиально); (б) путь `%TEMP%` у пользователя мог отличаться. Итог — «лог-файл не найден ни на одном диске».

### 2.3 Решение: trace.json рядом с настройками приложения

**2.3.1 Новый компонент диагностики вместо env-переменной.**

- Файл **`menuclose_trace.json`** в каталоге данных приложения [`PlatformPaths.AppDataDirectory`](Configuration Management/Services/PlatformPaths.cs:21) (Windows: `%APPDATA%\ConfigurationManagement\menuclose_trace.json`; Linux: `~/.config/ConfigurationManagement/menuclose_trace.json`) — РЯДОМ с `settings.json`, тот же каталог, который пользователь уже знает (там же лежат `its_accounts.json`, `custom_actions.json` и пр.).
- Механизм включения: **писать всегда** (событий мало — несколько записей на один клик), с ограничением размера: круговое усечение до ~512 КБ (при превышении оставляется хвост последних записей) — «работает точно и на будущее», не требует от пользователя никаких действий.
- Формат: **JSON Lines** (одна JSON-запись на строку, ключи латиницей): `{"ts":"2026-10-04T20:30:00.000+03:00","thread":12,"event":"TryApply","data":{...}}`. JSONL удобен для append и ротации; каждая строка — валидный JSON, читаемый и человеком, и скриптами. (Имя файла — `menuclose_trace.json`, расширение по просьбе пользователя; формат содержимого — JSONL.)
- Переработка [`MenuCloseTrace.cs`](Configuration Management/Services/MenuCloseTrace.cs:13):
  - удалить env-гейт и `%TEMP%`;
  - путь: `PlatformPaths.AppDataDirectory`; `Directory.CreateDirectory` при первой записи;
  - запись под lock с усечением при превышении лимита;
  - ошибки записи игнорируются (не влияют на приложение);
  - при первом обращении писать **startup-запись**: версия приложения (из `InformationalVersion`), платформа (WPF/Avalonia), ОС, время запуска — чтобы по логу всегда было ясно, какая сборка.
- Все существующие вызовы `MenuCloseTrace.Log(...)` **сохраняются без изменений** (сигнатура прежняя) — правки только внутри класса. Это касается WPF и Avalonia.
- Если для тестируемости понадобится чистая часть (построение записи, усечение) — вынести в `internal static` helper `MenuCloseTraceFormat` (отдельный файл `Services/MenuCloseTraceFormat.cs`), покрыть юнит-тестами.

**2.3.2 Дополнительные диагностические данные (в тех же точках).**

- В `TryApplyTreeClickAfterMenuClosed` (WPF, [`MainWindow.Hotkeys.cs:746`](Configuration Management/Views/MainWindow.Hotkeys.cs:746)) и Avalonia-зеркало добавить в запись: `IsVisible`, `IsActive` окна, `_openContextMenus.Count` — для проверки гипотезы S4 (деактивация).
- В `EnsureSelectionStable` (обе платформы) в каждой записи прохода — уже есть `passes/matches/containerRealized/action`; добавить `selectedItemId` (актуальный SelectedItem дерева) и `timeSinceStartMs`.

### 2.4 Доработка стабилизации выбора (сам баг)

Защитные фиксы, которые делаются БЕЗ ожидания лога (лог затем подтвердит/скорректирует):

- **F1. «Догоняющая» стабилизация для нереализованного контейнера.** Сейчас `SelectTreeRowByData` при отсутствии контейнера только ставит модель `_viewModel.SelectedInfobase = target` ([`MainWindow.Tree.cs:828`](Configuration Management/Views/MainWindow.Tree.cs:828)) — WPF TreeView не подсвечивает строку, пока контейнер не реализован, а подписка на LayoutUpdated может закончиться раньше переработки Recycling. Добавить: одноразовый `DispatcherTimer` на ~800 мс после завершения подписки LayoutUpdated (или подписку на `ItemContainerGenerator.StatusChanged` в WPF) — при реализации контейнера вызвать `ApplySelection` и проверить `IsSelected`. Зеркально в Avalonia (`LeveledTreeView`/`FindRowForData`).
- **F2. Расширить окно стабилизации.** Константы [`MainWindow.Tree.cs:858`](Configuration Management/Views/MainWindow.Tree.cs:858): `maxPasses` 10 → 15, `timeoutMs` 1000 → 1500 (и зеркально Avalonia). Отписка обязательна во всех выходах (как сейчас).
- **F3. Grace-период в Deactivated.** [`MainWindow.xaml.cs:89`](Configuration Management/Views/MainWindow.xaml.cs:89): сейчас сброс отложен на `DispatcherPriority.Background` и выполняется при `_openContextMenus.Count == 0` — но список меню может опустеть ДО повторной доставки клика, и сброс убьёт fallback. Заменить на: таймер ~300 мс с момента Deactivated; если окно снова стало активным (`Activated`) — таймер отменяется и состояние НЕ сбрасывается; сброс — только если окно реально осталось неактивным и меню закрыты. Зеркально Avalonia ([`MainWindow.Avalonia.cs:233`](Configuration Management/Views/MainWindow.Avalonia.cs:233)).
- **F4. Не снимать `_menuClosePendingApply` при MouseUp, если повторная доставка ещё возможна.** Сейчас MouseUp сбрасывает только снимок ([`MainWindow.Events.cs:841`](Configuration Management/Views/MainWindow.Events.cs:841)); флаг pending живёт до fallback — это верно, НЕ менять. Проверить при реализации только порядок `MouseUp` vs повторный `MouseDown` в логе.
- **F5. Avalonia-зеркало F1–F4** в [`MainWindow.Avalonia.Events.cs`](Configuration Management/Views/MainWindow.Avalonia.Events.cs:180) и при необходимости [`LeveledTreeView.Avalonia.cs`](Configuration Management/Controls/LeveledTreeView.Avalonia.cs).

**НЕ меняем:** мультивыделение (`ToggleBatchSelection`/`SelectRange`/`ClearBatchSelection`), правило секций (#326), Ctrl/Shift-клики, двойной клик, DnD.

### 2.5 Затронутые файлы (#340)

- `Configuration Management/Services/MenuCloseTrace.cs` — переработка на JSONL в каталоге данных (общий для платформ).
- `Configuration Management/Services/MenuCloseTraceFormat.cs` — новый, чистая сериализация/усечение (для тестов).
- `Configuration Management/Views/MainWindow.Tree.cs` — F1, F2 (`EnsureSelectionStable`, `SelectTreeRowByData`, `SelectionMatchesTarget`).
- `Configuration Management/Views/MainWindow.Hotkeys.cs` — дополнительные поля в записях (F-диагностика).
- `Configuration Management/Views/MainWindow.Events.cs` — дополнительные поля в записях.
- `Configuration Management/Views/MainWindow.xaml.cs` — F3 (Deactivated grace-период).
- `Configuration Management/Views/MainWindow.Avalonia.Events.cs`, `MainWindow.Avalonia.cs` — зеркальные F1–F5.
- `Configuration Management/Controls/LeveledTreeView.Avalonia.cs` — при необходимости поиск/реализация контейнера.

### 2.6 Тесты (#340)

- **Новый `ConfigurationManagement.Tests/MenuCloseTraceFormatTests.cs`:**
  - запись сериализуется в одну JSON-строку (валидный JSON, ключи латиницей);
  - усечение при превышении лимита (остаётся хвост, первая строка — маркер усечения `{"event":"truncated",...}`);
  - startup-запись содержит версию/платформу;
  - в записях нет секретов (поля username/password/значения токенов не выводятся — тест на список полей).
- **`BatchSelectionHelperTests.cs` (дополнение):**
  - новый чистый helper (если введён) «нужна ли повторная попытка восстановления при нереализованном контейнере»;
  - регресс существующих сценариев (снимок без модификаторов, идемпотентность fallback, IsSameClick).

---

## 3. Issues #323/#330/#334 — программный вход на portal.1c.ru

### 3.1 Диагноз по последнему логу 7OH (2026-10-04 13:22 МСК, #330)

```
Вход на portal.1c.ru: учётная запись 'Основная', форма: https://login.1c.ru/login?service=...
Вход на portal.1c.ru выполнен (status=200).
  ×3 повтора
Превышен лимит попыток входа на portal.1c.ru за сессию (лимит 3)
Редирект 302 (шаг 3): 'https://login.1c.ru/login?...' для 'https://releases.1c.ru/project/Platform83'
Скачивание платформы: каталог не получен — PlatformUpdate.Error.AuthRequired
```

Вывод: POST формы возвращает **200 без HTTP-редиректа и без установки сессионной cookie** (в ветке `postResponse.IsSuccessStatusCode` успехом считается 2xx + тело «не похоже на форму входа» — [`OneCUpdatesService.cs:1286`](Configuration Management/Services/OneCUpdatesService.cs:1286)). Повтор исходного запроса к releases.1c.ru снова даёт 302 → login → новый вход → лимит из 3 попыток исчерпан за одну операцию. **Это и есть «фантомный успех», ради которого в 0.3.9.303 добавили `LooksLikeLoginForm(postBody)`, но тело ответа, видимо, не похоже на HTML-форму** (возможны варианты: пустое тело, JS-страница, meta-refresh-редирект, JSON/текстовая ошибка).

### 3.2 Решение: диагностика «фантомного успеха» + защитные фиксы

**3.2.1 Расширенная пошаговая диагностика ([`TryLoginPortalAsync`](Configuration Management/Services/OneCUpdatesService.cs:1162), [`SendWithAuthAsync`](Configuration Management/Services/OneCUpdatesService.cs:994)).** В журнал (INFO/WARN, БЕЗ секретов):

- после POST (в обеих ветках: редирект и 2xx): `sessionCookie=true/false` (уже есть в 0.3.9.305 — убедиться, что в присланном логе она будет);
- в ветке 2xx: `contentType`, `bodyLength`, первые ~300 символов тела (обрезанные, экранированные; пароль/логин в теле не встречаются, поля формы логировать именами, не значениями);
- заголовки `Set-Cookie` ответа POST: только имена cookie и флаги (`HttpOnly/Secure/Domain`), БЕЗ значений;
- после входа: список cookie в `CookieContainer` для hosts `login.1c.ru`/`releases.1c.ru` (имена и атрибуты);
- в `SendWithAuthAsync`: после успешного входа и повтора исходного запроса, если снова получен 302 на login — логировать `retryAfterLoginStill302=true` (признак «фантомного успеха»).

**3.2.2 Защитный фикс «фантомный успех» — успех только с сессионной cookie.**

- В ветке 2xx ([`OneCUpdatesService.cs:1286`](Configuration Management/Services/OneCUpdatesService.cs:1286)): `Success` возвращать ТОЛЬКО если `HasPortalSessionCookie()` == true **или** тело явно содержит целевой контент (не форма и не страница входа; критерий — отсутствие маркеров входа + наличие `Set-Cookie`). Иначе: `AuthFailed`/`RedirectFailed` с сообщением «сервер вернул 200 без установки сессии (фантомный успех)» — и повторно исходный запрос НЕ запускать (не тратить лимит).
- Признак meta-refresh/JS-редиректа в теле 2xx: маркеры `<meta http-equiv="refresh"`, `window.location`, `location.href`, `document.location`, `top.location`. Если найден — извлечь URL и пройти [`FollowLoginRedirectsAsync`](Configuration Management/Services/OneCUpdatesService.cs:1470) по нему (CAS-цепочка часто доводится JS-редиректом на `security_check?ticket=…`).
- В `SendWithAuthAsync` (цикл редиректов): если вход «успешен», но повтор исходного запроса снова даёт 302 на login — НЕ начинать второй вход в рамках одной операции (`loginTried=true` уже запрещает) и вернуть ответ как есть (распознаётся как AuthRequired), а в логе зафиксировать признак фантомного успеха. Это устраняет «тройной вход за одну операцию».

**3.2.3 Прочее (оставить/проверить).**

- POST на `action` формы (0.3.9.305) — сохранить, проверять в новом логе.
- Лимит попыток и кулдаун 10 мин — не менять.
- UI окон (кнопки «Открыть login.1c.ru», «Учётные данные ИТС…») — уже есть с 0.3.9.305, не трогать; при необходимости уточнить текст для «фантомного успеха».
- Кандидат на следующие итерации (в плане 0.3.9.305, НЕ реализовывать в 0.3.9.306): импорт cookie браузера Chrome/Edge/Firefox — зафиксировать в CHANGELOG как перспективу.

### 3.3 Затронутые файлы (#323/#330/#334)

- `Configuration Management/Services/OneCUpdatesService.cs` — расширенная диагностика, фикс «фантомного успеха», следование meta-refresh/JS-редиректу, критерий Success.
- `Configuration Management/Services/PlatformUpdateService.cs` — при необходимости маппинг нового статуса (если введён, например `SessionNotEstablished` → существующий `AuthFailed` с уточнённым ключом; вероятно, маппинга не потребуется).
- `Configuration Management/ViewModels/PlatformUpdateViewModel.cs`, `PlatformDownloadViewModel.cs` — тексты (только при введении нового статуса).
- `Configuration Management/Localization/Languages/ru.json`, `en.json` — ключ для текста «сервер вернул 200 без установки сессии» (если решено показывать отдельно).
- Тесты: см. 3.4.

### 3.4 Тесты (#323/#330/#334)

- **`ConfigurationManagement.Tests/OneCUpdatesLoginFlowTests.cs` (дополнение):**
  - `LoginPost_200WithoutSessionCookie_IsNotSuccess` — ответ 200 без Set-Cookie/session cookie → НЕ Success (AuthFailed), исходный запрос не повторяется;
  - `LoginPost_200WithSessionCookie_IsSuccess` — 200 + Set-Cookie сессии → Success;
  - `LoginPost_BodyWithMetaRefresh_FollowsLocation` — в теле 200 есть meta refresh/JS-redirect → цепочка продолжается до security_check (фейковый handler);
  - `LoginDiagnostics_LogHasNoSecrets` — лог шагов не содержит пароля/логина/значений токенов и значений cookie;
  - `SendWithAuthAsync_RetryStill302_LogsPhantomSuccessMarker` — после «успешного» входа повтор даёт 302 → маркер фантомного успеха в логе, вторая попытка входа в рамках операции не запускается.
- **`ConfigurationManagement.Tests/PlatformUpdateServiceTests.cs`, `UpdateCheckCatalogTests.cs`** — регресс маппингов статусов (если менялись).

---

## 4. Комментарии в issues (этап релиза; issues НЕ закрывать)

Черновики по образцу `publish/comment-*-0.3.9.305.md`, публикуются ТОЛЬКО после релиза 0.3.9.306:

- `publish/comment-340-0.3.9.306.md` — восьмая итерация: диагностика заменена на `menuclose_trace.json` рядом с настройками (что именно и где искать, как прислать); стабилизация доработана (список F1–F5); сценарий проверки; просьба прислать файл trace.json при сохранении бага.
- `publish/comment-323-0.3.9.306.md`, `comment-330-0.3.9.306.md`, `comment-334-0.3.9.306.md` — четвёртая итерация CAS: найден и устранён «фантомный успех» (200 без сессионной cookie), расширенная диагностика; просьба проверить с реальными ИТС-данными и прислать полный лог операции.
- В комментариях указывать конкретную версию (0.3.9.306) и точные файлы/методы.

## 5. Версия 0.3.9.306, CHANGELOG.md, README.md

- [`Configuration Management.csproj`](Configuration%20Management/Configuration%20Management.csproj:62): четыре поля `Version`/`AssemblyVersion`/`FileVersion`/`InformationalVersion` → `0.3.9.306`.
- [`CHANGELOG.md`](CHANGELOG.md:12): новая секция `## [0.3.9.306] — 2026-10-04` над 0.3.9.305:
  - **#340:** диагностика `menuclose_trace.json` рядом с настройками (вместо env-переменной `%TEMP%`), стартовая запись с версией, усечение файла; стабилизация: догоняющее восстановление для нереализованного контейнера, окно до 15 проходов/1.5 с, grace-период в Deactivated;
  - **#323/#330/#334:** устранён «фантомный успех» входа (200 без сессионной cookie теперь не считается успехом, лимит попыток не тратится впустую), следование JS/meta-refresh-редиректу в теле ответа, расширенная диагностика (sessionCookie, Set-Cookie, contentType, bodyLength, маркеры);
  - число тестов после прогона.
- [`README.md`](README.md:3): бейдж версии → `Версия-0.3.9.306`.

## 6. Сборка исполняемых файлов

- **Windows/WPF:** Release build + publish по образцу `publish/build_deb_win_0.3.9.305.py` → `publish/out-0.3.9.306/`.
- **Linux/Avalonia:** `dotnet build -p:BuildLinux=true` + кросс-сборка/deb/AppImage по образцу `package/linux` и скриптов `publish/build_deb_win_0.3.9.305.py`.
- Проверка: `publish/check_deb_win_0.3.9.306.py` (по образцу check_deb_win_0.3.9.305.py), контрольные суммы `publish/SHA256SUMS_0.3.9.306.txt`.

## 7. GitHub: выкладывание изменений + новый релиз

- Коммит/пуш изменений (все правки одним PR/коммитом версии, прецедент проекта допускает несколько микро-версий, здесь одна).
- Создание GitHub Release **0.3.9.306** с телом из `publish/release_body_0.3.9.306.md` + артефакты (Windows exe/zip, Linux deb/AppImage, SHA256SUMS).
- Публикация комментариев из раздела 4 в issues **#340, #323, #330, #334**. Issues НЕ закрывать.

## 8. Порядок работ, задачи и зависимости

Каждый пункт — **отдельная новая задача** (по запросу). Рекомендуемый порядок:

| № | Задача (новая) | Зависит от | Примечание |
|---|----------------|-----------|------------|
| 1 | Декомпозиция (этот план) | — | Выполнено |
| 2 | Исправления #340: trace.json + стабилизация (код + юнит-тесты, WPF + Avalonia) | 1 | Независима от 3 |
| 3 | Исправления авторизации #323/#330/#334 (код + юнит-тесты) | 1 | Независима от 2; можно параллельно |
| 4 | Черновики комментариев к issues (publish/comment-*-0.3.9.306.md) | 2, 3 | Публикация — на шаге 7 |
| 5 | Версия 0.3.9.306 + CHANGELOG.md + README.md | 2, 3 | Описывает фактические изменения |
| 6 | Сборка Windows (WPF) и Linux (Avalonia), контрольные суммы | 5 | — |
| 7 | GitHub: коммит, релиз 0.3.9.306, публикация комментариев | 4, 5, 6 | Issues не закрывать |

Критический путь: **2/3 → 5 → 6 → 7**. Задачи 2 и 3 не пересекаются по файлам (единственная общая область — ничего: #340 правит Views/Services-трассу, #323–#334 правит OneCUpdatesService) — безопасно выполнять параллельно.

## 9. Риски

- **#340:** окно/виртуализация не покрываются юнит-тестами; финальное подтверждение — живой прогон 7OH. Диагностика теперь гарантированно пишется (файл рядом с настройками, без env).
- **#323/#330/#334:** без валидных ИТС-кредов финальное подтверждение невозможно; «фантомный успех» уже подтверждён логом, фиксы адресуют его напрямую. Если портал перешёл на JS-челлендж полностью — программный вход невозможен, потребуется импорт cookie (перспектива, вне 0.3.9.306).
- **Регресс:** стабилизация не меняет семантику мультивыделения; диагностика не влияет на поведение (ошибки записи игнорируются, файл усекается).