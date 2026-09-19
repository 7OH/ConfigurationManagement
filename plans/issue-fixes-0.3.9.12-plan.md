# План исправлений issue #263 и #261 — версия 0.3.9.12

Дата плана: 2026-09-19.
Источник: [`plans/issues-analysis-2026-09-19.md`](plans/issues-analysis-2026-09-19.md), данные issues `issues_open_latest.json`.
Текущая версия: `0.3.9.11` (см. [`_release/0.3.9.11.md`](_release/0.3.9.11.md)).
Предлагаемая следующая версия: **`0.3.9.12`**.

> **Процессное правило (оба issues):** issue НЕ закрывать автоматически ботом. После реализации и выпуска получить подтверждение автора (`7OH`) и только затем закрывать.

---

## Выбор версии и обоснование

- Текущая последняя версия `0.3.9.11`.
- Оба issues — **повторные исправления** мест, которые уже правились в серии `0.3.9.x`:
  - #263 уже затрагивался в `0.3.9.4`, `0.3.9.6`, `0.3.9.11`;
  - #261 уже затрагивался в `0.3.9.4`, `0.3.9.8`, `0.3.9.11`.
- Выбранный подход по #263 — вариант Б (честная документация/коррекция текстов локализации), а не новая функциональность. Это patch-уровень, а не минорное расширение возможностей.
- Поэтому версия **`0.3.9.12`** (третий сегмент — патч), а не `0.3.10.0`. Если бы был выбран вариант А (#263) с реализацией реальных разработческих инструментов — тогда был бы оправдан бамп до `0.3.10.0`.

Версия поднимается во всех четырёх полях (`<Version>`, `<AssemblyVersion>`, `<FileVersion>`, `<InformationalVersion>`) в `Configuration Management.csproj`, как и в `0.3.9.11` (см. [`_release/0.3.9.11.md`](_release/0.3.9.11.md:25)).

---

## Issue #263 «Нужны пояснения к режиму функциональности»

### Выбранный подход: вариант Б — честная документация текущей эквивалентности

**Обоснование отказа от варианта А (реальная разница):**
- Вариант А требует реализации разработческих инструментов конфигуратора (пакеты `.dt/.cf`, доступ к хранилищу конфигурации, раздел разработческих команд), которые сейчас отсутствуют. Это масштабная функциональность, не запрошенная как фича, и не пропорционально объёму тикет-итерации.
- Запрос автора сформулирован как «пояснения для людей»: «И всё таки - для людей - чем отличаются режимы?». Это вопрос о прозрачности и честности подсказок, а не требование добавить новое поведение.
- Тексты подсказок уже трижды правились (`0.3.9.4`, `0.3.9.6`, `0.3.9.11`), но до сих пор **обещают несуществующее** отличие («плюс инструменты конфигуратора»), тогда как фактически `IsSystemMenuRestricted` ограничивает меню только в режиме «Пользователь», а «Специалист»/«Разработчик» дают одинаковый полный доступ (см. [`MainViewModel.Functional.cs`](Configuration Management/ViewModels/MainViewModel.Functional.cs:50), [`MainViewModel.Avalonia.Functional.cs`](Configuration Management/ViewModels/MainViewModel.Avalonia.Functional.cs:41), [`MainWindow.Columns.cs`](Configuration Management/Views/MainWindow.Columns.cs:68)).
- Вариант Б устраняет корень претензии («состав меню одинаковый» / «непрозрачность») путём приведения текста к факту и явного признания эквивалентности + указания на дорожную карту.

### Изменения локализации

Файлы: [`ru.json`](Configuration Management/Localization/Languages/ru.json:38), [`en.json`](Configuration Management/Localization/Languages/en.json:38).

Заменить три ключа в `ru.json`:
```jsonc
"FunctionalMode.SpecialistHint": "Режим «Специалист»: полный набор операций со списком баз (редактирование, удаление, выгрузки, администрирование).",
"FunctionalMode.DeveloperHint": "Режим «Разработчик»: на текущем этапе полностью совпадает со «Специалистом» — тот же полный доступ ко всем операциям со списком баз. Разработческие инструменты конфигуратора запланированы на последующие этапы.",
"Settings.General.FunctionalModeHint": "Режим функциональности задаёт доступный набор возможностей:\n• «Пользователь» — только запуск, системное меню скрыто;\n• «Специалист» — полный набор операций со списком баз;\n• «Разработчик» — на текущем этапе совпадает со «Специалистом».\n\nСейчас «Специалист» и «Разработчик» дают одинаковый полный доступ, и состав меню не различается. Реальные разработческие инструменты конфигуратора появятся на последующих этапах дорожной карты."
```

Аналогично в `en.json`:
```jsonc
"FunctionalMode.SpecialistHint": "\"Specialist\" mode: the full set of operations with the infobase list (edit, delete, dumps, administration).",
"FunctionalMode.DeveloperHint": "\"Developer\" mode: currently fully matches \"Specialist\" — the same full access to all infobase-list operations. Configuration developer tools are planned for the upcoming roadmap stages.",
"Settings.General.FunctionalModeHint": "The functionality mode defines the available capabilities:\n• \"User\" — launch only, the system menu is hidden;\n• \"Specialist\" — the full set of operations with the infobase list;\n• \"Developer\" — currently matches \"Specialist\".\n\nCurrently \"Specialist\" and \"Developer\" provide the same full access and the menus do not differ. Real configuration developer tools will be added in upcoming roadmap stages."
```

### Синхронизация с фактическим кодом (документация)

Файл [`FunctionalMode.cs`](Configuration Management/Models/FunctionalMode.cs:3) — привести XML-документацию enum к факту, чтобы код не обещал различия:
- В описании `Specialist`/`Developer` убрать утверждения про «разработческие инструменты конфигуратора» и явно указать, что на текущем этапе оба режима дают одинаковый полный доступ, а разработческие инструменты запланированы.
- Аналогично согласовать комментарий в [`MainWindow.Columns.cs`](Configuration Management/Views/MainWindow.Columns.cs:72) (там уже корректно сказано «в остальных режимах меню показывается полностью» — проверить, не править без необходимости).

### UI (без изменений логики — тексты приходят из локализации)

Подсказки уже выводятся из ключей локализации и перерисуются автоматически:
- Avalonia: [`SettingsWindow.Avalonia.cs`](Configuration Management/Views/SettingsWindow.Avalonia.cs:275) — `UpdateFunctionalModeHints()` берёт `FunctionalMode.UserHint/SpecialistHint/DeveloperHint`.
- WPF: [`SettingsWindow.xaml.cs`](Configuration Management/Views/SettingsWindow.xaml.cs:268) — `InitializeFunctionalModeUi()/UpdateFunctionalModeHint()` — тексты те же ключи.
- Вводное пояснение `Settings.General.FunctionalModeHint` уже отображается (Avalonia — [`SettingsWindow.Avalonia.cs`](Configuration Management/Views/SettingsWindow.Avalonia.cs:240)).
- Проверить, что нигде не осталось «сырых» ключей (поиск по `FunctionalMode.*` в `.xaml`/`.axaml` и коде).

### Ответ автору (в комментарии issue #263)

Составить понятный ответ: «Сейчас „Специалист“ и „Разработчик“ действительно дают одинаковый полный доступ; отличие планируется в виде разработческих инструментов конфигуратора на следующих этапах. Подсказки приведены в соответствие с этим». Закрыть issue только после подтверждения автором.

### Критерии готовности #263
- Подсказки `SpecialistHint`/`DeveloperHint`/`Settings.General.FunctionalModeHint` (ru и en) больше НЕ обещают несуществующего различия и прямо указывают на эквивалентность на текущем этапе.
- XML-документация в [`FunctionalMode.cs`](Configuration Management/Models/FunctionalMode.cs) соответствует факту.
- Ни в одном UI не показываются сырые ключи локализации.
- Сохранена обратная совместимость канонических строк режима (`FunctionalModes.*`), сохранённый в `settings.json` режим не ломается.
- Автор подтвердил, что пояснение понятно; issue закрыто вручную (не ботом).

---

## Issue #261 «ESC и открытая подсказка»

### Диагноз
`CloseOpenToolTips()` возвращает `false`, когда тултип открыт: Avalonia-обход [`CloseIn(this)`](Configuration Management/Views/MainWindow.Avalonia.Hotkeys.cs:319) ищет владельца по `ToolTip.GetIsOpen(control)` в визуальном дереве окна. Открытый ToolTip рендерится попапом в оверлейном слое `TopLevel` (`OverlayLayer`), который не всегда входит в `GetVisualChildren()` окна, а `ToolTip.GetIsOpen` на владельце может не отражать фактически показанный попап. Из-за этого ветка Esc→в трей выполняется раньше ([`OnWindowKeyDown`](Configuration Management/Views/MainWindow.Avalonia.Hotkeys.cs:202)) — окно сворачивается, тултип «висит».

### Решение: детерминированное отслеживание открытого владельца тултипа

Основной механизм — подписка на изменение присоединённого свойства `ToolTip.IsOpenProperty` на уровне класса. Это не зависит от того, где физически отрисован попап, и потому надёжно.

#### Avalonia/Linux — файлы [`MainWindow.Avalonia.Hotkeys.cs`](Configuration Management/Views/MainWindow.Avalonia.Hotkeys.cs) и [`MainWindow.Avalonia.cs`](Configuration Management/Views/MainWindow.Avalonia.cs)

1. **Поле отслеживания.** Добавить в `MainWindow`:
   ```csharp
   private Control? _openToolTipOwner;
   ```

2. **Регистрация класс-обработчика** в конструкторе окна рядом с существующим Tunnel-хендлером ESC (после строки 147 в [`MainWindow.Avalonia.cs`](Configuration Management/Views/MainWindow.Avalonia.cs)):
   ```csharp
   ToolTip.IsOpenProperty.Changed.AddClassHandler<Control>(OnToolTipIsOpenChanged);
   ```
   Обработчик:
   ```csharp
   private void OnToolTipIsOpenChanged(Control owner, AvaloniaPropertyChangedEventArgs e)
   {
       if (e.NewValue is true) _openToolTipOwner = owner;
       else if (ReferenceEquals(_openToolTipOwner, owner)) _openToolTipOwner = null;
   }
   ```
   Это записывает владельца любого ToolTip сразу при его открытии — независимо от того, находится ли владелец в дереве главного окна или в оверлейном слое.

3. **Переписать `CloseOpenToolTips()`** ([`MainWindow.Avalonia.Hotkeys.cs`](Configuration Management/Views/MainWindow.Avalonia.Hotkeys.cs:293)) — сначала закрыть по сохранённому владельцу, затем страховочный обход:
   ```csharp
   private bool CloseOpenToolTips()
   {
       var closed = false;

       // Основной путь: владелец открытого тултипа, записанный класс-обработчиком.
       if (_openToolTipOwner is not null && ToolTip.GetIsOpen(_openToolTipOwner))
       {
           ToolTip.SetIsOpen(_openToolTipOwner, false);
           _openToolTipOwner = null;
           closed = true;
       }

       // Страховка: обходим оверлейный слой TopLevel — там живут открытые попапы ToolTip.
       if (!closed && this.GetTopLevel()?.OverlayLayer is OverlayLayer overlay)
       {
           foreach (var child in overlay.Children)
           {
               if (child is PopupHost { HostedPopup: { IsOpen: true } popup }
                   && popup.PlacementTarget is Control target)
               {
                   ToolTip.SetIsOpen(target, false);
                   closed = true;
               }
           }
       }

       // Старый обход визуального дерева + фокуса оставляем как резервный.
       if (!closed)
       {
           CloseIn(this);
           if (FocusManager?.GetFocusedElement() is Visual focused)
               for (var node = focused; node is not null; node = node.GetVisualParent())
                   if (node is Control control && ToolTip.GetIsOpen(control))
                   {
                       ToolTip.SetIsOpen(control, false);
                       closed = true;
                   }
       }

       return closed;

       void CloseIn(Visual node) { /* прежняя логика */ }
   }
   ```
   > Примечание для исполнителя: точные типы (`OverlayLayer`, `PopupHost`, `HostedPopup`) сверить с версией Avalonia в проекте; при недоступности `PopupHost` — итерировать по `overlay.Children` как `IPopupHost`/`Visual` и проверять открытые `Popup` по `IsOpen`. Имя `TopLevel.OverlayLayer` подтвердить по фактической сборке.

4. **Порядок обработчиков уже корректен**: Tunnel-хендлер [`OnPreviewKeyDownCloseToolTips`](Configuration Management/Views/MainWindow.Avalonia.Hotkeys.cs:134) срабатывает раньше `OnWindowKeyDown` и ставит `e.Handled=true`, если тултип закрыт — значит ветка Esc→трей не выполнится на этом же нажатии. Повторный ESC, когда тултипа уже нет, уводит окно в трей. Дополнительная проверка `TopmostModalDialog()`/`HasOpenModalDialog()` сохраняется как есть.

5. **Фокус и маршрутизация.** Тултипы в Avalonia обычно не забирают клавиатурный фокус, поэтому `KeyDown` маршрутизируется от сфокусированного элемента вверх через главное окно, и Tunnel-хендлер окна перехватит ESC. Если при верификации выяснится, что попап тултипа перехватывает клавишу (отдельный топлевел), дополнительно навесить такой же Tunnel-обработчик на попапы: `overlay.Children` через тот же класс-обработчик отслеживания — записи `_openToolTipOwner` достаточно для закрытия, а `e.Handled` проставит сам попап.

#### WPF/Windows — файл [`MainWindow.Hotkeys.cs`](Configuration Management/Views/MainWindow.Hotkeys.cs)

- Текущая реализация [`CloseOpenToolTips()`](Configuration Management/Views/MainWindow.Hotkeys.cs:314) через `TryCloseToolTip` (объект `ToolTip.IsOpen=false`, строка 389) уже надёжна для WPF (закрывает реальный попап по объекту). Основная проблема наблюдалась у пользователя в актуальной версии (вероятно, Avalonia/Linux).
- Для единообразия и защиты от «висящего» тултипа на WPF опционально добавить аналогичное отслеживание последнего открытого владельца: подписку на `ToolTip.IsOpenProperty` (WPF: `DependencyPropertyDescriptor.FromProperty(ToolTipService.IsOpenProperty, typeof(UIElement)).AddValueChanged(...)`) либо просто оставить текущий обход по курсору/фокусу, который уже присутствует в [`CloseToolTipByMouseOrFocus`](Configuration Management/Views/MainWindow.Hotkeys.cs:364).
- **Рекомендация:** не менять рабочую WPF-ветку без подтверждённого дефекта; перед релизом прогнать ручной сценарий (ESC при открытом тултипе) на Windows. Если поведение воспроизводится — добавить отслеживание владельца по аналогии с Avalonia.

### Проверка на месте создания тултипов
Множество `ToolTip.SetTip(...)`/`ToolTip.Tip` в главном окне (например, [`MainWindow.Avalonia.Tree.cs`](Configuration Management/Views/MainWindow.Avalonia.Tree.cs) и другие partial-файлы) не требуют правок: новый механизм опирается на глобальное свойство `ToolTip.IsOpenProperty`, а не на каждую точку установки подсказки. Проверить лишь, что все подсказки используют стандартный `ToolTip` (не кастомные попапы).

### Критерии готовности #261
- Открыта ЛЮБАЯ подсказка в главном окне → первый ESC закрывает подсказку, окно остаётся на месте (не сворачивается).
- Повторный ESC сворачивает/прячет окно в трей, без «висящего» тултипа на экране.
- Тултип не остаётся видимым после сворачивания окна.
- Поведение проверено на Avalonia/Linux (X11/Wayland) и на WPF/Windows.
- Инвариант сохранён: `CloseOpenToolTips()` гарантированно возвращает `true` при реально открытой подсказке, поэтому Esc→трей не срабатывает на том же нажатии.
- Автор подтвердил исправление на актуальной версии; issue закрыт вручную (не ботом).

---

## Порядок реализации (режим Code)

1. **Локализация #263** — обновить `ru.json` и `en.json` (три ключа в каждом), убедиться в отсутствии сырых ключей.
2. **Документация #263** — синхронизировать XML-док-комментарии в [`FunctionalMode.cs`](Configuration Management/Models/FunctionalMode.cs).
3. **Механизм #261 (Avalonia)** — поле `_openToolTipOwner`, класс-обработчик `OnToolTipIsOpenChanged`, переписать `CloseOpenToolTips()` (+страховочный обход `OverlayLayer`).
4. **Верификация #261 (WPF)** — ручной сценарий; внести симметричный фикс только при воспроизведении дефекта.
5. **Версия** — поднять `0.3.9.12` в `Configuration Management.csproj` (4 поля).
6. **Релиз-ноты** — файл `_release/0.3.9.12.md` с описанием Fix #263 и Fix #261; обновить `CHANGELOG.md`.
7. **Сборка и ручная проверка** обеих платформ по критериям готовности.
8. **Ответ автора** в комментариях #263 и #261; закрыть issues только после подтверждения (НЕ автозакрытие ботом).

## Затронутые файлы (сводно)
- `Configuration Management/Localization/Languages/ru.json`
- `Configuration Management/Localization/Languages/en.json`
- `Configuration Management/Models/FunctionalMode.cs` (только XML-документация)
- `Configuration Management/Views/MainWindow.Avalonia.Hotkeys.cs`
- `Configuration Management/Views/MainWindow.Avalonia.cs` (регистрация обработчика)
- `Configuration Management/Views/MainWindow.Hotkeys.cs` (только при подтверждённом WPF-дефекте)
- `Configuration Management.csproj` (версия 0.3.9.12)
- `_release/0.3.9.12.md` (новый файл), `CHANGELOG.md`