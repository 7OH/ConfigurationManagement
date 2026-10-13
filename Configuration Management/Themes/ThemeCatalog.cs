using Configuration_Management.Models;

namespace Configuration_Management.Themes;

/// <summary>
/// Каталог полноценных тем: встроенные (Светлая, Тёмная, Синяя, Зелёная)
/// и обёртка пользовательской цветовой схемы в <see cref="ThemeDefinition"/>.
/// Порядок встроенных тем стабилен (используется в настройках и тестах).
/// </summary>
public static class ThemeCatalog
{
    /// <summary>Встроенные темы в стабильном порядке: Светлая, Тёмная, Синяя, Зелёная.</summary>
    public static IReadOnlyList<ThemeDefinition> BuiltIn { get; } = new[]
    {
        Create("light", "Светлая", false, ColorScheme.CreateLight(), WindowButtonStyle.Fluent, IconSet.Fluent, "#FDBF00"),
        Create("dark", "Тёмная", true, ColorScheme.CreateDark(), WindowButtonStyle.Fluent, IconSet.Fluent, "#FFB300"),
        Create("blue", "Синяя", false, ColorScheme.CreateBlue(), WindowButtonStyle.Fluent, IconSet.Fluent, "#2563EB"),
        Create("green", "Зелёная", false, ColorScheme.CreateGreen(), WindowButtonStyle.Fluent, IconSet.Fluent, "#16A34A")
    };

    /// <summary>
    /// Находит встроенную тему по идентификатору (без учёта регистра).
    /// Неизвестный или пустой идентификатор возвращает null.
    /// </summary>
    public static ThemeDefinition? Find(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return null;
        foreach (var theme in BuiltIn)
        {
            if (string.Equals(theme.Id, id, StringComparison.OrdinalIgnoreCase))
                return theme;
        }
        return null;
    }

    /// <summary>
    /// Оборачивает пользовательскую цветовую схему в полноценную тему.
    /// Базовый вариант задаётся параметром <paramref name="isDark"/>; стиль кнопок
    /// и набор иконок берутся по умолчанию (Fluent), акцент — из схемы.
    /// </summary>
    public static ThemeDefinition WrapCustom(ColorScheme scheme, bool isDark)
    {
        scheme ??= ColorScheme.CreateLight();
        scheme.Normalize();
        return new ThemeDefinition
        {
            Id = "custom",
            Name = scheme.Name,
            IsDark = isDark,
            Scheme = scheme,
            ButtonStyle = WindowButtonStyle.Fluent,
            Icons = IconSet.Fluent,
            AccentColor = scheme.AccentColor ?? scheme.PaletteValue(isDark, "AccentColor")
        };
    }

    private static ThemeDefinition Create(string id, string name, bool isDark, ColorScheme scheme,
        WindowButtonStyle buttonStyle, IconSet icons, string accentColor) => new()
    {
        Id = id,
        Name = name,
        IsDark = isDark,
        Scheme = scheme,
        ButtonStyle = buttonStyle,
        Icons = icons,
        AccentColor = accentColor
    };
}