Исправлено в версии **0.3.9.300** (Windows/WPF и Linux/Avalonia).

**Что было.**

1. Окно правки строки версии `EditionEditWindow` было низким (`Height=360`) — поле URL (`UrlOverrideBox`) не помещалось, URL версии не был виден без прокрутки.
2. Поля формы и окно поиска конфигураций выглядели «серыми» — создавалось впечатление, что они недоступны для правки.
3. Главному окну списка `ConfigTypesEditWindow` не хватало стандартной кнопки максимизации.

**Что сделано.**

1. [`Views/EditionEditWindow.xaml`](Configuration%20Management/Views/EditionEditWindow.xaml): окно стало выше (`Height` 360 → 520, `MinHeight` 330 → 470) — поле URL видно сразу, без прокрутки; аналогично для Avalonia ([`EditionEditWindow.Avalonia.cs`](Configuration%20Management/Views/EditionEditWindow.Avalonia.cs)).
2. Убрана «серость» полей формы и окна поиска (стили TextBox/фон, [`ConfigTypeEditWindow.Avalonia.cs`](Configuration%20Management/Views/ConfigTypeEditWindow.Avalonia.cs)) — поля выглядят доступными для редактирования.
3. [`Views/ConfigTypesEditWindow.xaml`](Configuration%20Management/Views/ConfigTypesEditWindow.xaml): `ResizeMode="CanResize"` — главному окну списка добавлена стандартная кнопка максимизации (разворот на весь экран); аналогично для Avalonia.

**Как проверить:** обновитесь до **0.3.9.300**, откройте «Типовые конфигурации» → «Добавить…/Изменить…»: поле URL видно без прокрутки, поля редактируются и не выглядят недоступными; окно списка разворачивается на весь экран.