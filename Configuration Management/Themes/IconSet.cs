namespace Configuration_Management.Themes;

/// <summary>
/// Набор векторных иконок интерфейса. Один и тот же ключ иконки
/// (имя Geometry из Icons.xaml / Icons.axaml) может иметь несколько
/// вариантов начертания; активный набор выбирается полноценной темой
/// (<see cref="ThemeDefinition"/>) и применяется менеджером тем.
/// </summary>
public enum IconSet
{
    /// <summary>Залитые (Fluent) иконки — набор по умолчанию.</summary>
    Fluent,

    /// <summary>Контурные (Outline) иконки.</summary>
    Outline,

    /// <summary>Материальные (Material) иконки — заполненные формы Material Design.</summary>
    Material,

    /// <summary>Иконки в духе macOS (SF Symbols) — тонкие контурные линии.</summary>
    MacOS,

    /// <summary>Иконки в стиле классической Windows XP — заполненные формы.</summary>
    WindowsXp,

    /// <summary>Стеклянные (Glass) иконки — лёгкие контурные линии.</summary>
    Glass
}