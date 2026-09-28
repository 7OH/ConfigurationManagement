Исправлено в версии **0.3.9.117** (Windows/WPF и Linux/Avalonia).

**Что было.**

В окнах сравнения конфигураций — настройки (`ConfigDiffSetupWindow`), отчёта
(`ConfigDiffResultWindow`) и прогресса (`ConfigDiffProgressWindow`) — надписи не читались:
цвета текста не были привязаны к теме приложения, и в светлой схеме текст сливался
с подложкой (и наоборот в тёмной).

**Как стало.**

Цвет текста всех надписей теперь берётся из темы и автоматически меняется при её смене:

- Linux/Avalonia — `Foreground` заголовков, описаний, меток полей, подсказок, строк
  результата, meta-строки и сводки привязывается через
  `ThemeBrushes.Bind(tb, TextBlock.ForegroundProperty, "TextPrimaryBrush"/"TextSecondaryBrush")`,
  как в остальных окнах приложения:
  [`Views/ConfigDiffSetupWindow.Avalonia.cs`](Configuration%20Management/Views/ConfigDiffSetupWindow.Avalonia.cs),
  [`Views/ConfigDiffResultWindow.Avalonia.cs`](Configuration%20Management/Views/ConfigDiffResultWindow.Avalonia.cs),
  [`Views/ConfigDiffProgressWindow.Avalonia.cs`](Configuration%20Management/Views/ConfigDiffProgressWindow.Avalonia.cs);
- Windows/WPF — оконные стили `Style TargetType="TextBlock"` уже задавали цвет из
  динамического ресурса `TextPrimaryBrush` (он определён и в светлой, и в тёмной теме);
  дополнительно привязан цвет текста этапа в окне прогресса
  ([`Views/ConfigDiffProgressWindow.cs`](Configuration%20Management/Views/ConfigDiffProgressWindow.cs)),
  у которого нет XAML-стиля.

Статусы объектов оставлены фирменными жёсткими цветами — они читаемы в обеих схемах:
«Добавлен» `#22C55E`, «Изменён» `#F59E0B`, «Удалён» `#EF4444` (как и раньше).

**Как проверить.**

1. Откройте «Сравнить конфигурации» (главное меню → Сервис → Сравнение конфигураций).
2. Проверьте читаемость всех надписей: описание, метки полей, подсказки платформы,
   заголовок и сводку отчёта, строки объектов и статусы.
3. Переключите тему (Настройки → Тема/цветовая схема) между светлой и тёмной —
   цвета текста меняются вместе с темой, всё читается в обоих режимах.
4. Повторите на Windows (WPF) и Linux (Avalonia).