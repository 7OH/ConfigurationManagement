namespace Configuration_Management.Themes;

/// <summary>
/// Стиль кнопок управления окном (свернуть/развернуть/закрыть).
/// Определяет внешний вид кнопок в заголовке окна: скругление, размер,
/// поведение при наведении. Используется полноценными темами
/// (<see cref="ThemeDefinition"/>) и применяется менеджером тем.
/// </summary>
public enum WindowButtonStyle
{
    /// <summary>Современный «плавный» стиль (скруглённые кнопки, как в Windows 11).</summary>
    Fluent,

    /// <summary>Минималистичный стиль (компактные кнопки без фона).</summary>
    Minimal,

    /// <summary>Классический стиль (прямоугольные кнопки, как в Windows 10).</summary>
    Classic
}