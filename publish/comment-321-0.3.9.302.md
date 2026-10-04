Исправлено в версии **0.3.9.302** (Windows/WPF и Linux/Avalonia).

**Что было.**

После фикса 0.3.9.300 поля в окне правки/добавления строки релиза выглядели «чужеродными»: в [`EditionEditWindow.xaml`](Configuration%20Management/Views/EditionEditWindow.xaml) в `Window.Resources` был объявлен локальный неявный стиль `TextBox` (свой фон, рамка, отступы, без скруглённого шаблона и акцентных триггеров), который переопределял общий вид полей именно в этом окне. Окно-«сосед» [`ConfigTypeEditWindow.xaml`](Configuration%20Management/Views/ConfigTypeEditWindow.xaml) использовало дефолтный стиль MaterialDesign — тоже не совпадающий с общим видом полей других окон правки.

**Что сделано.**

1. [`Views/EditionEditWindow.xaml`](Configuration%20Management/Views/EditionEditWindow.xaml): удалён локальный неявный стиль `TextBox`; поля (`NameBox`, `RedBox`, `SubRedBox`, `UrlOverrideBox`) теперь используют общий `ModernTextBox` (скругление, акцентная рамка при наведении/фокусе, единая высота), как в остальных окнах правки приложения (CreateInfobase, GroupEdit и др.).
2. [`Views/ConfigTypeEditWindow.xaml`](Configuration%20Management/Views/ConfigTypeEditWindow.xaml): поля (`CodeBox`, `NameBox`, `ConfigNameBox`, `NickBox`) приведены к тому же общему стилю `ModernTextBox` — окно правки конфигурации и окно правки релиза выглядят единообразно.
3. Linux/Avalonia уже была консистентна (поля используют `ControlThemes.ModernTextBox`) — изменений не потребовалось.

**Как проверить:** обновитесь до **0.3.9.302**, откройте «Утилиты → Типовые конфигурации» → «Добавить…/Изменить…»: поля по виду идентичны полям других окон правки (скругление, рамка, подсветка при фокусе), не выглядят «серыми» или «чужеродными», текст вводится; окно правки релиза и окно правки конфигурации выглядят единообразно. Проверено в светлой и тёмной теме, WPF и Avalonia.