# PLAN 0.3.9.305 — #305, #323, #330, #334, #340 (все открытые issues категории B)

Дата: 2026-10-04. Текущая версия: **0.3.9.304**. Следующая микро-версия: **0.3.9.305**.
Источник: `publish/issues_analysis_current.md` (5 открытых issues, все категории B — последний комментарий не от sivatorov).
Двуплатформенный проект: Windows/WPF (`#if WINDOWS`, `*.xaml`/`*.xaml.cs`) и Linux/Avalonia (`#if LINUX`, `*.Avalonia.cs`). Common-логика (сервисы, модели, ViewModel) — общая.

**В скоупе:** #305, #323, #330, #334, #340 — исправления кода + юнит-тесты; issues НЕ закрываются, комментарии публикуются на этапе релиза (отдельной задачей).

**Ограничения реализации:** только код + юнит-тесты; ничего не коммитить без согласования. Реализация — отдельной задачей в code-режиме строго по этому плану.

---

## 1. Краткое резюме и порядок работ

| № | Issue | Суть | Корень (по анализу кода) | Статус по итогам анализа |
|---|-------|------|--------------------------|--------------------------|
| 1 | **#305** | Сервер СУБД не сохраняется после успешного создания; запоминать последний тип базы | **Найдена первопричина**: WPF `MainViewModel.SaveSettings()` перезаписывает файл настроек новым объектом без `LastCreateDbServer/Port` (гонка с `ScheduleSaveSettings` после успешного создания) | Детерминированный фикс |
| 2 | **#340** | Выделение пропадает «через мгновение» после клика, закрывшего контекстное меню (7-я попытка) | Гипотезы по коду: стабилизация не запускается в части путей; «нереализованный контейнер» считается согласованным; возможен сброс из `Deactivated` | Трассировка + защитные фиксы |
| 3 | **#323/#330/#334** | Программный вход на portal.1c.ru по ИТС-учётке не проходит (3-я итерация) | По присланному фрагменту лога непонятно, где рвётся CAS-цепочка (GET формы / POST / цепочка / креды). Возможные причины: POST не на `action` формы, форма изменилась, капча/JS | Диагностика по шагам + хардненинг + понятные сообщения |

**Рекомендуемый порядок работ (реализация в code-режиме):**

1. **#305** — быстрый детерминированный фикс (рефакторинг `SaveSettings` на «мутацию» загруженного объекта) + доработка «последний тип базы» (`LastCreateDbType`). Параллельно можно начать с ним, т.к. он не зависит от остальных.
2. **#340** — сначала диагностический трейс (`CM_MENUCLOSE_TRACE=1`), затем точечные фиксы F1–F3 (обе платформы, симметрично).
3. **#323/#330/#334** — пошаговая диагностика CAS-входа, POST на `action` формы, уточнение текстов ошибок, кнопки действий в окнах.
4. Полный прогон `dotnet test`, кросс-сборка Linux, релиз 0.3.9.305, комментарии в issues (issues не закрывать).

---

## 2. Issue #305 — «Создание серверной базы»

### 2.1 Описание проблемы

Пользователь (последний комментарий от 2026-10-04): «Имя сервера СУБД сохраняется — почти. Сохраняется только по кнопке отмена. При успешном создании базы — не сохраняется. Попутно предложу доработку — сохранять ещё и тип базы, который был использован в последний раз».

### 2.2 Ожидаемое поведение

- После успешного создания клиент-серверной базы значение «Сервер СУБД» (и порт) подставляется при следующем открытии окна создания.
- При повторном открытии окна сразу выбирается последний использованный тип базы («Файловая» или «Клиент-серверная»).
- Поведение при отмене/закрытии без создания сохраняется как сейчас.

### 2.3 Первопричина (подтверждена чтением кода)

Цепочка при успешном создании (WPF):

1. `OnCreate_Click` → `CreateInfobaseService.TryCreate` → успех → [`SaveLastDbServer(dbServer, dbPort)`](Configuration%20Management/Services/CreateInfobaseService.cs:139) — файл настроек получает `LastCreateDbServer`.
2. `Closed` → [`OnWindowClosed`](Configuration%20Management/Views/CreateInfobaseWindow.xaml.cs:625) — снова пишет `SaveLastDbServer`. Файл корректен.
3. Возврат в главное окно: `AddInfobase` → `SelectedInfobase = createDlg.Result` ([`MainViewModel.Commands.cs:85`](Configuration%20Management/ViewModels/MainViewModel.Commands.cs:85)) → сеттер `SelectedInfobase` вызывает `ScheduleSaveSettings()` ([`MainViewModel.cs:778`](Configuration%20Management/ViewModels/MainViewModel.cs:778)).
4. Через ~150 мс `ScheduleSaveSettings` → [`SaveSettings()`](Configuration%20Management/ViewModels/MainViewModel.Launch.cs:573), который создаёт **НОВЫЙ** `new AppSettings { ... }` с фиксированным списком полей — **без** `LastCreateDbServer`/`LastCreateDbPort` — и перезаписывает файл. **Значение теряется.**

При отмене шаг 3 отсутствует (`SelectedInfobase` не меняется) → перезаписи нет → значение сохраняется. Это в точности симптом пользователя.

Avalonia такой проблемы не имеет: там единый экземпляр `_settings` ([`MainViewModel.Avalonia.cs:45`](Configuration%20Management/ViewModels/MainViewModel.Avalonia.cs:45)), сохраняемый целиком через `SaveSettingsSilently`.

### 2.4 Предлагаемое решение

**2.4.1 WPF `SaveSettings()` — «мутация» вместо «конструктора с нуля».**

В [`MainViewModel.Launch.cs:573`](Configuration%20Management/ViewModels/MainViewModel.Launch.cs:573) заменить `_repository.SaveSettings(new AppSettings { ... })` на:

```csharp
public void SaveSettings()
{
    var s = _repository.LoadSettings();   // сохраняет поля внешних писателей
    s.Language = ...;
    s.ShowFavoritesOnly = _showFavoritesOnly;
    // ... все существующие присваивания 1:1 (тот же список полей, что сейчас) ...
    _repository.SaveSettings(s);
}
```

- Объект `s` содержит текущие значения VM (как сейчас) **плюс** любые поля, которые VM не ведёт (`LastCreateDbServer`, `LastCreateDbPort`, будущий `LastCreateDbType` и др.).
- Проверить при реализации, что набор присваиваний совпадает со старым списком (никакие поля не потерялись и не появились лишние).
- Avalonia не трогаем (там уже корректно).

**2.4.2 Доработка «запоминать последний тип базы».**

- [`AppSettings.cs:132`](Configuration%20Management/Models/AppSettings.cs:132): новое поле `public string LastCreateDbType { get; set; } = "File";` (значения `"File"`/`"ClientServer"`; пустое/неизвестное трактуется как `"File"` — обратная совместимость со старыми файлами настроек).
- [`ICreateInfobaseService.cs:94`](Configuration%20Management/Services/ICreateInfobaseService.cs:94): новая сигнатура `void SaveLastDbServer(string dbServer, string dbPort, bool isClientServer)` либо отдельный метод `SaveLastDbType(bool isClientServer)`. Минимально: расширить `SaveLastDbServer` третьим параметром.
- [`CreateInfobaseService.cs:443`](Configuration%20Management/Services/CreateInfobaseService.cs:443): `SaveLastDbServer` также пишет `settings.LastCreateDbType`.
- WPF [`CreateInfobaseWindow.xaml.cs:611`](Configuration%20Management/Views/CreateInfobaseWindow.xaml.cs:611) и Avalonia [`CreateInfobaseWindow.Avalonia.cs:917`](Configuration%20Management/Views/CreateInfobaseWindow.Avalonia.cs:917):
  - `RestoreLastDbServer` → `RestoreLastCreateState`: читать `settings.LastCreateDbType` и выставлять `TypeBox.SelectedIndex`/`_typeBox.SelectedIndex` (`1` — клиент-сервер, иначе `0`). Внимание: `TypeBox` может ещё не быть инициализирован в момент вызова — перенести восстановление типа после `InitializeComponent` (в WPF окно уже вызывает `RestoreLastDbServer()` в конструкторе после `InitializeComponent()`, строки 64–66 — проверить порядок относительно события `SelectionChanged` типа, чтобы подстановка сервера/порта не была затёрта сменой типа).
  - `OnWindowClosed` (WPF [`CreateInfobaseWindow.xaml.cs:625`](Configuration%20Management/Views/CreateInfobaseWindow.xaml.cs:625), Avalonia [`CreateInfobaseWindow.Avalonia.cs:931`](Configuration%20Management/Views/CreateInfobaseWindow.Avalonia.cs:931)): сохранять и тип (`SaveLastDbServer(dbServer, port, isClientServer: TypeBox.SelectedIndex == 1)`). Для файлового режима тоже сохранять тип (сервер не сохранять).
  - В `TryCreate` (успешный путь, [`CreateInfobaseService.cs:139`](Configuration%20Management/Services/CreateInfobaseService.cs:139)): передавать `isClientServer: true` (ветка выполняется только для клиент-серверного режима).

### 2.5 Затронутые файлы

- `Configuration Management/ViewModels/MainViewModel.Launch.cs` — `SaveSettings()` (мутация).
- `Configuration Management/Models/AppSettings.cs` — поле `LastCreateDbType`.
- `Configuration Management/Services/ICreateInfobaseService.cs`, `CreateInfobaseService.cs` — расширенный `SaveLastDbServer` (+ вызовы).
- `Configuration Management/Views/CreateInfobaseWindow.xaml.cs`, `CreateInfobaseWindow.Avalonia.cs` — восстановление типа, сохранение типа при закрытии.
- Тесты: `ConfigurationManagement.Tests/CreateInfobaseDbServerStringTests.cs` (пополнить), новый `CreateInfobaseServiceTests.cs`.

### 2.6 Риски

- **Регресс настроек WPF** при рефакторинге `SaveSettings`: снижается тем, что набор присваиваний остаётся прежним; перед слиянием — прогон всего набора тестов и ручная проверка сохранения настроек (тема, колонки, горячие клавиши, позиция окна).
- **Переупорядочение записи** файла настроек (VM и `SaveLastDbServer` пишут в один файл): последняя запись теперь всегда несёт `LastCreateDbServer`, конфликт устранён.
- **Порядок восстановления типа и сервера** при открытии окна: смена `TypeBox.SelectedIndex` должна происходить до чтения `LastCreateDbServer`-полей или аккуратно после, чтобы не затёрлись значения (проверить обработчик `TypeBox.SelectionChanged` — [`CreateInfobaseWindow.xaml.cs:251`](Configuration%20Management/Views/CreateInfobaseWindow.xaml.cs:251)).

### 2.7 Способ проверки

1. Юнит-тесты (см. раздел 6, блок #305).
2. Ручной сценарий (Windows и Linux): создать клиент-серверную базу → закрыть окно → открыть окно создания → поле «Сервер СУБД» и тип «Клиент-серверная» подставлены. Создать файловую базу → открыть окно → выбран тип «Файловая». Отмена без создания → значения сохраняются.
3. Проверить, что прочие настройки (тема, колонки, горячие клавиши) не слетели после рефакторинга `SaveSettings`.

### 2.8 Оценка трудоёмкости

Низкая (точечный рефакторинг одного метода + добавление поля и параметра).

---

## 3. Issue #340 — «Снятие выделения после мультивыделения»

### 3.1 Описание проблемы

«Выделяем несколько строк… Нажимаем правую кнопку и передумываем что-то делать. Нажимаем на другую строку для снятия выделения. Через мгновение исчезает контекстное меню и выделение тоже пропадает.» После шестой попытки (0.3.9.304) пользователь: «Баг на месте».

### 3.2 Трассировка текущей логики (WPF, 0.3.9.304)

Последовательность и три пути применения выбора:

1. Мультивыделение → правый клик по строке R → `OnInfobaseTree_PreviewMouseRightButtonDown` ставит `SelectedInfobase = R` (набор «для выделенных» не меняется) → меню открывается.
2. Левый клик по строке T: попап меню «проглатывает» MouseDown → `OnContextMenuClosed` → [`TryApplyTreeClickAfterMenuClosed`](Configuration%20Management/Views/MainWindow.Hotkeys.cs:746): фиксирует снимок (`_menuCloseClickSnapshot`), ставит `_menuClosePendingApply = true`, цель T, планирует fallback [`ApplyMenuCloseFallback`](Configuration%20Management/Views/MainWindow.Hotkeys.cs:816) на приоритете `DispatcherPriority.Input`.
3. Далее один из трёх путей:
   - **Путь A (повторная доставка MouseDown в дерево):** [`OnInfobaseTree_PreviewMouseLeftButtonDown`](Configuration%20Management/Views/MainWindow.Events.cs:619) → снимок совпал (`IsSameClick`) → `isMenuCloseRedelivery = true` → штатная ветка `ClearBatchSelection + ApplySelection(T)` → **`EnsureSelectionStable` вызывается только здесь**.
   - **Путь B (fallback):** повторной доставки нет → fallback применяет `ClearBatchSelection + SelectTreeRowByData + EnsureSelectionStable`. `EnsureSelectionStable` есть и здесь.
   - **Путь C (снимок сброшен до доставки):** `OnInfobaseTree_PreviewMouseLeftButtonUp` ([`MainWindow.Events.cs:828`](Configuration%20Management/Views/MainWindow.Events.cs:828)) сбрасывает `_menuCloseClickSnapshot = null`; пришедший позже MouseDown не распознаётся как повторная доставка → обрабатывается как обычный клик: `ClearBatchSelection + ApplySelection` — **БЕЗ `EnsureSelectionStable`**.

**Слабые места, объясняющие сохранение бага:**

- **S1 (наиболее вероятное):** в пути C выбор ставится по живому контейнеру, но сразу после закрытия попапа идёт переработка контейнеров (`VirtualizingStackPanel`, `VirtualizationMode=Recycling`); `IsSelected` «уезжает», а стабилизации нет (она вызвана только при `isMenuCloseRedelivery == true`).
- **S2:** `SelectionMatchesTarget` ([`MainWindow.Tree.cs:893`](Configuration%20Management/Views/MainWindow.Tree.cs:893)) считает «контейнер не реализован» (`item is null`) согласованным состоянием. Если строка T видима, но её контейнер в момент стабилизации не реализован/переработан, восстановление НЕ выполняется, и после окна стабилизации (3 прохода / ~200 мс) выделение остаётся потерянным навсегда.
- **S3:** окно стабилизации короткое (3 срабатывания `LayoutUpdated` или 200 мс). Отложенная переработка контейнеров после закрытия попапа может произойти позже.
- **S4 (гипотеза):** `Deactivated`-обработчик ([`MainWindow.xaml.cs:79`](Configuration%20Management/Views/MainWindow.xaml.cs:79)) сбрасывает `_menuClosePendingApply/_menuCloseTarget/снимок`. Если открытие/закрытие попапа меню вызывает кратковременную деактивацию главного окна, fallback отменяется, а повторная доставка трактуется как новый клик. Требует проверки на живой системе (см. 3.3.1).
- **S5:** в Avalonia зеркальные слабости S1–S3 (стабилизация и fallback есть, но те же окна допусков и `SelectionMatchesTarget`-логика по данным [`MainWindow.Avalonia.Events.cs:276`](Configuration%20Management/Views/MainWindow.Avalonia.Events.cs:276)).

### 3.3 Предлагаемое решение

**3.3.1 Шаг 0 — диагностическая сборка (обязателен до фиксов):**

- Новый env-гейт `CM_MENUCLOSE_TRACE=1` (по образцу `CM_COLUMNS_TRACE` из 0.3.9.298).
- Трассировать в журнал (INFO) последовательность событий по клику закрытия меню:
  1. `OnContextMenuClosed` → снимок записан: `pos=(x,y)`, `target=<Id>`, `pending=true`.
  2. `PreviewMouseLeftButtonUp` → `snapshotCleared=true/false`.
  3. `PreviewMouseLeftButtonDown` → `snapshotPresent`, `matched=IsSameClick(...)`, `path=A|C`.
  4. `ApplyMenuCloseFallback` → `ran=true/false` (был ли флаг), `containerFound=true/false` (результат `SelectTreeRowByData`), `selectedByData=true/false`.
  5. `EnsureSelectionStable` → каждый проход: `pass=N`, `selectionMatches=true/false`, `containerRealized=true/false`, действие (`restored`/`skip`).
  6. Через 500 мс после клика — контрольный дамп: `SelectedItem`, `SelectedInfobase.Id`, `container.IsSelected`, `batch.Count`.
- Цель: получить от пользователя полный лог одного воспроизведения и точно определить, какой путь (A/B/C) и какое звено рвётся. Это снимает гадание по статике.

**3.3.2 Фиксы (после анализа лога, но можно в той же версии как защитные):**

- **F1 (путь C):** вызывать `EnsureSelectionStable` во всех трёх путях. В штатной ветке простого клика стабилизировать, если «этот клик закрыл меню» — признак: снимок существовал в течение текущего клика (ввести локальный bool в `PreviewMouseLeftButtonDown`, взводимый при `_menuCloseClickSnapshot != null` до сброса, независимо от совпадения координат). Плюс в WPF можно просто перенести `EnsureSelectionStable` из ветки `if (isMenuCloseRedelivery)` в общий блок после `ApplySelection` с условием «снимок присутствовал в этом клике».
- **F2 (S2/S3):** усилить `EnsureSelectionStable`:
  - Подписку держать до **сходимости**, а не фиксированно 3 прохода: лимит 10 проходов / 1000 мс (константы рядом с методом).
  - `SelectionMatchesTarget` не должен считать видимую-но-нереализованную строку согласованной: для обычного списка и «Закреплённых» проверять, реализован ли контейнер; если нет, но строка должна быть видима — пробовать восстановить через `SelectTreeRowByData` (повторный поиск контейнера). Для строк вне видимой области (контейнер принципиально не реализован) — дополнительно подписаться на `ItemContainerGenerator.StatusChanged` / `LayoutUpdated` и, когда контейнер целевой строки будет реализован, выставить `IsSelected` (одноразово).
  - НЕ менять семантику: метод по-прежнему не вызывает `ClearBatchSelection/ToggleBatchSelection`.
- **F3 (S4):** в `Deactivated` сбрасывать pending-состояние только при отсутствии открытых контекстных меню (`_openContextMenus.Count == 0`), либо с grace-периодом: если меню ещё в процессе закрытия (попап только что освободил захват), не отменять fallback. Простейший безопасный вариант: отложенный сброс (через `Dispatcher` с приоритетом ниже Input) с проверкой «меню больше нет, снимок не потребовался».
- **F4 (симметрия Avalonia):** те же F1–F3 в [`MainWindow.Avalonia.Events.cs`](Configuration%20Management/Views/MainWindow.Avalonia.Events.cs) (пути: `OnTreeMenuCloseClickDedup_PointerPressed`/`OnTreeMenuCloseClickDedup_PointerReleased`/`ApplyMenuCloseFallback`/`EnsureSelectionStable`) и в `LeveledTreeView.Avalonia.cs` (при необходимости — поиск контейнера по данным при реализации).

**3.3.3 Что НЕ меняем:**

- Логику мультивыделения (`ToggleBatchSelection`/`SelectRange`/`ClearBatchSelection`), правило секций (#326), Ctrl/Shift-клики, двойной клик (запуск базы), DnD.

### 3.4 Затронутые файлы

- `Configuration Management/Views/MainWindow.Events.cs` — трассировка, F1, F3.
- `Configuration Management/Views/MainWindow.Hotkeys.cs` — трассировка, F1/F2 (вызовы стабилизации).
- `Configuration Management/Views/MainWindow.Tree.cs` — F2 (`EnsureSelectionStable`, `SelectionMatchesTarget`).
- `Configuration Management/Views/MainWindow.xaml.cs` — F3 (`Deactivated`).
- `Configuration Management/Views/MainWindow.Avalonia.Events.cs` — зеркальные F1–F3.
- `Configuration Management/Controls/LeveledTreeView.Avalonia.cs` — при необходимости реализация контейнера по данным.
- `Configuration Management/Services/BatchSelectionHelper.cs` — только чистые helper-дополнения для тестов (при необходимости новых решений стабилизации).
- Тесты: `ConfigurationManagement.Tests/BatchSelectionHelperTests.cs`.

### 3.5 Риски

- **Сложность воспроизведения:** окно и виртуализация не покрываются юнит-тестами; фиксы защитные, но окончательное подтверждение — живой прогон пользователем.
- **Регресс:** усиленная стабилизация может «воевать» с последующим действием пользователя (перевыбрал строку) — защищено проверкой `ReferenceEquals(SelectedInfobase, target)`; с новым пользовательским кликом стабилизация выключается.
- **Производительность:** подписка до 1000 мс на `LayoutUpdated` — не более 10 проходов, отписка обязательна во всех выходах.
- **F3** может вернуть старую проблему при реальной деактивации на другое окно — поэтому сброс оставляем, но только когда меню гарантированно закрыто.

### 3.6 Способ проверки

1. Юнит-тесты (раздел 6, блок #340) — чистая логика (`BatchSelectionHelper`).
2. Ручной сценарий пользователя (Windows, затем Linux): мультивыделение → правый клик → левый клик по другой строке → строка остаётся активной; повторить с Ctrl/Shift-мультивыделением и в «Закреплённых»; проверить, что двойной клик запускает базу и Ctrl/Shift-клики по-прежнему работают.
3. При сохранении бага — собрать полный лог с `CM_MENUCLOSE_TRACE=1` и передать автору для итерации 0.3.9.306.

### 3.7 Оценка трудоёмкости

Средняя (трассировка + 3 защитных фикса на двух платформах + тесты).

---

## 4. Issues #323/#330/#334 — программный вход на portal.1c.ru (единый корень)

### 4.1 Описание проблемы

- **#334 «Автообновление платформы»:** проверка каталога версий уходит редиректом на login.1c.ru; пользователь видит «Требуется вход на сайт 1С: проверьте учётные данные ИТС…».
- **#330 «Скачивание нужной версии платформы»:** скачивание упирается в тот же вход на portal.1c.ru.
- **#323 «Окно Проверка обновлений»:** в логе редирект 302 на `login.1c.ru/login?…` и «Требуется вход на portal.1c.ru». (Замечания «окно не модальное»/«кнопка обрезана» уже закрыты: модальность реализована в [`MainViewModel.Updates.cs:73`](Configuration%20Management/ViewModels/MainViewModel.Updates.cs:73); проверить визуально кнопку при релизе.)

История: 0.3.9.301 (динамические поля формы, HTTP/1.1, Referer/Origin, retry-политика, AuthFailed/AuthRequired) и 0.3.9.303 (анти-«фантомный успех», сброс счётчика, `loginTried` на вызов, распознавание формы при 200, `LoginLimitReached`) не решили проблему на боевом портале.

### 4.2 Ожидаемое поведение

- С сохранёнными корректными учётными данными ИТС («Настройки → Учётные данные ИТС») программный вход на portal.1c.ru проходит: проверка обновлений (F9), каталог платформы (Ctrl+F9) и скачивание платформы получают данные с releases.1c.ru.
- При неверных/отсутствующих данных — понятное сообщение с указанием конкретной причины (нет учётной записи / вход не подтверждён / форма недоступна / лимит) и действиями (открыть страницу входа в браузере, открыть справочник учётных записей ИТС, повторить).

### 4.3 Диагноз по коду (текущее состояние 0.3.9.303/304)

CAS-поток в [`TryLoginPortalAsync`](Configuration%20Management/Services/OneCUpdatesService.cs:1145) реализован полно (GET формы → `ExtractFormFields` → POST с динамическим набором полей → `FollowLoginRedirectsAsync` → проверка «фантомного успеха»). Ключевые наблюдения:

1. **POST всегда идёт на `formUrl`** ([`OneCUpdatesService.cs:1197`](Configuration%20Management/Services/OneCUpdatesService.cs:1197)) — атрибут `action` HTML-формы игнорируется. Если форма login.1c.ru имеет `action` отличный от GET-адреса (частое поведение Spring Security CAS), POST уходит не туда → 401/404 или форма ошибки → `AuthFailed`. **Конкретный, вероятный дефект.**
2. **Присланный пользователем фрагмент лога не содержит строк шагов входа** («Вход на portal.1c.ru: учётная запись…», «не подтверждён (status=…)») — непонятно, предпринималась ли попытка входа вовсе (возможно, фрагмент обрезан) и где именно она рвётся. Без пошагового лога дальнейшие «вслепую» правки неэффективны.
3. `GetCredentials` ([`OneCUpdatesService.cs:1122`](Configuration%20Management/Services/OneCUpdatesService.cs:1122)) резолвит запись ИТС через `_itsAccounts.Resolve(ItsAccountId)` с fallback на устаревшие `UpdatesLogin/UpdatesPassword`. Не ясно, какие данные фактически используются у пользователя (лог имени записи не пишется).
4. Если форма входа изменилась радикально (OAuth, JS-челлендж, капча) — программный вход невозможен в принципе; нужен признак этого в логе (нет `execution`/`lt` → `FormUnavailable`) и, в перспективе, альтернативный путь (импорт cookie браузера).

### 4.4 Предлагаемое решение (0.3.9.305 — «диагностика + хардненинг», итерация не «вслепую»)

**4.4.1 Пошаговая диагностика входа (главное):**

В [`TryLoginPortalAsync`](Configuration%20Management/Services/OneCUpdatesService.cs:1145) и [`SendWithAuthAsync`](Configuration%20Management/Services/OneCUpdatesService.cs:~1000) добавить INFO/WARN-логи (без секретов):

- в `SendWithAuthAsync`: какая причина запустила вход (`reason=401|403|redirect-login|self-redirect`, `url`, `location`), результат `PortalLoginResult`, повторён ли исходный запрос;
- в `TryLoginPortalAsync`:
  - `accountName` (имя записи ИТС, БЕЗ пароля) и `credsPresent=true/false`;
  - статус GET формы, наличие `execution`/`lt`, **имена всех полей формы** (без значений);
  - атрибут `action` формы (новое извлечение, см. 4.4.2);
  - статус POST, `location` редиректа;
  - результат `FollowLoginRedirectsAsync`;
  - `HasPortalSessionCookie()` после входа;
  - итог: `result=<Success|AuthFailed|NoCredentials|FormUnavailable|RedirectFailed>`, `status=<код>`.

**4.4.2 POST на `action` формы (вероятный фикс):**

- Новый извлекатель `ExtractFormAction(html)`: regex `<form\b[^>]*\baction\s*=\s*(['"])(?<action>.*?)\1` (+ вариант без кавычек). Вернуть `null`, если action пуст/`#`.
- В `TryLoginPortalAsync`: `postUrl = ResolveRelative(formUrl, action ?? formUrl)`; POST идёт на `postUrl` (Referer/Origin оставляем на `formUrl`). Логировать факт «POST на action формы».
- Не ломать текущие тесты (дефолтный случай: action отсутствует → POST на formUrl).

**4.4.3 Распознавание «протокол изменился»:**

- Если после GET формы нет ни `execution`, ни `lt`, но форма содержит признаки OAuth/JS-челленджа (маркеры `oauth`, `csrf`, `challenge`, `client_id`) — в `LogAnonymizedAuthFailure` выводить эти маркеры; итог для UI — `FormUnavailable` с текстом «Форма входа изменилась: автоматический вход временно недоступен; откройте login.1c.ru в браузере…».
- В перспективе (вне 0.3.9.305): импорт cookie из браузера Chrome/Edge/Firefox. Зафиксировать кандидатом на 0.3.9.306+ в CHANGELOG/комментариях — не реализовывать сейчас.

**4.4.4 UI окон (UpdateCheckWindow / PlatformUpdateWindow / PlatformDownloadWindow):**

- При `AuthRequired`/`AuthFailed`/`FormUnavailable`/`LoginLimitReached` показывать:
  - имя используемой учётной записи ИТС (анонимизированно: «Учётная запись „Основная“»),
  - кнопку/ссылку «Открыть login.1c.ru в браузере» (есть с 0.3.9.287 — оставить; при необходимости добавить явную кнопку),
  - кнопку «Учётные данные ИТС…» (открывает окно справочника `ItsAccountsWindow`/эквивалент),
  - кнопку «Повторить» (повторный запуск операции без закрытия окна).
- Тексты в [`ru.json`](Configuration%20Management/Localization/Languages/ru.json)/`en.json`: уточнить `Updates.AuthRequired`/`AuthFailed`/`AuthAdvice` (0.3.9.303) формулировкой «автоматический вход не выполнен», добавить ключ для `FormUnavailable`.
- Проверить визуально кнопку «Начать проверку» в `UpdateCheckWindow` (замечание из описания #323) — при обрезке поправить разметку (WPF XAML).

**4.4.5 Порядок публикации для пользователя:**

- После релиза 0.3.9.305 запросить у пользователя (7OH) полный лог операции с новыми шагами и проверить в браузере инкогнито те же учётные данные на login.1c.ru (валидность пары). По результатам — либо закрыть итерацией, либо план 0.3.9.306 по конкретной причине из лога.

### 4.5 Затронутые файлы

- `Configuration Management/Services/OneCUpdatesService.cs` — пошаговые логи, `ExtractFormAction`, POST на action, маркеры новой формы.
- `Configuration Management/Services/PlatformUpdateService.cs` — при необходимости проброс статуса `FormUnavailable` в результат (маппинг уже есть для AuthFailed).
- `Configuration Management/ViewModels/PlatformUpdateViewModel.cs`, `PlatformDownloadViewModel.cs` — отображение нового текста.
- `Configuration Management/Views/UpdateCheckWindow.xaml(.cs)`, `.Avalonia.cs`, `PlatformUpdateWindow.xaml(.cs)`, `.Avalonia.cs`, `PlatformDownloadWindow.xaml(.cs)`, `.Avalonia.cs` — кнопки/тексты.
- `Configuration Management/Localization/Languages/ru.json`, `en.json` — ключи.
- Тесты: `ConfigurationManagement.Tests/OneCUpdatesLoginFlowTests.cs`, `PlatformUpdateServiceTests.cs`, `UpdateCheckCatalogTests.cs` (существующие названия уточнить при реализации).

### 4.6 Риски

- **Невозможность подтверждения без валидных ИТС-кредов** — главный риск (как и в 0.3.9.301/303). Все правки повышают вероятность и, главное, диагностируемость; финальная проверка — живой сеанс.
- **Капча/JS-челлендж** могут сделать программный вход невозможным в принципе → тогда решение — импорт cookie браузера (следующая итерация).
- **Неверные креды пользователя** — не лечится кодом; диагностика (лог имени записи + шагов) поможет отличить этот случай.
- **Регресс существующих сообщений** (ссылка 0.3.9.287, AuthAdvice 0.3.9.303) — только добавляем, не заменяем рабочее.
- **Лимит попыток (анти-брутфорс портала)** — 3 попытки/сессию + кулдаун 10 мин уже реализованы; не увеличивать.

### 4.7 Способ проверки

1. Юнит-тесты (раздел 6, блок #323/#330/#334).
2. Живая проверка с пользователем: F9 по «Бухгалтерия предприятия», Ctrl+F9 (Обновление платформы), окно «Скачивание платформы» с реальными ИТС-данными; собрать полный лог.
3. Негативные сценарии: заведомо неверный пароль → сообщение «Вход не подтверждён (401)» с советом; пустой справочник ИТС → «не задан логин»; повторная попытка после лимита через 10 минут.

### 4.8 Оценка трудоёмкости

Средняя (диагностика + мелкий хардненинг + UI + тексты + тесты). Не включает импорт cookie (отдельная задача).

---

## 5. Версионирование, CHANGELOG, README

### 5.1 Политика версий

**Одна микро-версия 0.3.9.305 на все пять issues.** Обоснование (по прецедентам 0.3.9.298/301/302/303/304):

- #305 — быстрый детерминированный фикс;
- #340 — седьмая итерация, требует живой проверки;
- #323/#330/#334 — третья итерация CAS, требует живых ИТС-данных.

Отдельные версии дробили бы релиз без выгоды. При сохранении проблемы CAS после проверки пользователем — условная версия **0.3.9.306** строго по новым диагностическим логам (план составляется отдельно). Прецедент проекта допускает несколько микро-версий в одном коммите, но 0.3.9.305 выпускается как релиз A.

### 5.2 Поднятие версии

Файл [`Configuration Management.csproj`](Configuration%20Management/Configuration%20Management.csproj:62), четыре поля → `0.3.9.305`:
- `Version`, `AssemblyVersion`, `FileVersion`, `InformationalVersion`.

### 5.3 CHANGELOG.md

Новая секция `## [0.3.9.305] — 2026-10-04` над `0.3.9.304`, стиль прошлых записей. Содержание:

- **Создание серверной базы (issue #305):**
  - устранена потеря «Сервера СУБД» после успешного создания: сохранение настроек главного окна больше не перезаписывает файл «с нуля» (сохраняются поля внешних писателей, включая последний сервер СУБД/порт);
  - запоминается и восстанавливается последний использованный тип базы («Файловая»/«Клиент-серверная») при открытии окна создания.
- **Снятие выделения после мультивыделения (issue #340, седьмая попытка):**
  - диагностика `CM_MENUCLOSE_TRACE=1` (полная последовательность событий клика, закрывшего контекстное меню);
  - стабилизация `IsSelected` вызывается во всех путях применения клика (в т.ч. при сброшенном снимке до повторной доставки);
  - стабилизация доводится до сходимости (до 10 проходов/1 с) и восстанавливает выбор при видимой, но нереализованной виртуализацией строке;
  - сброс pending-состояния при деактивации окна — только после фактического закрытия контекстных меню.
- **Программный вход на portal.1c.ru (issues #323/#330/#334, третья итерация):**
  - пошаговая диагностика входа (причина запуска входа, статусы GET/POST, имена полей формы, результат цепочки, наличие сессионной cookie) — следующий релиз сможет точно указать причину при сохранении проблемы;
  - POST формы входа отправляется на атрибут `action` формы (ранее — всегда на URL GET);
  - распознавание изменённой формы входа (OAuth/JS-челлендж) с понятным сообщением;
  - в окнах проверки обновлений/платформы — имя используемой учётной записи ИТС, кнопки «Открыть login.1c.ru в браузере» и «Учётные данные ИТС…».
- Перечень тестов и числа (полный набор после реализации).

### 5.4 README.md

- [`README.md:3`](README.md:3): бейдж версии `Версия-0.3.9.304` → `Версия-0.3.9.305`.
- При желании — одна фраза в разделе возможностей про автоподстановку последнего сервера СУБД/типа при создании базы (необязательно).

### 5.5 Комментарии в issues (на этапе релиза, issues НЕ закрывать)

Черновики в `publish/` по образцу существующих:
- `publish/comment-305-0.3.9.305.md` — исправлено сохранение сервера СУБД после успешного создания (причина: перезапись файла настроек), добавлено запоминание типа базы; сценарий проверки.
- `publish/comment-340-0.3.9.305.md` — седьмая итерация: диагностика + стабилизация во всех путях; просьба проверить сценарий и при сохранении бага прислать лог с `CM_MENUCLOSE_TRACE=1`.
- `publish/comment-323-0.3.9.305.md`, `comment-330-0.3.9.305.md`, `comment-334-0.3.9.305.md` — третья итерация CAS: пошаговая диагностика, POST на action формы; просьба проверить с реальными ИТС-данными и прислать полный лог.

---

## 6. Перечень новых/изменяемых тестов

### Блок #305

1. **Новый `CreateInfobaseServiceTests.cs`** (fake `IInfobaseRepository`, в т.ч. с временным файлом):
   - `TryCreate_Success_SavesLastDbServerAndType` — успешное создание клиент-серверной базы → в настройках `LastCreateDbServer`/`LastCreateDbPort`/`LastCreateDbType="ClientServer"`.
   - `SaveLastDbServer_FileMode_SavesTypeOnly` — файловый режим → тип `"File"`, сервер не перезаписывается (или не пишется).
   - `SaveLastDbServer_InvalidRepo_DoesNotThrow` — исключение в репозитории не прерывает создание (поведение уже есть, зафиксировать тестом).
2. **`AppSettings` round-trip** (новый или в существующем наборе): сериализация/десериализация `LastCreateDbType`; пустое/старое значение → `"File"`.
3. **`MainViewModel` (WPF) `SaveSettings` merge** — если удастся вынести чистую часть (например, helper `MergeVmFields(AppSettings s)`), покрыть тестом «сохраняет поля, которых нет в списке VM»; иначе — ручная проверка (UI-связанный код).

### Блок #340

4. **`BatchSelectionHelperTests.cs`** (дополнение):
   - `DecideSelectionRestore_UnrealizedContainer_ReturnsSelectByData` — контейнер не реализован (null) → восстановление требуется.
   - `DecideSelectionRestore_UserReselected_ReturnsNone` (существует аналогичный? — уточнить и дополнить вариантом «контейнер нереализован»).
   - `IsSameClick_*` — границы допусков (время раньше снимка, > допуска, координаты вне окрестности) — расширить при необходимости.
   - `ShouldRecordMenuCloseSnapshot_*` — Left+Ctrl/Shift → false; Right → false.
5. Если вводится новый чистый helper для решения «контейнер виден, но не реализован» — соответствующие unit-тесты.

### Блок #323/#330/#334

6. **`OneCUpdatesLoginFlowTests.cs`** (дополнение):
   - `LoginPost_UsesFormActionUrl` — форма с `action="/login/cas?service=…"` → POST на резолвленный action (не на GET-URL).
   - `LoginPost_NoFormAction_PostsToFormUrl` — без action → прежнее поведение (регресс).
   - `LoginStepDiagnostics_LoggedNoSecrets` — при неудаче лог содержит шаги/имена полей/маркеры, но не пароль/логин/значения токенов.
   - `LoginForm_NoExecution_FormUnavailableWithMarker` — форма без execution/lt, но с oauth-маркерами → `FormUnavailable` и в логе маркер.
   - `SendWithAuthAsync_LogsReasonForLogin` — лог содержит причину запуска входа (`redirect-login`/`401`/`self-redirect`).
7. **`PlatformUpdateServiceTests.cs`** — маппинг `FormUnavailable` → пользовательский статус/сообщение (ключ локализации).
8. **`UpdateCheckCatalogTests.cs`** — сценарий `FormUnavailable` для каталога конфигурации (результат Failed с ключом, не NetworkError).
9. **`PlatformUpdateViewModelTests.cs`/`PlatformDownloadViewModelTests.cs`** — отображение имени учётной записи и нового текста FormUnavailable.

### Общий прогон

- Полный `dotnet test` (на момент 0.3.9.304 — 1721 тест) зелёный; кросс-сборка Linux (`dotnet build -p:BuildLinux=true`) без ошибок.

---

## 7. Сводка затрагиваемых файлов

**Код:**
- `Configuration Management/ViewModels/MainViewModel.Launch.cs` — `SaveSettings()` (мутация) — #305.
- `Configuration Management/Models/AppSettings.cs` — `LastCreateDbType` — #305.
- `Configuration Management/Services/ICreateInfobaseService.cs`, `CreateInfobaseService.cs` — расширенный `SaveLastDbServer` — #305.
- `Configuration Management/Views/CreateInfobaseWindow.xaml.cs`, `CreateInfobaseWindow.Avalonia.cs` — восстановление/сохранение типа — #305.
- `Configuration Management/Views/MainWindow.Events.cs`, `MainWindow.Hotkeys.cs`, `MainWindow.Tree.cs`, `MainWindow.xaml.cs` — трассировка/стабилизация #340 (WPF).
- `Configuration Management/Views/MainWindow.Avalonia.Events.cs`, `Configuration Management/Controls/LeveledTreeView.Avalonia.cs` — #340 (Avalonia).
- `Configuration Management/Services/BatchSelectionHelper.cs` — чистые helper (при необходимости) — #340.
- `Configuration Management/Services/OneCUpdatesService.cs` — пошаговая диагностика, `ExtractFormAction`, POST на action — #323/#330/#334.
- `Configuration Management/Services/PlatformUpdateService.cs` — маппинг `FormUnavailable` — #323/#330/#334.
- `Configuration Management/ViewModels/PlatformUpdateViewModel.cs`, `PlatformDownloadViewModel.cs` — новые тексты — #323/#330/#334.
- `Configuration Management/Views/UpdateCheckWindow.xaml(.cs)`, `.Avalonia.cs`, `PlatformUpdateWindow.*`, `PlatformDownloadWindow.*` — кнопки/тексты — #323/#330/#334.
- `Configuration Management/Localization/Languages/ru.json`, `en.json` — ключи — #323/#330/#334.

**Тесты:** `CreateInfobaseServiceTests.cs` (новый), `BatchSelectionHelperTests.cs`, `OneCUpdatesLoginFlowTests.cs`, `PlatformUpdateServiceTests.cs`, `UpdateCheckCatalogTests.cs`, `PlatformUpdateViewModelTests.cs`, `PlatformDownloadViewModelTests.cs`, (возможно) `AppSettingsTests`.

**Документация/релиз:** `Configuration Management.csproj` (0.3.9.305), `CHANGELOG.md`, `README.md`, `publish/comment-{305,340,323,330,334}-0.3.9.305.md`, `publish/release_body_0.3.9.305.md`, артефакты в `publish/out-0.3.9.305/`.

---

## 8. Критерии приёмки

- **#305:** после успешного создания клиент-серверной базы поле «Сервер СУБД» и тип восстанавливаются при следующем открытии окна; при отмене — тоже; остальные настройки не слетают; новые тесты зелёные.
- **#340:** сценарий «мультивыделение → правый клик → левый клик по другой строке» оставляет строку активной (обе платформы); Ctrl/Shift-клики, двойной клик и DnD не регрессировали; при сохранении бага пользователь может прислать полный лог `CM_MENUCLOSE_TRACE=1`.
- **#323/#330/#334:** с реальными ИТС-данными вход на portal.1c.ru проходит (F9, каталог платформы, скачивание); при неверных данных — понятное сообщение с причиной; в логе видны все шаги входа; новые тесты зелёные.
- Релиз: артефакты exe/linux-x64/deb с контрольными суммами, `FileVersion=0.3.9.305`, комментарии «исправлено в версии 0.3.9.305» в #305/#340/#323/#330/#334; **issues не закрыты**.

---

## 9. Риски (общие)

- **#323/#330/#334 нельзя подтвердить без живых ИТС-кредов** — итоговая проверка только с пользователем; при сохранении — итерация 0.3.9.306 по новым логам (план отдельный).
- **#340 — оконный стек не покрывается юнит-тестами**; фиксы защитные и диагностируемые; возможно потребуется итерация по логу пользователя.
- **Регресс настроек WPF (#305)** — снижен тождественностью набора полей; проверить полным прогоном тестов и ручной проверкой.
- **Двуплатформенность** — правки делаются симметрично (WPF/Avalonia); common-логика не разъезжается.
- **Лимит попыток входа** — не увеличивать (анти-брутфорс портала); кулдаун 10 минут уже реализован.