# icongen — генератор наборов иконок тем

Создаёт словари ресурсов `Configuration Management/Themes/Icons<Набор>.xaml` (WPF) и
`Icons<Набор>.axaml` (Avalonia) так, чтобы **каждый** ключ базового набора
(`Icons.xaml` / `Icons.axaml`) и **каждый** используемый вид значка
`materialDesign:PackIcon` имел вариант в каждой теме.

| Набор      | Тема              | Источник                                   | Лицензия                  |
|------------|-------------------|--------------------------------------------|---------------------------|
| Material   | Material Design   | Google Material Symbols Rounded (Fill)     | Apache License 2.0        |
| MacOS      | macOS             | Phosphor Icons — Light                     | MIT                       |
| WindowsXp  | Windows XP        | Phosphor Icons — Fill                      | MIT                       |
| Glass      | Glass             | Phosphor Icons — Duotone (+ слой `.Back`)  | MIT                       |
| Outline    | Синяя / Зелёная   | рукописные + Phosphor Icons — Regular      | MIT                       |

Карта соответствий — `icon_map.py` (`ICON_KEYS`, `PACK_KINDS`, `MATERIAL_SYMBOLS`).
Генератор сам находит все `PackIcon Kind="…"` и `PackIconKind.…` в проекте и
останавливается со списком, если для какого-то ключа нет соответствия.

## Запуск

```sh
git clone --depth 1 https://github.com/phosphor-icons/core.git phosphor
git clone --depth 1 https://github.com/Templarian/MaterialDesign-SVG.git mdi
git clone --depth 1 --filter=blob:none --sparse https://github.com/google/material-design-icons.git gmd
# (в gmd: git sparse-checkout set --no-cone "symbols/web/*/materialsymbolsrounded/*_fill1_24px.svg")

python3 tools/icongen/generate_icon_sets.py --mdi mdi/svg --phosphor phosphor/assets \
    --material gmd/symbols/web
```

Пути Phosphor (256×256) и Material Symbols (`0 -960 960 960`) пересчитываются в
сетку 24×24 и получают префикс `F1` (правило заливки Nonzero). Повторный запуск
идемпотентен; рукописные глифы кнопок окна (`GlyphWindow*.<Набор>`) и рукописные
контурные иконки сохраняются.

## Лицензии источников

- Phosphor Icons — © 2023 Phosphor Icons, MIT License — https://github.com/phosphor-icons/core
- Material Symbols — © Google, Apache License 2.0 — https://github.com/google/material-design-icons
- Material Design Icons — Pictogrammers Free License — https://github.com/Templarian/MaterialDesign-SVG
