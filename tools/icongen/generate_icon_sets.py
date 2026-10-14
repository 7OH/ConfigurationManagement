#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Генератор полных наборов иконок для тем Configuration Management.

Создаёт словари ресурсов Themes/Icons<Set>.xaml (WPF, Geometry) и
Themes/Icons<Set>.axaml (Avalonia, StreamGeometry) для наборов:

    Material  — Google Material Symbols Rounded (Fill)     → IconsMaterial.*
    MacOS     — Phosphor Light (тонкие линии, как SF)      → IconsMacOS.*
    WindowsXp — Phosphor Fill (плотные заливки)            → IconsWindowsXp.*
    Glass     — Phosphor Duotone (передний слой + .Back)   → IconsGlass.*
    Outline   — Phosphor Regular, только недостающие ключи → IconsOutline.* (дополнение)

Ключи:
    Icon<Name>.<Set>          — вариант ключа из Icons.xaml / Icons.axaml;
    Icon<Name>.Back.<Set>     — фоновый полупрозрачный слой (только Glass);
    Pack.<Kind>.<Set>         — вариант значка materialDesign:PackIcon (WPF);
    Pack.<Kind>.Back.<Set>    — фоновый слой значка PackIcon (только Glass);
    GlyphWindow<Glyph>.<Set>  — штриховые глифы кнопок окна свернуть/развернуть/
                                восстановить/закрыть (рукописные, переносятся из прежних
                                словарей; ключ IconClose при этом — обычная залитая иконка).

Phosphor рисуется в квадрате 256×256 — пути масштабируются в сетку 24×24, как у MDI
и базового набора (холст 24×24 в IconHelper и шаблоне PackIcon). Перед путём Phosphor
ставится префикс «F1» (правило заливки Nonzero), которое понимают и WPF, и Avalonia.

Запуск:
    python3 tools/icongen/generate_icon_sets.py --mdi <MaterialDesign-SVG/svg> \
        --phosphor <phosphor core/assets> --material <material-design-icons/symbols/web> \
        [--repo <корень репозитория>]
"""
import argparse
import os
import re
import sys
from xml.sax.saxutils import escape

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from icon_map import (ICON_KEYS, PACK_KINDS, PACK_MDI_OVERRIDES, WINDOW_GLYPH_KEYS,  # noqa: E402
                      MATERIAL_SYMBOLS, TEMPLATE_GLYPHS, WINDOW_GLYPHS_BY_SET)

SETS = [
    # (имя набора, источник, вариант Phosphor, описание)
    ("Material", "material", None, "Material Symbols Rounded (Fill) — Material Design 3"),
    ("MacOS", "phosphor", "light", "Phosphor Light — тонкие линии в духе SF Symbols"),
    ("WindowsXp", "phosphor", "fill", "Phosphor Fill — плотные заливки классической Windows XP"),
    ("Glass", "phosphor", "duotone", "Phosphor Duotone — передний слой и полупрозрачный фон (.Back)"),
]
OUTLINE = ("Outline", "phosphor", "regular", "Phosphor Regular — дополнение контурного набора")

WINDOW_GLYPHS = ["GlyphWindowMinimize", "GlyphWindowMaximize", "GlyphWindowRestore", "GlyphWindowClose"]

# --------------------------------------------------------------------------- path utils

_TOKEN = re.compile(r"[MmLlHhVvCcSsQqTtAaZz]|[-+]?(?:\d+\.?\d*|\.\d+)(?:[eE][-+]?\d+)?")
_ARGS = {"M": 2, "L": 2, "H": 1, "V": 1, "C": 6, "S": 4, "Q": 4, "T": 2, "A": 7, "Z": 0}


def _fmt(v):
    s = ("%.3f" % v).rstrip("0").rstrip(".")
    return "0" if s in ("-0", "") else s


def transform_path(d, scale, ox=0.0, oy=0.0):
    """
    Переводит путь SVG в сетку 24×24: абсолютные координаты — (v + сдвиг) × scale,
    относительные — × scale; у дуги A масштабируются радиусы и конечная точка, а угол
    и флаги (в SVG они могут идти слитно, «011») переносятся как есть.
    """
    out = []
    i = 0
    n = len(d)
    cmd = None
    nums = []

    def read_number():
        nonlocal i
        m = re.compile(r"[\s,]*([-+]?(?:\d+\.?\d*|\.\d+)(?:[eE][-+]?\d+)?)").match(d, i)
        if not m:
            raise ValueError("number expected at %d in %r" % (i, d[i:i + 30]))
        i = m.end()
        return float(m.group(1))

    def read_flag():
        nonlocal i
        m = re.compile(r"[\s,]*([01])").match(d, i)
        if not m:
            raise ValueError("flag expected at %d" % i)
        i = m.end()
        return int(m.group(1))

    ws = re.compile(r"[\s,]*")
    first_rel_move = False
    while True:
        i = ws.match(d, i).end()
        if i >= n:
            break
        ch = d[i]
        if ch.isalpha():
            cmd = ch
            i += 1
            if not out and cmd == "m":
                # Первая «m» в SVG абсолютна, а следующие за ней пары — относительные «l».
                cmd = "M"
                first_rel_move = True
            if cmd in "Zz":
                out.append("Z")
                continue
        elif cmd is None:
            raise ValueError("path must start with a command")
        up = cmd.upper()
        if up == "Z":
            raise ValueError("numbers after Z")
        if up == "A":
            rx, ry, rot = read_number(), read_number(), read_number()
            large, sweep = read_flag(), read_flag()
            x, y = read_number(), read_number()
            if cmd == "A":
                x, y = x + ox, y + oy
            vals = [_fmt(rx * scale), _fmt(ry * scale), _fmt(rot), str(large), str(sweep),
                    _fmt(x * scale), _fmt(y * scale)]
        else:
            raw = [read_number() for _ in range(_ARGS[up])]
            if cmd.isupper():
                if up == "H":
                    raw = [raw[0] + ox]
                elif up == "V":
                    raw = [raw[0] + oy]
                else:
                    raw = [v + (ox if k % 2 == 0 else oy) for k, v in enumerate(raw)]
            vals = [_fmt(v * scale) for v in raw]
        if up in ("H", "V"):
            out.append(cmd + vals[0])
        else:
            pairs = [vals[k] + "," + vals[k + 1] for k in range(0, len(vals) - 1, 2)]
            if up == "A":
                pairs = [vals[0] + "," + vals[1], vals[2], vals[3], vals[4], vals[5] + "," + vals[6]]
            out.append(cmd + " ".join(pairs))
        # Повтор команды без буквы: M превращается в L (как в SVG).
        if cmd == "M" and first_rel_move:
            cmd = "l"
            first_rel_move = False
        elif cmd == "M":
            cmd = "L"
        elif cmd == "m":
            cmd = "l"
    return " ".join(out)


def validate_path(d):
    """Грубая проверка: путь начинается с M/F1 M и разбирается токенизатором без мусора."""
    body = d[3:] if d.startswith("F1 ") else d
    rest = _TOKEN.sub(" ", body).replace(",", " ").strip()
    if rest:
        raise ValueError("unexpected characters in path: %r" % rest[:40])
    if not body.lstrip().startswith(("M", "m")):
        raise ValueError("path does not start with M")


# --------------------------------------------------------------------------- sources

_PATH_RE = re.compile(r"<path([^>]*)\sd=\"([^\"]+)\"([^>]*)/?>")


def load_mdi(mdi_dir, name):
    fn = os.path.join(mdi_dir, name + ".svg")
    if not os.path.exists(fn):
        return None
    svg = open(fn, encoding="utf-8").read()
    paths = [m.group(2) for m in _PATH_RE.finditer(svg)]
    if not paths:
        return None
    return " ".join(paths), None


# Для Fill-набора фигура «неотмеченный флажок» по смыслу пустая:
# их заливка исказила бы значение, поэтому они берутся из Regular.
FILL_AS_REGULAR = {"square"}


def load_phosphor(ph_dir, variant, name):
    if variant == "fill" and name in FILL_AS_REGULAR:
        variant = "regular"
    fn = os.path.join(ph_dir, variant, "%s-%s.svg" % (name, variant)) if variant != "regular" \
        else os.path.join(ph_dir, "regular", name + ".svg")
    if not os.path.exists(fn):
        return None
    svg = open(fn, encoding="utf-8").read()
    front, back = [], []
    for m in _PATH_RE.finditer(svg):
        attrs = m.group(1) + m.group(3)
        d = transform_path(m.group(2), 24.0 / 256.0)
        (back if "opacity" in attrs else front).append(d)
    if not front:
        return None
    f = "F1 " + " ".join(front)
    b = ("F1 " + " ".join(back)) if back else None
    return f, b


def load_material_symbol(ms_dir, name):
    """Material Symbols Rounded (Fill 1, 24px): viewBox «0 -960 960 960» → сетка 24×24."""
    fn = os.path.join(ms_dir, name, "materialsymbolsrounded", "%s_fill1_24px.svg" % name)
    if not os.path.exists(fn):
        return None
    svg = open(fn, encoding="utf-8").read()
    # У большинства файлов viewBox «0 -960 960 960», у части старых — без viewBox (сразу 24×24).
    vb = re.search(r'viewBox="([-\d.]+)[ ,]+([-\d.]+)[ ,]+([\d.]+)[ ,]+([\d.]+)"', svg)
    if vb:
        minx, miny, w = float(vb.group(1)), float(vb.group(2)), float(vb.group(3))
    else:
        minx, miny = 0.0, 0.0
        wm = re.search(r'width="([\d.]+)"', svg)
        w = float(wm.group(1)) if wm else 24.0
    paths = [transform_path(m.group(2), 24.0 / w, -minx, -miny) for m in _PATH_RE.finditer(svg)]
    if not paths:
        return None
    return "F1 " + " ".join(paths), None


def kebab(kind):
    s = re.sub(r"(?<=[a-z0-9])([A-Z])", r"-\1", kind)
    s = re.sub(r"(?<=[A-Z])([A-Z][a-z])", r"-\1", s)
    return s.lower()


# --------------------------------------------------------------------------- repo scan

def repo_pack_kinds(repo):
    kinds = set()
    root = os.path.join(repo, "Configuration Management")
    for dp, dn, fns in os.walk(root):
        if any(p in dp for p in (os.sep + "bin", os.sep + "obj")):
            continue
        for fn in fns:
            p = os.path.join(dp, fn)
            if fn.endswith(".xaml"):
                text = open(p, encoding="utf-8-sig").read()
                kinds.update(re.findall(r'PackIcon[^>]*?\sKind="([A-Za-z0-9]+)"', text, re.S))
                # Kind в сеттерах стилей/триггеров PackIcon.
                kinds.update(re.findall(r'Property="Kind" Value="([A-Za-z0-9]+)"', text))
            elif fn.endswith(".cs"):
                kinds.update(re.findall(r"PackIconKind\.([A-Za-z0-9]+)",
                                        open(p, encoding="utf-8-sig").read()))
    return sorted(kinds)


def repo_icon_keys(repo):
    keys = set()
    for fn in ("Icons.xaml", "Icons.axaml"):
        t = open(os.path.join(repo, "Configuration Management", "Themes", fn), encoding="utf-8-sig").read()
        keys.update(re.findall(r'<(?:Stream)?Geometry x:Key="(Icon[A-Za-z0-9]+)"', t))
    return sorted(keys)


def existing_entries(path, suffix):
    """Рукописные записи прежнего словаря набора: ключ → геометрия."""
    if not os.path.exists(path):
        return {}
    t = open(path, encoding="utf-8-sig").read()
    res = {}
    for m in re.finditer(r'<(?:Stream)?Geometry x:Key="([A-Za-z0-9.]+)">([^<]+)</', t):
        if m.group(1).endswith("." + suffix):
            res[m.group(1)[: -len(suffix) - 1]] = m.group(2).strip()
    return res


# --------------------------------------------------------------------------- writers

def write_dict(path, wpf, set_name, desc, entries, source_note):
    tag = "Geometry" if wpf else "StreamGeometry"
    if wpf:
        head = ('<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"\n'
                '                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">\n')
    else:
        head = ('<ResourceDictionary xmlns="https://github.com/avaloniaui"\n'
                '                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">\n')
    lines = [head]
    lines.append("    <!--\n")
    lines.append("        Icons%s.%s — набор иконок «%s» (%s).\n" % (set_name, "xaml" if wpf else "axaml", set_name, desc))
    lines.append("        ФАЙЛ СГЕНЕРИРОВАН tools/icongen/generate_icon_sets.py — не редактируйте вручную,\n")
    lines.append("        правьте карту tools/icongen/icon_map.py и перезапустите генератор.\n")
    lines.append("        Источник: %s.\n" % source_note)
    lines.append("        Ключи: Icon<Имя>.%s — варианты ключей базового набора; Pack.<Kind>.%s — варианты\n" % (set_name, set_name))
    lines.append("        значков materialDesign:PackIcon; *.Back.%s — полупрозрачный фоновый слой.\n" % set_name)
    lines.append("        Резолв и живое переключение: ThemeManager.ApplyIconSet переопределяет базовые\n")
    lines.append("        ключи ресурсами активного набора (ThemeFeatureMap.IconOverrides).\n")
    lines.append("    -->\n\n")
    for section, items in entries:
        lines.append("    <!-- ===================== %s ===================== -->\n" % section)
        for key, data in items:
            validate_path(data)
            lines.append('    <%s x:Key="%s">%s</%s>\n' % (tag, escape(key), escape(data), tag))
        lines.append("\n")
    lines.append("</ResourceDictionary>\n")
    with open(path, "w", encoding="utf-8", newline="\n") as f:
        f.write("".join(lines))


def build_set(set_def, args, icon_keys, kinds, themes_dir, missing):
    name, source, variant, desc = set_def
    is_outline = name == "Outline"

    def fetch(mdi_name, ph_name):
        if source == "mdi":
            return load_mdi(args.mdi, mdi_name)
        if source == "material":
            return load_material_symbol(args.material, MATERIAL_SYMBOLS.get(ph_name, ""))
        return load_phosphor(args.phosphor, variant, ph_name)

    icons = []
    handwritten_wpf = existing_entries(os.path.join(themes_dir, "Icons%s.xaml" % name), name)
    handwritten_ava = existing_entries(os.path.join(themes_dir, "Icons%s.axaml" % name), name)
    for key in icon_keys:
        if key in WINDOW_GLYPH_KEYS:
            continue
        if key not in ICON_KEYS:
            missing.append("ICON_KEYS: " + key)
            continue
        mdi_name, ph_name = ICON_KEYS[key]
        r = fetch(mdi_name, ph_name)
        if r is None:
            missing.append("%s: %s (%s/%s)" % (name, key, mdi_name, ph_name))
            continue
        icons.append((key + "." + name, r[0]))
        if r[1]:
            icons.append((key + ".Back." + name, r[1]))

    packs = []
    for kind in kinds:
        mdi_name = PACK_MDI_OVERRIDES.get(kind, kebab(kind))
        ph_name = PACK_KINDS.get(kind)
        if source in ("phosphor", "material") and ph_name is None:
            missing.append("PACK_KINDS: " + kind)
            continue
        r = fetch(mdi_name, ph_name)
        if r is None:
            missing.append("%s: Pack %s (%s/%s)" % (name, kind, mdi_name, ph_name))
            continue
        packs.append(("Pack.%s.%s" % (kind, name), r[0]))
        if r[1]:
            packs.append(("Pack.%s.Back.%s" % (kind, name), r[1]))

    return icons, packs, handwritten_wpf, handwritten_ava


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--mdi", required=True)
    ap.add_argument("--phosphor", required=True)
    ap.add_argument("--material", required=True, help="каталог symbols/web репозитория google/material-design-icons")
    ap.add_argument("--repo", default=os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..")))
    args = ap.parse_args()

    themes_dir = os.path.join(args.repo, "Configuration Management", "Themes")
    icon_keys = repo_icon_keys(args.repo)
    kinds = repo_pack_kinds(args.repo)
    missing = []
    results = []
    for set_def in SETS + [OUTLINE]:
        results.append((set_def,) + build_set(set_def, args, icon_keys, kinds, themes_dir, missing))

    if missing:
        print("Не найдено соответствие для:\n  " + "\n  ".join(missing))
        return 1

    for set_def, icons, packs, hw_wpf, hw_ava in results:
        name, source, variant, desc = set_def
        note = {"mdi": "Material Design Icons, Pictogrammers Free License",
                "material": "Google Material Symbols Rounded, Apache License 2.0"}.get(
                    source, "Phosphor Icons (%s), MIT License" % variant)
        for wpf, hw in ((True, hw_wpf), (False, hw_ava)):
            entries = []
            if name == "Outline":
                # Рукописный контурный набор сохраняется целиком, генератор только дополняет.
                entries.append(("РУКОПИСНЫЕ КОНТУРНЫЕ ИКОНКИ", [(k + ".Outline", v) for k, v in sorted(hw.items())]))
            else:
                glyphs = []
                for g in WINDOW_GLYPH_KEYS:
                    if g in hw:
                        glyphs.append((g + "." + name, hw[g]))
                for g in WINDOW_GLYPHS:
                    # Штриховой глиф кнопки окна: карта WINDOW_GLYPHS_BY_SET, иначе прежний
                    # рукописный Icon<Глиф>.<Набор>.
                    src = WINDOW_GLYPHS_BY_SET.get(name, {}).get(g) or hw.get(g) \
                        or hw.get(g.replace("GlyphWindow", "Icon"))
                    if src:
                        glyphs.append((g + "." + name, src))
                entries.append(("ГЛИФЫ КНОПОК ОКНА (рукописные, штрих)", glyphs))
                tmpl = [(g + "." + name, d) for g, d in sorted(TEMPLATE_GLYPHS.get(name, {}).items())]
                if tmpl:
                    entries.append(("ГЛИФЫ ШАБЛОНОВ ЭЛЕМЕНТОВ (ComboBox, подменю, CheckBox)", tmpl))
            set_icons = icons
            if name == "Outline":
                # Генерируются только ключи, которых нет среди рукописных контурных.
                set_icons = [(k, v) for k, v in icons
                             if k.split(".")[0] not in hw]
            entries.append(("ИКОНКИ ИНТЕРФЕЙСА (Icon*)", set_icons))
            if wpf:
                entries.append(("ЗНАЧКИ PACKICON (Pack.<Kind>)", packs))
            ext = "xaml" if wpf else "axaml"
            write_dict(os.path.join(themes_dir, "Icons%s.%s" % (name, ext)), wpf, name, desc, entries, note)
        print("%-10s icons=%d packs=%d" % (name, len(icons), len(packs)))
    return 0


if __name__ == "__main__":
    sys.exit(main())
