Исправлено в версии **0.3.9.121** (Windows/WPF и Linux/Avalonia).

**Что было.**

После 0.3.9.117 часть надписей окон «Сравнение конфигураций» по-прежнему не читалась
(«Пока без изменений» + скриншоты). Остались незакрытыми: подписи радио-кнопок режима,
текст комбобоксов (выбранный элемент и выпадающий список), статусы объектов в отчёте
(янтарный и серый сливались с белым фоном светлой схемы) и имена объектов в дереве отчёта.

**Как исправлено** (обе платформы):

- **Радио-кнопки режима сравнения** — цвет текста из темы (`TextPrimaryBrush`):
  - Windows/WPF — [`Views/ConfigDiffSetupWindow.xaml`](Configuration%20Management/Views/ConfigDiffSetupWindow.xaml):
    в ресурсы окна добавлен стиль `TargetType="RadioButton"` →
    `Foreground="{DynamicResource TextPrimaryBrush}"`;
  - Linux/Avalonia — [`Views/ConfigDiffSetupWindow.Avalonia.cs`](Configuration%20Management/Views/ConfigDiffSetupWindow.Avalonia.cs):
    `ThemeBrushes.Bind(_modeBaseRadio/_modeCfRadio, ..., "TextPrimaryBrush")`.
- **Комбобоксы** (`База`, `Платформа 1С`) — выбранный элемент и выпадающий список
  читаемы в обеих схемах: WPF использует штатные `ModernComboBox`/`ModernComboBoxItem`
  (текст `TextPrimaryBrush`), Avalonia — `ThemeBrushes.Bind` на сам `ComboBox`
  (`TemplatedControl.ForegroundProperty`) и на `ItemTemplate` списка баз.
- **Статусы объектов в отчёте** — жёсткие цвета заменены на более тёмные оттенки,
  читаемые и на светлой, и на тёмной схеме (синхронно на обеих платформах):
  - «Добавлен» — `#16A34A` (было `#22C55E`),
  - «Изменён» — `#D97706` (было `#F59E0B`),
  - «Удалён» — `#DC2626` (было `#EF4444`),
  - «Без изменений» — `#64748B` (было `#94A3B8`).
  Файлы: [`Views/ConfigDiffResultWindow.xaml`](Configuration%20Management/Views/ConfigDiffResultWindow.xaml)
  (`StatusTextStyle`) и [`Views/ConfigDiffResultWindow.Avalonia.cs`](Configuration%20Management/Views/ConfigDiffResultWindow.Avalonia.cs)
  (`StatusBrush`).
- **Имена объектов в дереве отчёта** — явный `Foreground="{DynamicResource TextPrimaryBrush}"`
  у `TextBlock` уровней «тип» и «объект` (не зависят от контекста DataTemplate).
- **Окна прогресса** — уже были привязаны к теме в 0.3.9.117: текст этапа и полоса
  читаемы в обеих схемах, изменений не потребовалось.

Тесты `dotnet test` (418) и сборка Linux-ветки (`dotnet build -p:BuildLinux=true`)
проходят без ошибок.

**Как проверить.**

1. Откройте «Сравнить конфигурации» (Сервис → Сравнение конфигураций) на Windows и Linux.
2. Проверьте читаемость: описание, радио-кнопки режима, метки, комбобоксы
   (выбранный элемент и раскрытый список), подсказки, окно прогресса.
3. Выполните сравнение и проверьте отчёт: шапку, сводку, имена объектов
   и статусы («Добавлен»/«Изменён»/«Удалён»/«Без изменений»).
4. Переключите тему (Настройки → Тема/цветовая схема) между светлой и тёмной —
   все надписи читаемы в обоих режимах.