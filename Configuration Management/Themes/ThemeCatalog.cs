using Configuration_Management.Models;

namespace Configuration_Management.Themes;

/// <summary>
/// Каталог полноценных тем: встроенные (Светлая, Тёмная, Синяя, Зелёная)
/// и обёртка пользовательской цветовой схемы в <see cref="ThemeDefinition"/>.
/// Порядок встроенных тем стабилен (используется в настройках и тестах).
/// </summary>
public static class ThemeCatalog
{
    /// <summary>
    /// Встроенные темы в стабильном порядке: Светлая, Тёмная, Синяя, Зелёная.
    /// Темы отличаются не только палитрой, но и стилем кнопок управления окном и
    /// набором иконок (issue #376): иначе выбор полноценной темы визуально менял бы
    /// только цвет. Светлая/Тёмная — Fluent (круглые кнопки, Fluent-иконки),
    /// Синяя — Classic (прямоугольные кнопки, контурные иконки),
    /// Зелёная — Minimal (плоские кнопки, контурные иконки).
    /// </summary>
    public static IReadOnlyList<ThemeDefinition> BuiltIn { get; } = new[]
    {
        Create("light", "Светлая", false, ColorScheme.CreateLight(), WindowButtonStyle.Fluent, IconSet.Fluent, "#FDBF00"),
        Create("dark", "Тёмная", true, ColorScheme.CreateDark(), WindowButtonStyle.Fluent, IconSet.Fluent, "#FFB300"),
        Create("blue", "Синяя", false, ColorScheme.CreateBlue(), WindowButtonStyle.Classic, IconSet.Outline, "#2563EB"),
        Create("green", "Зелёная", false, ColorScheme.CreateGreen(), WindowButtonStyle.Minimal, IconSet.Outline, "#16A34A"),
        // Задача 3: четыре новые темы — Material Design, macOS, Windows XP, Glass.
        // Каждая несёт собственную палитру, стиль кнопок окна и набор иконок.
        Create("material", "Material Design", false, ColorScheme.CreateMaterial(), WindowButtonStyle.Material, IconSet.Material, "#7C4DFF"),
        Create("macos", "macOS", false, ColorScheme.CreateMacOS(), WindowButtonStyle.MacOS, IconSet.MacOS, "#0A84FF"),
        Create("windowsxp", "Windows XP", false, ColorScheme.CreateWindowsXp(), WindowButtonStyle.WindowsXp, IconSet.WindowsXp, "#3B6EA5"),
        Create("glass", "Glass", false, ColorScheme.CreateGlass(), WindowButtonStyle.Glass, IconSet.Glass, "#007AFF")
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