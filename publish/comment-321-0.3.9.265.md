Исправлено в версии **0.3.9.265** (Windows/WPF и Linux/Avalonia). Это первая часть правок окна «Типовые конфигурации» — данные и окно редактора; вторая часть (поиск/отбор, ширина списка, подсветка перекрытия типовой строки, пояснение «Сегмент УРЛ», отображение учётной записи) выйдет в следующей версии 0.3.9.266.

**Что было не так.**

1. Правка типовой конфигурации (со звёздочкой), например ЗУП, после сохранения создавала ДУБЛЬ строки: рядом с предопределённой ЗУП появлялась её копия без звезды, а не изменённая та же запись.
2. Добавление редакций в окне «Изменить конфигурацию» визуально не оповещало: кнопка «Добавить» нажата, но строки в списке не появлялись — «дважды добавленный релиз 3.0» был виден только в копии строки ЗУП после сохранения.
3. Окно «Изменить конфигурацию» было слишком низким — список редакций показывал меньше 3–4 строк.

**Причина.**

- Окно списка строило строки как «все встроенные + все пользовательские» без правила замены: [`RebuildRows`](Configuration%20Management/Views/ConfigTypesEditWindow.xaml.cs) не использовало единый загрузчик [`CustomConfigTypesStore.LoadAll`](Configuration%20Management/Services/CustomConfigTypesStore.cs), который уже умеет подменять встроенную запись её пользовательской копией по коду. Правка встроенной добавляла копию (`OverridesBuiltIn=true`), и в списке оказывалось две строки ЗУП.
- Список редакций хранился в обычном `List` — `Add` не уведомлял UI, поэтому новые строки появлялись только после переоткрытия окна/сохранения.
- Высота окна редактора `Height=580`, список редакций ограничен `MaxHeight=110`.

**Как исправлено.**

1. Правило объединения вынесено в единый публичный метод [`CustomConfigTypesStore.MergeAll`](Configuration%20Management/Services/CustomConfigTypesStore.cs): пользовательская копия предопределённой заменяет встроенную с тем же кодом, обычные пользовательские записи добавляются следом. Окно списка строит строки им же из своего рабочего буфера (ссылки на экземпляры сохраняются, поэтому правка/удаление по ссылке работают) — дублей строк больше нет. Изменённая типовая строка помечается значком ✎★ с подсказкой «пользовательская копия предопределённой (перекрывает типовую)».
2. Список редакций — `ObservableCollection`, после «Добавить» новая редакция сразу видна, выбрана и прокручивается в зону видимости (обе платформы).
3. Окно «Изменить конфигурацию» увеличено: `Height=720` / `MinHeight=620`, список редакций до 200 px (видны 3–4 строки); окно подгоняется под содержимое и не уходит за нижний край экрана через [`WindowSizeMath`](Configuration%20Management/Services/WindowSizeMath.cs) (`ClampHeight`/`FitTop`), как в остальных окнах.

Файлы:

- [`Services/CustomConfigTypesStore.cs`](Configuration%20Management/Services/CustomConfigTypesStore.cs) — единое правило `MergeAll` (используется и `LoadAll()`, и окном списка);
- [`ViewModels/ConfigTypeItemViewModel.cs`](Configuration%20Management/ViewModels/ConfigTypeItemViewModel.cs) — признак перекрытия `IsOverride`;
- WPF [`Views/ConfigTypesEditWindow.xaml`](Configuration%20Management/Views/ConfigTypesEditWindow.xaml) и [`Views/ConfigTypesEditWindow.xaml.cs`](Configuration%20Management/Views/ConfigTypesEditWindow.xaml.cs) — строки через `MergeAll`, значок ✎★;
- WPF [`Views/ConfigTypeEditWindow.xaml`](Configuration%20Management/Views/ConfigTypeEditWindow.xaml) и [`Views/ConfigTypeEditWindow.xaml.cs`](Configuration%20Management/Views/ConfigTypeEditWindow.xaml.cs) — высота окна, список редакций, `WindowSizeMath`;
- Avalonia [`Views/ConfigTypesEditWindow.Avalonia.cs`](Configuration%20Management/Views/ConfigTypesEditWindow.Avalonia.cs) и [`Views/ConfigTypeEditWindow.Avalonia.cs`](Configuration%20Management/Views/ConfigTypeEditWindow.Avalonia.cs) — зеркальные правки;
- локализация `ru.json`/`en.json` — ключ `Updates.OverrideHint`.

**Тесты.**

[`CustomConfigTypesStoreTests`](ConfigurationManagement.Tests/CustomConfigTypesStoreTests.cs): дубли переопределений одного кода → в `LoadAll` одна строка (без дубля); `MergeAll` возвращает те же ссылки на пользовательские записи (правка/удаление по ссылке работают); порядок «встроенные с копиями → обычные пользовательские». Новый [`ConfigTypeItemViewModelTests`](ConfigurationManagement.Tests/ConfigTypeItemViewModelTests.cs): `IsOverride`/`IsBuiltIn`, доступность удаления только у пользовательских строк. Дополнительно стабилизирован флак-тест параллельной загрузки [`PlatformDownloadTests`](ConfigurationManagement.Tests/PlatformDownloadTests.cs) (ожидание финального отчёта прогресса). Полный набор `dotnet test` зелёный (1502), кросс-сборка Linux (`dotnet build -p:BuildLinux=true`) без ошибок.

**Как проверить в 0.3.9.265.**

1. Откройте «Типовые конфигурации», нажмите «Изменить» у ЗУП (★), добавьте редакцию «3.0» и сохраните — в списке останется ОДНА строка ЗУП (с пометкой ✎★ и обеими редакциями), а не две.
2. Повторите правку той же строки — дубли не плодятся; «Восстановить типовые» возвращает исходный набор.
3. В окне «Изменить конфигурацию» нажмите «Добавить» в разделе «Редакции» — новая строка появляется сразу, окно выше и показывает 3–4 строки списка.