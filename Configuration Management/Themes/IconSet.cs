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
    Outline
}