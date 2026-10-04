# PLAN 0.3.9.302 — исправление открытых issues #342, #340, #321, #305

Дата: 2026-10-04. Текущая версия: **0.3.9.301** (релиз от 2026-10-04). Следующая микро-версия: **0.3.9.302**.
Источник: комментарии пользователя @7OH от 2026-10-04 в [`publish/_comments_current.txt`](publish/_comments_current.txt).
Двуплатформенный проект: Windows/WPF (`#if WINDOWS`, `*.xaml`/`*.xaml.cs`) и Linux/Avalonia (`#if LINUX`, `*.Avalonia.cs`); общая логика (сервисы, модели, ViewModel) — общая.

**Ограничения реализации:** только код + юнит-тесты; issues НЕ закрывать; комментарий в issue после выхода релиза с указанием версии; изменения — в CHANGELOG.md, README.md; сборка exe для Windows и Linux; публикация релиза. Реализация выполняется другой задачей в code-режиме строго по этому плану.

---

## Порядок выполнения (рекомендуемый)

| № | Issue | Приоритет | Обоснование |
|---|-------|-----------|-------------|
| 1 | **#342** Инспектор процессов | Высокий | Причина найдена и однозначна (отсутствует привязка выделения грида к ViewModel); фикс маленький и самодостаточный — быстро закрывает жалобу «кнопка Завершить не работает». |
| 2 | **#340** Снятие выделения после мультивыделения | Высокий | Самая сложная и неопределённая (4 неудачные попытки); требует принципиально нового подхода и ручной проверки на обеих платформах — ставим в середину цикла, чтобы хватило времени на итеративную проверку. |
| 3 | **#305** Создание серверной базы | Средний | Логика + тесты, затрагивает общий сервис и оба окна; среднерисковый, делается после стабилизации выделения. |
| 4 | **#321** Стили полей EditionEditWindow | Низкий | Косметическая правка разметки; минимальный риск — в конце. |

Никаких зависимостей между задачами нет; порядок выбран по критерию «сначала воспроизводимое и самоокупаемое, сложное — в середину, косметика — в конец».

---

## Задача 1. Issue #342 «Инспектор процессов» — выделение строки сбрасывается, «Завершить процесс» не работает

### 1.1 Постановка (последний комментарий 7OH, 2026-10-04T08:53:22Z)

> «Глюк всё ещё на месте - оба: 1. Через некоторое время (периодически) сбрасывается текущая строка; 2. Нажать кнопку завершения невозможно, так как ДО нажатия или даже ПЕРЕД - сбрасывается выделение».

Фикс 0.3.9.300 (восстановление выделения по PID внутри [`ApplyRows`](Configuration%20Management/ViewModels/ProcessInspectorViewModel.cs:180)) проблему НЕ решил.

### 1.2 Причина (найдена, подтверждается кодом)

**Выделение таблицы вообще не связано с `SelectedRow` ViewModel ни на одной платформе:**

- WPF [`ProcessInspectorWindow.xaml`](Configuration%20Management/Views/ProcessInspectorWindow.xaml:44): у `DataGrid` задан только `ItemsSource`; привязки `SelectedItem="{Binding SelectedRow}"` **нет** (code-behind [`ProcessInspectorWindow.xaml.cs`](Configuration%20Management/Views/ProcessInspectorWindow.xaml.cs:44) тоже её не устанавливает).
- Avalonia [`ProcessInspectorWindow.Avalonia.cs`](Configuration%20Management/Views/ProcessInspectorWindow.Avalonia.cs:80): у `ListBox` задан только `ItemsSource`; привязки `SelectedItem` **нет**.

Отсюда всё поведение:

1. Клик по строке меняет выделение в контроле, но `_vm.SelectedRow` остаётся `null` (никто его не пишет).
2. Каждые 5 с [`ApplyRows`](Configuration%20Management/ViewModels/ProcessInspectorViewModel.cs:180) запоминает `selectedPid = SelectedRow?.Pid` → `null`, затем `Processes.Clear()` + `Add` (контрол сам сбрасывает визуальное выделение), затем `SelectedRow = null`. Восстановление по PID работает только в юнит-тестах, где `SelectedRow` выставляют вручную.
3. [`KillSelected`](Configuration%20Management/ViewModels/ProcessInspectorViewModel.cs:108) находит `SelectedRow == null` и показывает подсказку «Выберите процесс из списка» — ровно жалоба «нажать кнопку завершения невозможно».

Дополнительно: даже при корректной привязке восстановление по одному PID уязвимо к переиспользованию PID ОС (процесс перезапустился — PID тот же, процесс другой), а виртуализация контрола (WPF DataGrid / Avalonia ListBox) может «ронять» восстановленное выделение при последующей разметке.

### 1.3 Изменения

**Файлы: WPF + Avalonia + ViewModel + тесты.**

1. [`Configuration Management/Views/ProcessInspectorWindow.xaml`](Configuration%20Management/Views/ProcessInspectorWindow.xaml:44) — добавить привязку выбора:
   ```xml
   SelectedItem="{Binding SelectedRow, Mode=TwoWay}"
   ```
   (для DataGrid достаточно; `UpdateSourceTrigger` для `SelectedItem` не требуется).

2. [`Configuration Management/Views/ProcessInspectorWindow.Avalonia.cs`](Configuration%20Management/Views/ProcessInspectorWindow.Avalonia.cs:80) — после `ItemsSource` добавить двустороннюю привязку:
   ```csharp
   _grid.Bind(ListBox.SelectedItemProperty,
       new Binding("SelectedRow") { Mode = BindingMode.TwoWay });
   ```

3. [`Configuration Management/ViewModels/ProcessInspectorViewModel.cs`](Configuration%20Management/ViewModels/ProcessInspectorViewModel.cs:180) — усилить `ApplyRows`:
   - Сохранять перед перезаполнением **составной ключ** выбранной строки: `(Pid, StartTimeToken, FullCommandLine)`, а не только `Pid` (защита от переиспользования PID).
   - После `Clear()`+`Add` восстанавливать по ключу; если точного совпадения по всем трём полям нет — только по `Pid`; если и его нет — `SelectedRow = null`.
   - **Двухфазное восстановление (страховка от виртуализации):** после установки `SelectedRow` отложенно повторить установку той же строки через UI-диспетчер (WPF — `Dispatcher.BeginInvoke(..., DispatcherPriority.Background)`, Avalonia — `Dispatcher.UIThread.Post`), только если строка с тем же ключом всё ещё есть в списке и пользователь за это время ничего не выбрал (проверка «SelectedRow по-прежнему указывает на восстановленную строку»). Это закрывает случаи, когда контрол «роняет» выделение на последующей разметке/реализации контейнеров.

4. [`Configuration Management/ViewModels/ProcessRowViewModel.cs`](Configuration%20Management/ViewModels/ProcessRowViewModel.cs) — добавить внутреннее свойство составного ключа (например `SelectionKey`) или вычислять ключ в ViewModel из существующих `Pid`/`StartTimeToken`/`FullCommandLine` (чтобы не менять публичный контракт строки).

5. [`Configuration Management/Views/ProcessInspectorWindow.xaml.cs`](Configuration%20Management/Views/ProcessInspectorWindow.xaml.cs:44) — после установки `ItemsSource` ничего дополнительно не требуется (привязка в XAML); проверить, что `OnGrid_MouseDoubleClick` продолжает использовать `ProcessesGrid.SelectedItem`.

6. Локализация: изменения текстов не требуются (подсказка «Выберите процесс из списка» уже есть).

### 1.4 Тесты

[`ConfigurationManagement.Tests/ProcessInspectorSelectionTests.cs`](ConfigurationManagement.Tests/ProcessInspectorSelectionTests.cs):

- Оставить `Refresh_PreservesSelectionByPid`, `Refresh_ClearsSelectionWhenProcessGone`, `KillSelected_NoSelection_ShowsHint`, `KillSelected_WithSelection_CallsKiller`.
- Добавить:
  1. `Refresh_RestoresByCompositeKey_WhenPidReused` — два процесса с одинаковым PID, но разными командными строками/токенами старта: выбран старый (PID+cmdline), при обновлении появляется «новый» с тем же PID — выделение должно остаться на старой командной строке (по составному ключу), а не перескочить.
  2. `ApplyRows_RestoresThenClears_IfRowGone` — восстановление, затем процесс исчезает → `SelectedRow == null`.
  3. `Refresh_SecondPhaseRestore_IsIdempotent` — повторная установка `SelectedRow` (имитация двухфазного восстановления) не меняет выбранную строку, пока ключ совпадает.

### 1.5 Риски

- Двухфазное восстановление может конфликтовать с пользовательским кликом «в момент обновления»: обязательно условие «пользователь ничего не выбрал с момента восстановления» (сравнение ссылки/ключа до отложенной фазы), иначе гонка «клик по строке → отложенная фаза возвращает старую».
- Привязка `SelectedItem` TwoWay в DataGrid при `Clear()+Add` даёт промежуточный `null` → кратковременная потеря выделения возможна, но восстановление в том же кадре незаметно; двухфазная фаза — запас.
- В Avalonia `BindingMode.TwoWay` для `SelectedItem` ListBox — штатный режим, но проверить, что программная установка `SelectedRow` из фонового потока не попадает в контрол (все изменения идут через `_dispatchToUi` → уже UI-поток; сохранить инвариант).

### 1.6 Критерий проверки (ручной)

1. Открыть «Инспектор процессов», выделить строку, дождаться автообновления (≥5 с) — выделение остаётся на той же строке.
2. Нажать «Завершить процесс» сразу после выделения и спустя 5+ секунд — срабатывает подтверждение и kill по выбранному PID (без подсказки «Выберите процесс»).
3. Завершить процесс извне (или дождаться его завершения) — при следующем опросе выделение снимается корректно.
4. Повторить на Linux (Avalonia).
5. `dotnet test` зелёный; `dotnet build -p:BuildLinux=true` без ошибок.

---

## Задача 2. Issue #340 «Снятие выделения после мультивыделения/контекстного меню»

### 2.1 Постановка (последний комментарий 7OH, 2026-10-04T08:58:06Z)

> «Баг на месте - выделение пропадает». Сценарий: мультивыделение → правый клик (контекстное меню) → левый клик по другой строке → строка становится активной, но через мгновение выделение пропадает. Воспроизводится и без мультивыделения («просто вызвать контекстное меню и потом нажать на другую строку»).

Четыре предыдущие попытки (0.3.9.277, 0.3.9.291, 0.3.9.299, 0.3.9.300) — не помогли.

### 2.2 Причина (по коду; WPF и Avalonia доставляют клик по-разному)

**WPF** ([`MainWindow.Hotkeys.cs`](Configuration%20Management/Views/MainWindow.Hotkeys.cs:729), [`MainWindow.Events.cs`](Configuration%20Management/Views/MainWindow.Events.cs:619)):
- Клик по строке при открытом меню «проглатывается» попапом; после освобождения захвата WPF повторно доставляет «хвост» того же MouseDown в дерево.
- Текущий механизм 0.3.9.300: в [`TryApplyTreeClickAfterMenuClosed`](Configuration%20Management/Views/MainWindow.Hotkeys.cs:729) выбор применяется **отложенно** через `Dispatcher.BeginInvoke(..., DispatcherPriority.Input)`; повторная доставка «хвоста» гасится снимком [`BatchSelectionHelper.IsSameClick`](Configuration%20Management/Services/BatchSelectionHelper.cs:236) по времени (`DateTime.UtcNow`) и позиции (допуск ~300 мс / 12 px).
- Неустойчивые места: (а) отложенное применение выполняется ПОСЛЕ обработки текущего сообщения — между «строка стала активной» и «выбор применён» есть окно, в которое могут вмешаться разметка/виртуализация; (б) дедупликация по `DateTime.UtcNow` не привязана к штампу времени события мыши и при задержке «хвоста» >300 мс или сдвиге позиции >12 px пропускает его — тогда выбор применяется дважды (штатным путём + отложенным); (в) [`SelectTreeRowByData`](Configuration%20Management/Views/MainWindow.Tree.cs:805) может выбрать контейнер ДУБЛЯ строки (база присутствует и в «Закреплённых», и в группе — `FindTreeViewItemForData` не учитывает секцию), из-за чего «активная» строка мигает, а выделение переезжает/снимается.

**Avalonia** ([`MainWindow.Avalonia.Events.cs`](Configuration%20Management/Views/MainWindow.Avalonia.Events.cs:139)): первый PointerPressed ДОХОДИТ до контрола (выбор применяется штатно), «хвост» гасится снимком. Если дедупликация не срабатывает (время/позиция), повторное применение ломает выбор.

Итог: паттерн «применить выбор отложенно + гасить повторную доставку по данным» не даёт детерминированного результата на оконном стеке WPF.

### 2.3 Новый подход (синхронное применение, никакой отложенной работы)

**Принцип:** выбор строки, которой закрыли контекстное меню, применяется **ровно один раз и синхронно** в самой ранней детерминированной точке по данным (не по контейнеру); повторная доставка «хвоста» гасится; отложенное применение удаляется полностью.

**WPF:**

1. [`MainWindow.Hotkeys.cs`](Configuration%20Management/Views/MainWindow.Hotkeys.cs:729) — [`TryApplyTreeClickAfterMenuClosed`](Configuration%20Management/Views/MainWindow.Hotkeys.cs:729):
   - Оставить hit-test и условия (мышь над строкой, левая кнопка нажата, цель — база).
   - **Удалить блок `Dispatcher.BeginInvoke(...)`** (отложенное применение).
   - Вместо него **синхронно**: `_viewModel.ClearBatchSelection(); SelectTreeRowByData(target, clickedContainer, sectionIsPinned: from click)`.
   - Снимок `_menuCloseClickSnapshot` записывать как раньше (для гашения «хвоста»).
2. [`MainWindow.Events.cs`](Configuration%20Management/Views/MainWindow.Events.cs:630) — ветка дедупликации: при совпадении снимка оставить `e.Handled = true; return;` (повторное применение не нужно — выбор уже сделан синхронно). При несовпадении — снимок сбросить и обработать штатно (идемпотентно).
3. [`MainWindow.Tree.cs`](Configuration%20Management/Views/MainWindow.Tree.cs:805) — [`SelectTreeRowByData`](Configuration%20Management/Views/MainWindow.Tree.cs:805) доработать: добавить параметр секции клика (`isPinnedSection`); искать контейнер **в той же секции** (сначала в поддереве узла «Закреплённые» либо в обычном списке), чтобы не «перепрыгивать» на дубль.
4. Гашение «хвоста» перевести на штамп времени события мыши (WPF `MouseButtonEventArgs.Timestamp`), а не `DateTime.UtcNow` — надёжнее для допуска; допуски оставить настраиваемыми в [`BatchSelectionHelper.IsSameClick`](Configuration%20Management/Services/BatchSelectionHelper.cs:236).

**Avalonia:**

5. [`MainWindow.Avalonia.Events.cs`](Configuration%20Management/Views/MainWindow.Avalonia.Events.cs:145) — в ветке «первый клик при открытом меню» применить выбор **синхронно из данных** (найти строку по данным указателя, `ClearBatchSelection` + установить выбор через модель/контрол) и записать снимок; `e.Handled` НЕ выставлять (меню должно закрыться штатно).
6. Ветка дедупликации («хвост») — при совпадении гасить; дополнительно на случай рассинхронизации повторно зафиксировать выбор той же строки (идемпотентно).
7. Дедупликацию перевести на `PointerEventArgs.Timestamp`.

**Общее:**

8. [`BatchSelectionHelper.cs`](Configuration%20Management/Services/BatchSelectionHelper.cs) — `DecideAfterMenuCloseClick` оставить только если используется (регрессия тестов), либо удалить вместе с тестами; вместо него — чистый помощник выбора по данным «цель из снимка → единственная выбранная строка» с проверкой секции.
9. Локализация: без новых строк (подсказки уже есть).

### 2.4 Тесты

- [`ConfigurationManagement.Tests/BatchSelectionHelperTests.cs`](ConfigurationManagement.Tests/BatchSelectionHelperTests.cs): `IsSameClick` по штампу времени (добавить перегрузку), выбор по данным с учётом секции (новый helper).
- Ручные сценарии ОБЯЗАТЕЛЬНЫ (оконный стек юнит-тестами не покрывается):
  1. мультивыделение → правый клик → левый клик по другой строке: новая строка остаётся активной, набор снят, ничего не пропадает «через мгновение»;
  2. без мультивыделения: контекстное меню по строке → клик по другой строке → строка остаётся активной;
  3. выбор пункта меню и закрытие по ESC не меняют выделение;
  4. повторные клики по одной строке и быстрые серии (двойной клик для запуска базы) не ломаются;
  5. то же на Linux (Avalonia).

### 2.5 Риски

- При синхронном применении в `ContextMenu.Closed` попап ещё держит захват: программная установка `IsSelected`/`SelectedInfobase` работает, но нужно убедиться, что последующая нативная обработка «хвоста» (если WPF всё же доставит его) не перетрёт выбор — для этого и остаётся гашение снимком.
- Поведение доставки «хвоста» может отличаться между сборками WPF/OS: синхронное применение делает результат независимым от факта доставки (если «хвоста» нет — выбор уже сделан; если есть — он погашен или идемпотентен).
- Изменение `SelectTreeRowByData` (секция) затрагивает и другие вызовы (например, «Найти в списке»): проверить все места вызова.
- Как запасной вариант (если синхронное применение в Closed окажется недостаточным на какой-то сборке): глобальное подавление кликов на ~250 мс после закрытия меню с применением выбора один раз — по данным, не по контейнеру.

### 2.6 Критерий проверки (ручной)

Пункты 1–5 из раздела «Тесты» выполняются без регресса; дополнительно проверить, что:
- правый клик по строке вне мультивыделения по-прежнему открывает меню и делает строку «текущей»;
- Ctrl/Shift-мультивыделение и команды «Для выделенных (N)…» работают как раньше;
- `dotnet test` зелёный, `dotnet build -p:BuildLinux=true` без ошибок.

---

## Задача 3. Issue #305 «Создание серверной базы» — порт в предупреждении и сохранение «Сервера СУБД»

### 3.1 Постановка (последний комментарий 7OH, 2026-10-04T08:57:19Z)

> «1. Откуда взялся этот порт, если всё везде выбрано абсолютно другое? [скриншот предупреждения про различие версий платформы] 2. Сервер СУБД всё ещё не сохраняется. Был выбран localhost, который по умолчанию, но разве есть разница?»

### 3.2 Причина (по коду)

**Проблема (а) «откуда порт»:**

1. У любой клиент-серверной базы в модели `ConnectionSettings.Port` имеет значение по умолчанию **1541** ([`ConnectionSettings.cs`](Configuration%20Management/Models/ConnectionSettings.cs:62)) даже если пользователь порт нигде не вводил.
2. [`GetIncompatibleExistingVersion`](Configuration%20Management/Services/CreateInfobaseService.cs:273) ищет первую подходящую базу и возвращает её адрес через `Format1CServer(conn.Server, conn.Port)` → «localhost:1541» — порт «взялся» из списка баз приложения, а не из ввода пользователя.
3. **Реальный дефект сравнения:** [`SameServer`](Configuration%20Management/Services/CreateInfobaseService.cs:318) разбирает порт ТОЛЬКО из строки сервера (`ParseServerPort`), поле `conn.Port` игнорируется. Для типичного хранения («localhost» + `Port=1541`) обе стороны дают порт 0 → базы на `localhost:1541` и `localhost:1545` считаются ОДНИМ сервером (фикс 0.3.9.300 фактически не работает для этого хранения). В предупреждение попадает адрес «первой попавшейся» базы — случайный с точки зрения пользователя.
4. Текст предупреждения (`CreateInfobase.VersionMismatchMsg`, ru.json:1333) не объясняет, что порт взят из найденной базы списка.

**Проблема (б) «Сервер СУБД не сохраняется»:**

5. [`SaveLastDbServer`](Configuration%20Management/Services/CreateInfobaseService.cs:398) вызывается только внутри [`TryCreate`](Configuration%20Management/Services/CreateInfobaseService.cs:136) **после успешного создания**. Если пользователь закрыл окно без создания (или создание упало) — ничего не сохраняется.
6. Цепочка восстановления существует ([`RestoreLastDbServer`](Configuration%20Management/Views/CreateInfobaseWindow.xaml.cs:606), Avalonia — строка 912) и читает `AppSettings.LastCreateDbServer/LastCreateDbPort` — но покрывает только путь «успешно создал → снова открыл».
7. Дополнительная защита нужна от «пустых» значений: сохранять только непустой сервер и только в клиент-серверном режиме.

### 3.3 Изменения

**Файлы: сервис + WPF/Avalonia окна + модель + локализация + тесты.**

1. [`Configuration Management/Services/CreateInfobaseService.cs`](Configuration%20Management/Services/CreateInfobaseService.cs):
   - Новый чистый helper сравнения с явными портами:
     `internal static bool SameServer(string hostA, int portA, string hostB, int portB)` — правило: хосты равны (без регистра); если оба порта > 0 — они обязаны совпадать; если хотя бы один равен 0 — fallback «равны».
   - Перегрузить/заменить `SameServer(a, b)` на вариант, принимающий фактические порты сторон; старую сигнатуру оставить internal-обёрткой для тестов (`ParseServerPort` + порт 0).
   - [`GetIncompatibleExistingVersion`](Configuration%20Management/Services/CreateInfobaseService.cs:273) изменить:
     - сравнение по эффективным портам: сторона базы — `(ParseServerPort(conn.Server).server, conn.Port > 0 ? conn.Port : портИзСтроки)`; сторона ввода — `(serverName, requestServerPort)` (порт из поля «Сервер 1С», разобранный в окне и переданный в запрос);
     - адрес найденной базы строить через новый нормализатор `BuildBaseServerAddress(conn)` (парсит `conn.Server`, эффективный порт = `conn.Port > 0 ? conn.Port : порт из строки`, формат `server:port`) — защита от задвоения порта, если порт хранится и в строке, и в поле.
   - В результат `VersionMismatch` добавить (или переиспользовать) поле «адрес, введённый пользователем»: `EnteredServerAddress = Format1CServer(serverName, serverPort)`.
   - [`SaveLastDbServer`](Configuration%20Management/Services/CreateInfobaseService.cs:398) сделать `internal` и вызывать также из окон при закрытии (см. п. 3); в `TryCreate` оставить (не мешает).
2. [`Configuration Management/Views/CreateInfobaseWindow.xaml.cs`](Configuration%20Management/Views/CreateInfobaseWindow.xaml.cs:784) и [`CreateInfobaseWindow.Avalonia.cs`](Configuration%20Management/Views/CreateInfobaseWindow.Avalonia.cs) (ветка `VersionMismatch`):
   - текст предупреждения строить с ДВУМЯ адресами: «Вы выбрали сервер «{введённый}». В вашем списке баз есть клиент-серверные базы на сервере «{найденный}» (адрес из списка баз) с версией {версия}, которая отличается от выбранной ({выбранная}). Продолжить создание?».
3. Сохранение «Сервера СУБД» при вводе/закрытии:
   - В обоих окнах: подписаться на `Closed`/`Closing` (WPF `Closed`, Avalonia `Closed`) — при закрытии в клиент-серверном режиме сохранять текущие непустые `DbServerBox/DbPortBox` через `_createService.SaveLastDbServer(dbServer, dbPort)` (или отдельный метод-обёртка); при желании — ещё и дебаунс-сохранение на `TextChanged` (500 мс) — не обязательно, закрытие покрывает сценарий.
   - Условие: `TypeBox.SelectedIndex == 1` (клиент-сервер) и `!string.IsNullOrWhiteSpace(dbServer)`; порт пустой — допустимо (передаётся пустая строка, как сейчас).
4. [`Configuration Management/Localization/Languages/ru.json`](Configuration%20Management/Localization/Languages/ru.json:1333) и `en.json`: новый формат `VersionMismatchMsg` (3 аргумента: выбранная версия, версия из списка, найденный адрес, введённый адрес — аккуратно с `{0..3}`), при необходимости новый ключ подсказки «адрес из списка баз».

### 3.4 Тесты

[`ConfigurationManagement.Tests/CreateInfobaseDbServerStringTests.cs`](ConfigurationManagement.Tests/CreateInfobaseDbServerStringTests.cs):

- `SameServer_WithExplicitPorts_EqualWhenSame`, `SameServer_WithDifferentPorts_NotEqual` (порт в поле, а не в строке!), `SameServer_NoPortOnOneSide_Equal` (fallback), `SameServer_NoPortOnBothSides_Equal`.
- `BuildBaseServerAddress_NoDoublePort` — сервер с портом в строке и в поле одновременно → одно вхождение порта.
- `VersionMismatch_ReturnsFoundAndEnteredAddress` — через `ICreateInfobaseService` с fake-репозиторием: в списке база `localhost` + `Port=1545`, ввод `localhost:1541` → несовместимая версия НЕ находится (серверы разные); ввод `localhost` → находится, адрес `localhost:1545`.
- `SaveLastDbServer_PersistsForNextOpen` — fake-репозиторий: `SaveLastDbServer("localhost", "5433")` → `LoadSettings().LastCreateDbServer == "localhost"`, `LastCreateDbPort == "5433"`.

### 3.5 Риски

- Изменение правила сравнения портов может скрыть предупреждение там, где оно раньше появлялось (порт в поле теперь учитывается) — это ожидаемое улучшение, но нужно сверить с кейсами из истории (#305: «localhost» ≡ «localhost:1541» при запуске стартером — там отдельный матчер, не трогаем; здесь только окно создания).
- Сохранение при закрытии окна пишет настройки чаще — рисков гонки нет (атомарная запись `WriteAtomic`), но при пустом сервере не сохранять, чтобы не затереть предыдущее значение.
- Изменение текста предупреждения: обновить en.json зеркально.

### 3.6 Критерий проверки (ручной)

1. В списке баз есть клиент-серверные базы на `localhost` (порт по умолчанию 1541). В окне создания выбрать сервер `localhost` без порта и версию, отличающуюся по major.minor: предупреждение показывает «Вы выбрали сервер localhost» и «базы на сервере localhost:1541 (адрес из списка баз) с версией …» — порт объяснён.
2. Сервер с портом `localhost:1541` и базы с портом `1545` в списке: предупреждение НЕ появляется (серверы разные).
3. Ввести в «Сервер СУБД» `localhost` (порт пуст) и ЗАКРЫТЬ окно без создания → открыть снова: поле подставлено. То же для порта СУБД.
4. После успешного создания — значение по-прежнему подставляется при следующем открытии.
5. Повторить на Linux (Avalonia); `dotnet test` зелёный; `dotnet build -p:BuildLinux=true` без ошибок.

---

## Задача 4. Issue #321 «Окно Типовые конфигурации» — поля EditionEditWindow привести к общему виду

### 4.1 Постановка (последний комментарий 7OH, 2026-10-04T08:59:39Z)

> «Поля отлично видны, но в окне правки\добавления строки релиза - поля стали чужеродными. Надо вернуть к общему виду».

Контекст: в 0.3.9.300 окно `EditionEditWindow` увеличено (Height 360→520, MinHeight 330→470) и «убрана серость» полей.

### 4.2 Причина (по коду)

- WPF [`EditionEditWindow.xaml`](Configuration%20Management/Views/EditionEditWindow.xaml:24): в `Window.Resources` объявлен **локальный неявный стиль TextBox** (CardBackgroundBrush, BorderBrushColor, BorderThickness 1.5, Padding 10,7, без скруглённого шаблона и акцентных триггеров) — он переопределяет общий вид полей именно в этом окне → «чужеродные рамки/фон/отступы».
- Остальные окна правки приложения используют общий ключевой стиль [`ModernTextBox`](Configuration%20Management/Themes/LightTheme.xaml:212) (скругление 8 px, акцентная рамка при наведении/фокусе, MinHeight 36): CreateInfobaseWindow, CloneServerInfobaseWindow, GroupEditWindow, SettingsWindow и др.
- Окно-«сосед» [`ConfigTypeEditWindow.xaml`](Configuration%20Management/Views/ConfigTypeEditWindow.xaml:92) использует поля БЕЗ локального стиля (MaterialDesign-дефолт) — тоже не совпадает с `ModernTextBox`; проверка покажет, насколько это заметно.
- Avalonia уже консистентна: `EditionEditWindow.Avalonia.cs`, `ConfigTypeEditWindow.Avalonia.cs` и поиск в `ConfigTypesEditWindow.Avalonia.cs` используют `ControlThemes.ModernTextBox` — правки нужны в основном WPF.

### 4.3 Изменения

**Файлы: WPF (основное) + проверка Avalonia.**

1. [`Configuration Management/Views/EditionEditWindow.xaml`](Configuration%20Management/Views/EditionEditWindow.xaml):
   - **Удалить** локальный неявный стиль `TargetType="TextBox"` (строки 24–33) и связанный комментарий.
   - Четырём полям (`NameBox`, `RedBox`, `SubRedBox`, `UrlOverrideBox`) задать `Style="{StaticResource ModernTextBox}"` (общий вид приложения), убрать локальные `Padding/FontSize/VerticalContentAlignment` (стиль даёт свои) либо оставить только `VerticalContentAlignment="Center"` при необходимости.
2. [`Configuration Management/Views/ConfigTypeEditWindow.xaml`](Configuration%20Management/Views/ConfigTypeEditWindow.xaml:92) — для единообразия «окна правки конфигурации → окно правки релиза» применить тот же `ModernTextBox` к полям `CodeBox/NameBox/ConfigNameBox/NickBox` (сверить визуально с EditionEditWindow до и после; если дефолт MaterialDesign совпадает по виду — можно не трогать, но желательно единый стиль).
3. Проверить поле поиска в [`ConfigTypesEditWindow.xaml`](Configuration%20Management/Views/ConfigTypesEditWindow.xaml) (WPF): если оно после 0.3.9.300 осталось «нестандартным» — привести к `ModernTextBox` (Avalonia уже использует его).
4. Avalonia: только визуальная сверка (код уже на `ModernTextBox`); при расхождениях — синхронизировать поля подписей/высоту.

### 4.4 Тесты

Юнит-тесты для разметки не пишем (визуальное поведение). Достаточно:
- `dotnet test` зелёный (регрессия отсутствует),
- `dotnet build -p:BuildLinux=true` без ошибок,
- ручная сверка скриншотов в светлой и тёмной теме.

### 4.5 Риски

- Удаление локального стиля может вернуть «серость» (исходную жалобу 0.3.9.300), если `ModernTextBox` не подхватится (StaticResource из LightTheme/DarkTheme — подхватывается; проверить обе темы).
- Высота полей изменится (MinHeight 36 у ModernTextBox) — окно 520 px вмещает, но проверить, что URL-поле не выталкивается за край (окно уже увеличено в 0.3.9.300).

### 4.6 Критерий проверки (ручной)

1. «Утилиты → Типовые конфигурации» → «Добавить…»/«Изменить…» у редакции: поля по виду идентичны полям других окон (CreateInfobase/GroupEdit): скругление, рамка, отступы, подсветка при фокусе.
2. Поля редактируемы, не выглядят «серыми», текст вводится; URL-поле видно без прокрутки.
3. Сравнить окно правки релиза и окно правки конфигурации: единый стиль полей.
4. Светлая и тёмная темы; WPF и Avalonia.

---

## Общие шаги релиза 0.3.9.302 (после всех четырёх задач)

1. Инкремент версии до **0.3.9.302**.
2. Записи в [`CHANGELOG.md`](CHANGELOG.md) и `README.md` по каждой задаче.
3. Полный прогон `dotnet test` (зелёный) и кросс-сборка `dotnet build -p:BuildLinux=true`.
4. Сборка exe для Windows и Linux (скрипты в `publish/` по образцу `check_deb_win_0.3.9.301.py` и пр.).
5. Публикация релиза v0.3.9.302; в каждый из issues #342, #340, #321, #305 — комментарий «что исправлено и в какой версии» (issues не закрывать).