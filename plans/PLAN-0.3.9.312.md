# PLAN 0.3.9.312 — Кластер C: группа «Привязка» на вкладке «Платформа» свойств базы (#346)

- Дата: 2026-10-05. Режим: **архитектор** — только технический разбор; код НЕ изменяется.
- Основание: сводный [`plans/PLAN-0.3.9.310-312.md`](PLAN-0.3.9.310-312.md), задача T2; вход — полный текст `publish/issue_346_full.md` (2 комментария; последний 7OH 2026-10-05T13:03:33Z — НОВЫЙ запрос).
- Версия: **0.3.9.312** (микро-версия кластера C; комментарий в #346 после релиза, issue не закрывать; ответ «Исправлено в 0.3.9.309» уже опубликован и НЕ дублируется — отвечаем только на новое предложение).
- Проект двухплатформенный: WPF (`#if WINDOWS`, `*.xaml`) / Avalonia (`#if LINUX`, `*.Avalonia.cs`); сервисы и чистая логика — общие.

---

## C1. Диагноз по коду и требования

### C1.1 Что запросил 7OH (комментарий 2 в #346)

> «В окне правки свойств базы, на вкладке Платформа ниже группы Конфигурация вывести ещё одну группу "Привязка". В которой будет отображаться текущая привязка базы — сейчас вообще нигде этой информации нет. Так же рядом с новым полем должна быть кнопка для очистки этой связи и возможность тут же сделать новую привязку.»

Требования (декомпозиция):
1. Группа «Привязка» на вкладке «Платформа» свойств базы, ниже группы «Конфигурация».
2. Отображение текущей привязки: `UpdateConfigCode` → наименование типовой конфигурации + редакция + ник/URL каталога.
3. Кнопка «Очистить»: сброс `UpdateConfigCode`/`UpdateUrlOverride`/`UpdateUrlSegment` с подтверждением.
4. Возможность повторной привязки: вызов существующего окна «Связать с конфигурацией» (`ConfigUpdateLinkWindow`).
5. WPF + Avalonia симметрично; локализация ru/en; юнит-тесты.

### C1.2 Текущее состояние кода

| Узел | Что есть сейчас | Пробел |
|---|---|---|
| [`Models/Infobase.cs:331`](Configuration%20Management/Models/Infobase.cs:331) | Поля `UpdateConfigCode`, `UpdateUrlOverride`, `UpdateUrlSegment` | Хранятся, но **не редактируются в окне свойств базы** — только через окно «Связать с конфигурацией» (`ConfigUpdateLinkWindow`) из контекстного меню/команды F9-семейства ([`MainViewModel.Updates.cs:106`](Configuration%20Management/ViewModels/MainViewModel.Updates.cs:106), [`MainViewModel.Avalonia.Updates.cs:56`](Configuration%20Management/ViewModels/MainViewModel.Avalonia.Updates.cs:56)) |
| [`ConnectionSettingsViewModel.cs`](Configuration%20Management/ViewModels/ConnectionSettingsViewModel.cs) | Свойства вкладки «Платформа»: `ConfigurationName`, `ConfigurationVersion`, `PlatformVersion`, …; методы `LoadFrom` ([1121](Configuration%20Management/ViewModels/ConnectionSettingsViewModel.cs:1121))/`ApplyTo` ([1301](Configuration%20Management/ViewModels/ConnectionSettingsViewModel.cs:1301)) | Не переносят поля привязки; нет свойств для отображения связи и команд |
| [`ConnectionSettingsWindow.xaml`](Configuration%20Management/Views/ConnectionSettingsWindow.xaml.cs:40) (WPF) и `.Avalonia.cs` | Окно свойств базы; вкладки; группа «Конфигурация» (поля «Конфигурация»/«№ релиза») | Нет группы «Привязка» |
| [`ConfigUpdateLinkWindow.xaml.cs:42`](Configuration%20Management/Views/ConfigUpdateLinkWindow.xaml.cs:42) / [`.Avalonia.cs:31`](Configuration%20Management/Views/ConfigUpdateLinkWindow.Avalonia.cs:31) | Окно «Связать с конфигурацией»: конструктор `(Infobase infobase)`; при сохранении пишет `UpdateConfigCode`/`UpdateUrlOverride`/`UpdateUrlSegment` прямо в объект и в репозиторий (`PersistLink`, [341](Configuration%20Management/Views/ConfigUpdateLinkWindow.xaml.cs:341)) | Переиспользуемо как есть для повторной привязки |
| [`Models/OneCConfigType.cs`](Configuration%20Management/Models/OneCConfigType.cs) | `Code`, `Name`, `ConfigName`, `Nick`, `Editions` (`OneCConfigEdition` с `Edition`/`SubEdition`/сегментами) | Для отображения: поиск по `Code`, выбор редакции по версии через [`ConfigTypeMatcher.FindEditionByVersion`](Configuration%20Management/Services/ConfigTypeMatcher.cs) (прецедент: [`ConfigUpdateLinkWindow.xaml.cs:296`](Configuration%20Management/Views/ConfigUpdateLinkWindow.xaml.cs:296)) |
| Построение URL | [`OneCUpdatesService.BuildUpdateUrl(config, edition, override, segment)`](Configuration%20Management/Services/OneCUpdatesService.cs) (прецедент [`UpdateCheckWindow.xaml.cs:88`](Configuration%20Management/Views/UpdateCheckWindow.xaml.cs:88)) | Для отображения ссылки каталога |

### C1.3 Решения по архитектуре

1. **Список типовых конфигураций для отображения**: окно свойств получает `IReadOnlyList<OneCConfigType>` из `ICustomConfigTypesStore.LoadAll()` (единый загрузчик — как в [`ConfigUpdateLinkWindow.LoadConfigs`](Configuration%20Management/Views/ConfigUpdateLinkWindow.xaml.cs:80)) и передаёт в VM через новый метод `SetConfigTypes(...)`. VM остаётся тестируемой (без DI-зависимостей).
2. **Описание привязки — чистый метод VM**: `BuildLinkSummary(...)`: если `UpdateConfigCode` пуст → null (показываем «—»); иначе найти `OneCConfigType` по `Code`, редакцию — по `ConfigurationVersion` (префикс версии, `ConfigTypeMatcher.FindEditionByVersion`), URL — через делегат построения адреса (инжектируется из окна: `Func<OneCConfigType?, OneCConfigEdition?, string, string>` вокруг `IOneCUpdatesService.BuildUpdateUrl`), учесть `UpdateUrlOverride`/`UpdateUrlSegment`. Итог: строка вида `«Бухгалтерия предприятия» · ред. 3.0 · releases.1c.ru/project/Accounting30` (или URL override).
3. **Семантика сохранения привязки** (важно): окно «Связать с конфигурацией» пишет поля **сразу в объект Infobase и репозиторий** (`PersistLink`). Чтобы поведение было согласованным:
   - «Повторная привязка»: открываем `ConfigUpdateLinkWindow(infobase)` с ТЕМ ЖЕ объектом базы, который редактирует окно свойств; после закрытия окна связи — перечитываем 3 поля из `infobase` в VM (`RefreshLinkState()`) и помечаем изменения. Привязка сохраняется немедленно (как в контекстном меню) — это прецедентное поведение.
   - «Очистить»: подтверждение (IDialogService.Confirm), затем сброс 3 полей в VM И в `infobase` + сохранение в репозиторий (аналог `PersistLink` — вынести общий helper или повторить 5 строк). Если база новая (ещё не в репозитории) — сброс перетечёт в репозиторий при штатном сохранении через `ApplyTo` (см. п. 4).
   - Альтернатива (отклонена): изменения привязки применять только через кнопку «Сохранить» окна свойств — потребовала бы передачи в `ConfigUpdateLinkWindow` временной копии и рассинхронизировала бы поведение с контекстным меню.
4. **`ApplyTo`/`LoadFrom`**: добавить перенос `UpdateConfigCode`/`UpdateUrlOverride`/`UpdateUrlSegment`, чтобы сброс/связь новой базы сохранялись штатно и при «Сохранить» окна свойств (для случая, когда PersistLink не нашёл базу в репозитории).
5. **UI** (обе платформы): группа «Привязка» под группой «Конфигурация» на вкладке «Платформа»: текстовое поле описания (при отсутствии — вторичный текст «—» / «не задана»), кнопки «Связать…» (или «Изменить привязку») и «Очистить» (disabled при отсутствии привязки).
6. **Локализация**: ключи `Conn.*` в `ru.json`/`en.json`.

---

## C2. Предлагаемые изменения

| № | Файл | Что изменить |
|---|------|--------------|
| C-1 | [`ConnectionSettingsViewModel.cs`](Configuration%20Management/ViewModels/ConnectionSettingsViewModel.cs) | Новые свойства: `UpdateConfigCode`, `UpdateUrlOverride`, `UpdateUrlSegment` (переносятся в `LoadFrom`/`ApplyTo`); `HasLink` (производное, `OnPropertyChanged` при изменении кода); `LinkDisplay` (строка описания, строится в `RefreshLinkState()`); `LinkUrl` (для показа ссылки). Новые методы: `SetConfigTypes(IReadOnlyList<OneCConfigType>)`; `RefreshLinkState()` — пересчёт `HasLink`/`LinkDisplay`/`LinkUrl`; статический чистый `BuildLinkSummary(OneCConfigType? config, OneCConfigEdition? edition, string urlOrEmpty)` (форматирование строки; при `config is null` → null). Инжектируемый делегат построения URL: `Func<string,string,string>` не нужен — окно строит URL и передаёт строку; VM хранит «сырые» поля и форматирует отображение. |
| C-2 | [`ConnectionSettingsWindow.xaml`](Configuration%20Management/Views/ConnectionSettingsWindow.xaml) (WPF) | Вкладка «Платформа», под группой «Конфигурация»: `<GroupBox Header="{Loc Conn.LinkGroup}">` с `TextBlock Text="{Binding LinkDisplay}"` (или стиль «не задана»), кнопки «Связать…» (`OnBindConfigLink_Click`) и «Очистить» (`OnClearConfigLink_Click`, `IsEnabled="{Binding HasLink}"`). |
| C-3 | [`ConnectionSettingsWindow.xaml.cs`](Configuration%20Management/Views/ConnectionSettingsWindow.xaml.cs:40) (WPF) | Конструктор: получить `ICustomConfigTypesStore`/`IOneCUpdatesService` из `AppServices`; `_viewModel.SetConfigTypes(_store.LoadAll())`; после `LoadFrom(infobase)` → `RefreshLinkState()`. Обработчики: `OnBindConfigLink_Click` — `new ConfigUpdateLinkWindow(CurrentInfobase) { Owner = this }.ShowDialog()` затем `_viewModel.RefreshLinkState()` + пометка изменений; `OnClearConfigLink_Click` — `_dialogs.Confirm(key Conn.LinkClearConfirm)` → сброс VM-полей + запись в infobase/repository (helper `PersistLinkFields`) → `RefreshLinkState()`. Хранение редактируемого объекта: окно уже держит `_viewModel`; передать `Infobase` в обработчики через поле (например, `_editingInfobase`, заполняется в конструкторе). |
| C-4 | [`ConnectionSettingsWindow.Avalonia.cs`](Configuration%20Management/Views/ConnectionSettingsWindow.Avalonia.cs) | Зеркально C-2/C-3: группа и кнопки на вкладке «Платформа» (Avalonia-разметка строится кодом), `ConfigUpdateLinkWindow.ShowSync(this)`, подтверждение через `IDialogService`/`MessageBox`, `PersistLinkFields`-аналог. |
| C-5 | [`ConnectionSettingsWindow.xaml.cs:341`](Configuration%20Management/Views/ConfigUpdateLinkWindow.xaml.cs:341) / [`.Avalonia.cs:386`](Configuration%20Management/Views/ConfigUpdateLinkWindow.Avalonia.cs:386) | **Не меняется**; окно связи переиспользуется как есть. Опционально: вынести `PersistLink`-логику в общий helper `InfobaseLinkStorage.Save(infobase, repository)` — используется и окном связи, и очисткой в свойствах базы (уменьшение дублирования); только если не затрагивает существующий контракт. |
| C-6 | [`Services/ConfigTypeMatcher.cs`](Configuration%20Management/Services/ConfigTypeMatcher.cs) | Использовать существующие `FindByCode`/`FindEditionByVersion` (проверить наличие `FindByCode`; при отсутствии — маленький чистый метод `FindByCode(IEnumerable<OneCConfigType>, string)` с тестами). |
| C-7 | Локализация `ru.json`/`en.json` | Ключи: `Conn.LinkGroup` («Привязка»/«Link»), `Conn.LinkNotSet` («не задана»/«not set»), `Conn.LinkBind` («Связать…»/«Link…»), `Conn.LinkClear` («Очистить»/«Clear»), `Conn.LinkClearConfirm` («Снять привязку к типовой конфигурации?»/«Remove the link to the standard configuration?»), при необходимости `Conn.LinkEditionFormat` («{0} · ред. {1}»). |
| C-8 | README/CHANGELOG | После реализации: описание новой группы в окне свойств базы; CHANGELOG `## [0.3.9.312]`. |

Формат отображения (пример): `Бухгалтерия предприятия · 3.0 · https://releases.1c.ru/project/Accounting30`; при `UpdateUrlOverride` — показывать его (с пометкой «(ручная ссылка)»); при отсутствии ника — только имя и редакция; при пустом коде — «—» (не задана).

---

## C3. Юнит-тесты

Файл [`ConnectionSettingsViewModelTests.cs`](ConfigurationManagement.Tests/ConnectionSettingsViewModelTests.cs) (существующие fake-хелперы переиспользуются):

1. `LoadFrom_TransfersLinkFields` — `LoadFrom` переносит `UpdateConfigCode`/`UpdateUrlOverride`/`UpdateUrlSegment`.
2. `ApplyTo_WritesLinkFields` — `ApplyTo` пишет поля привязки в Infobase (включая сброс в пустые строки).
3. `RefreshLinkState_NoCode_ReturnsNotSetAndNoLink` — пустой код → `HasLink=false`, `LinkDisplay` = «—».
4. `BuildLinkSummary_KnownConfigAndEdition_Formats` — известный код → «Имя · ред. 3.1 · https://…» (с учётом ника/URL).
5. `BuildLinkSummary_ConfigWithoutNick_OmitsUrl` — конфигурация без ника и без override → строка без URL.
6. `BuildLinkSummary_UrlOverride_PrefersOverride` — при `UpdateUrlOverride` показывается он (с пометкой ручной ссылки).
7. `RefreshLinkState_AfterCodeChange_UpdatesHasLink` — смена `UpdateConfigCode` → `RefreshLinkState` → `HasLink`/`LinkDisplay` обновились.
8. `FindByCode_CaseInsensitive` (если добавлен новый метод в ConfigTypeMatcher) — поиск по коду регистронезависим.
9. Регрессия: существующие тесты VM (поле `ConfigurationName`/`ConfigurationVersion` и пр.) — без изменений.

Ручная проверка (пользователь 7OH, публикуется в комментарии):
- свойства базы с привязкой → вкладка «Платформа» показывает группу «Привязка» с наименованием/редакцией/URL;
- «Очистить» → подтверждение → поля сброшены, в F9 каталог строится автоопределением (0.3.9.309);
- «Связать…» → открывается окно связи → новый выбор → отображение обновилось;
- Linux/Avalonia — симметрично.

---

## C4. Риски и fallback

| Риск | Влияние | Митигация / fallback |
|------|---------|----------------------|
| Рассинхрон VM ↔ Infobase (окно связи мутирует объект напрямую) | Показ устаревшей привязки | После закрытия `ConfigUpdateLinkWindow` обязательный `RefreshLinkState()`; единый источник — поля Infobase. |
| Новая база (ещё не в репозитории): `PersistLink` в окне связи не находит объект | Привязка «теряется» до сохранения свойств | `ApplyTo` переносит 3 поля (C-1) → при «Сохранить» свойства запишутся; очистка работает аналогично. |
| Пользователь меняет привязку и нажимает «Отмена» в свойствах базы | Привязка уже изменена (поведение окна связи сохраняется немедленно) | Принято как согласованное с существующим UX (контекстное меню тоже сохраняет сразу); зафиксировать в комментарии к issue. |
| Список типовых пуст/ошибка загрузки | «—» вместо имени | fallback на `BuiltInConfigTypes.All` (как в `ConfigUpdateLinkWindow.LoadConfigs`); лог ошибки. |
| Регрессия F9/окна связи (общие поля модели) | Проверка обновлений ломается | Тесты `ConfigTypeMatcherTests`, `UpdateCheckCatalogTests`, `ConfigLinkItemViewModelTests`; поля не переименовываются. |
| Дублирование кода сохранения привязки (окно связи + очистка) | Разъезжание логики | Вынести `InfobaseLinkStorage.Save` (C-5, опционально) или копировать проверенный паттерн `PersistLink`. |

---

## C5. Критерии готовности

### C5.1 Технические

- `dotnet test` зелёный (включая C3.1–C3.9); кросс-сборка Linux без ошибок.
- `LoadFrom`/`ApplyTo` переносят поля привязки; `HasLink`/`LinkDisplay` корректны (включая «—»).
- Все новые тексты локализованы ru/en; группа «Привязка» присутствует в WPF и Avalonia.

### C5.2 Ручные (пользователь 7OH)

1. База, привязанная ранее → свойства → вкладка «Платформа» → группа «Привязка» показывает «Бухгалтерия предприятия · 3.0 · …/Accounting30».
2. Кнопка «Очистить» → запрос подтверждения → поля сброшены → F9 строит каталог автоопределением (конфигурация указана в свойствах).
3. Кнопка «Связать…» → окно связи → выбрать ЗУП → после закрытия группа показывает новую привязку.
4. База БЕЗ привязки → группа показывает «—», «Очистить» неактивна.
5. Повторить на Linux (Avalonia).

---

## C6. Перечень затрагиваемых файлов

- [`Configuration Management/ViewModels/ConnectionSettingsViewModel.cs`](Configuration%20Management/ViewModels/ConnectionSettingsViewModel.cs) — C-1.
- [`Configuration Management/Views/ConnectionSettingsWindow.xaml`](Configuration%20Management/Views/ConnectionSettingsWindow.xaml) — C-2 (WPF).
- [`Configuration Management/Views/ConnectionSettingsWindow.xaml.cs`](Configuration%20Management/Views/ConnectionSettingsWindow.xaml.cs) — C-3 (WPF).
- [`Configuration Management/Views/ConnectionSettingsWindow.Avalonia.cs`](Configuration%20Management/Views/ConnectionSettingsWindow.Avalonia.cs) — C-4 (Avalonia).
- [`Configuration Management/Services/ConfigTypeMatcher.cs`](Configuration%20Management/Services/ConfigTypeMatcher.cs) — C-6 (опционально `FindByCode`).
- [`Configuration Management/Localization/Languages/ru.json`](Configuration%20Management/Localization/Languages/ru.json) / [`en.json`](Configuration%20Management/Localization/Languages/en.json) — C-7.
- [`ConfigurationManagement.Tests/ConnectionSettingsViewModelTests.cs`](ConfigurationManagement.Tests/ConnectionSettingsViewModelTests.cs) — C3; `ConfigurationManagement.Tests/ConfigTypeMatcherTests.cs` — C3.8 (если добавлен метод).
- README/CHANGELOG — C-8.
- Без изменений (переиспользуется): [`ConfigUpdateLinkWindow.xaml.cs`](Configuration%20Management/Views/ConfigUpdateLinkWindow.xaml.cs) / [`.Avalonia.cs`](Configuration%20Management/Views/ConfigUpdateLinkWindow.Avalonia.cs), [`Infobase.cs`](Configuration%20Management/Models/Infobase.cs), [`OneCConfigType.cs`](Configuration%20Management/Models/OneCConfigType.cs), [`OneCUpdatesService.cs`](Configuration%20Management/Services/OneCUpdatesService.cs).

---

## C7. Порядок передачи в задачу-исполнитель (code)

1. Реализация строго по C-1…C-8; версия csproj → `0.3.9.312` (4 поля); CHANGELOG `## [0.3.9.312]`; README.
2. `dotnet test` + кросс-сборка Linux.
3. Сборка артефактов и релиз — по шаблону сводного плана (T12/T13).
4. Публикация комментария в **#346** (черновик `publish/comment-346-0.3.9.312.md`): ответ на новое предложение (группа «Привязка» реализована), чек-лист C5.2, ссылка на релиз. Без дублирования ответа про 0.3.9.309. Issue не закрывать.