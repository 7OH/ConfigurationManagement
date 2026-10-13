# 0.3.12.38

## Добавлено

### Полноценные темы оформления

- **4 встроенные темы** — «Светлая», «Тёмная», «Синяя», «Зелёная» (акцентные цвета:
  синий `#2563EB`, зелёный `#16A34A`); выбор — в настройках (WPF — выпадающий список,
  Avalonia — переключатели), выбор сохраняется и восстанавливается после перезапуска.
- **Тема = цветовая схема + стиль кнопок управления окном + набор иконок + оформление
  меню «Утилиты»**: модель `ThemeDefinition`/`ThemeCatalog`, перечисления
  `WindowButtonStyle` (Fluent/Minimal/Classic) и `IconSet` (Fluent/Outline),
  `ColorScheme.AccentColor` + фабрики `CreateBlue()`/`CreateGreen()`,
  `AppSettings.FullTheme` с миграцией из прежнего `Theme` (Light/Dark → light/dark).
- **Применение темы** (`ThemeManager.ApplyFullTheme`, обе платформы): применяется
  цветовая схема (2 палитры), стиль кнопок управления окном и набор иконок, рассылается
  событие `ThemeChanged`; старт приложения и главное окно применяют сохранённую тему.
- **Кнопки управления окном** темизируются под стиль (Fluent/Minimal/Classic): радиус,
  размер, цвета hover/close (WPF — стили на DynamicResource, Avalonia —
  `WindowControlButton` с подпиской на `ThemeChanged`).
- **Иконки**: добавлен второй набор контурных иконок Outline (WPF
  `Themes/IconsOutline.xaml`, Avalonia `IconsOutline.axaml`); иконки резолвятся по
  активному набору через конвертер/`IconHelper` с бесшовным fallback на Fluent.
- **Меню «Утилиты»** темизируется: цвета групп/пунктов берутся из активной схемы,
  иконки — через активный набор, меню пересобирается при `ThemeChanged` (WPF переведён
  с PackIcon/MaterialDesign на собственную геометрию через конвертер).
- Пользовательские цветовые схемы (JSON) продолжают работать (обратная совместимость).
- Обе платформы: WPF и Avalonia.

## Тесты

Добавлены тесты (`ThemeDefinitionTests`, `ThemeCatalogTests`, `ThemeFeatureMapTests`,
`AppSettingsThemeMigrationTests`, `FullThemeSelectionTests`, `IconSetResolutionTests`,
`ColorSchemeThemeTests`, `UtilitiesMenuThemeTests`).

## Проверка

- Полный прогон тестов: 2698/2698 зелёных.
- Сборки Windows (WPF) и Linux (Avalonia) без ошибок.
- Ручной чек-лист: в настройках доступны 4 темы; переключение мгновенно меняет цвета,
  кнопки управления окном, иконки и оформление меню «Утилиты»; выбор сохраняется и
  восстанавливается после перезапуска; пользовательские цветовые схемы работают.
