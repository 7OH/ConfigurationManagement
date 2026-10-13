using Configuration_Management.Models;

namespace Configuration_Management.Themes;

/// <summary>
/// Полноценная тема оформления: цветовая схема (две палитры) + базовый вариант
/// (светлый/тёмный) + стиль кнопок управления окном + набор иконок + акцентный цвет.
/// Встроенные темы описаны в <see cref="ThemeCatalog"/>; пользовательские цветовые
/// схемы оборачиваются в полноценную тему методом <see cref="ThemeCatalog.WrapCustom"/>.
/// </summary>
public class ThemeDefinition
{
    /// <summary>Идентификатор темы ("light", "dark", "blue", "green").</summary>
    public string Id { get; set; } = "light";

    /// <summary>Отображаемое имя темы (например «Светлая»).</summary>
    public string Name { get; set; } = "Светлая";

    /// <summary>Базовый вариант темы: true — тёмный, false — светлый.</summary>
    public bool IsDark { get; set; }

    /// <summary>Цветовая схема (две палитры — светлая и тёмная).</summary>
    public ColorScheme Scheme { get; set; } = ColorScheme.CreateLight();

    /// <summary>Стиль кнопок управления окном.</summary>
    public WindowButtonStyle ButtonStyle { get; set; } = WindowButtonStyle.Fluent;

    /// <summary>Набор векторных иконок.</summary>
    public IconSet Icons { get; set; } = IconSet.Fluent;

    /// <summary>Акцентный цвет темы в формате #RRGGBB.</summary>
    public string AccentColor { get; set; } = "#FDBF00";
}