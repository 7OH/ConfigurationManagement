using Configuration_Management.Models;

namespace Configuration_Management.Themes;

/// <summary>
/// Чистая (без UI) логика полноценных тем (Задача 2): выбор определения по
/// идентификатору с возвратом к светлой теме, копирование темы с другим базовым
/// вариантом и маппинг стиля кнопок окна / набора иконок в параметры ресурсов
/// приложения. Именно эти части <see cref="ThemeManager"/> (обеих платформ)
/// тестируются без Application.Current.
/// </summary>
public static class ThemeFeatureMap
{
    /// <summary>
    /// Ключи ресурсов приложения, выставляемые по стилю кнопок окна и набору иконок.
    /// Стили кнопок управления окном (WindowControlButton/WindowControlCloseButton)
    /// начнут читать эти ключи в Задаче 4, переключение наборов иконок — в Задаче 5.
    /// </summary>
    public const string WindowButtonHoverBrushKey = "WindowButtonHoverBrush";
    public const string WindowButtonCloseHoverBrushKey = "WindowButtonCloseHoverBrush";
    public const string WindowButtonCornerRadiusKey = "WindowButtonCornerRadius";
    public const string WindowButtonSizeKey = "WindowButtonSize";
    public const string IconSetNameKey = "IconSetName";

    /// <summary>Все ключи ресурсов полноценной темы (в порядке выдачи).</summary>
    public static readonly string[] AllResourceKeys =
    {
        WindowButtonHoverBrushKey, WindowButtonCloseHoverBrushKey,
        WindowButtonCornerRadiusKey, WindowButtonSizeKey, IconSetNameKey
    };

    /// <summary>
    /// Параметры стиля кнопок управления окном: цвет подложки наведения, цвет подложки
    /// кнопки «закрыть» (в формате #RRGGBB), радиус скругления подложки и размер кнопки.
    /// </summary>
    public readonly record struct WindowButtonMetrics(
        string HoverHex, string CloseHoverHex, double CornerRadius, double Size);

    /// <summary>
    /// Находит определение полноценной темы по идентификатору. Неизвестный или пустой
    /// идентификатор ошибкой не является: применяется светлая тема по умолчанию.
    /// </summary>
    public static ThemeDefinition Resolve(string? id)
        => ThemeCatalog.Find(id) ?? ThemeCatalog.Find("light")!;

    /// <summary>
    /// Копия темы с другим базовым вариантом: тот же Id, имя, стиль кнопок, набор иконок
    /// и акцент, изменённый <see cref="ThemeDefinition.IsDark"/> и клонированная схема
    /// (чтобы применение палитры не мутировало объекты <see cref="ThemeCatalog.BuiltIn"/>).
    /// </summary>
    public static ThemeDefinition WithBaseVariant(ThemeDefinition? theme, bool isDark)
    {
        var source = theme ?? Resolve(null);
        return new ThemeDefinition
        {
            Id = source.Id,
            Name = source.Name,
            IsDark = isDark,
            Scheme = source.Scheme?.Clone()
                     ?? (isDark ? ColorScheme.CreateDark() : ColorScheme.CreateLight()),
            ButtonStyle = source.ButtonStyle,
            Icons = source.Icons,
            AccentColor = source.AccentColor
        };
    }

    /// <summary>
    /// Параметры стиля кнопок управления окном: <see cref="WindowButtonStyle.Fluent"/> —
    /// круглая подложка (нынешний вид), <see cref="WindowButtonStyle.Minimal"/> — тонкая
    /// плоская кнопка почти без подложки, <see cref="WindowButtonStyle.Classic"/> —
    /// прямоугольная кнопка классического вида.
    /// </summary>
    public static WindowButtonMetrics ButtonMetrics(WindowButtonStyle style) => style switch
    {
        WindowButtonStyle.Minimal => new WindowButtonMetrics("#E2E8F0", "#E81123", 4, 40),
        WindowButtonStyle.Classic => new WindowButtonMetrics("#E2E8F0", "#E81123", 0, 34),
        _ => new WindowButtonMetrics("#FFF3CD", "#E81123", 15, 46)
    };

    /// <summary>
    /// Имя набора иконок для ресурса <see cref="IconSetNameKey"/>.
    /// Используется словарями иконок (Задача 5) для выбора активного набора.
    /// </summary>
    public static string IconSetId(IconSet icons) => icons switch
    {
        IconSet.Outline => "Outline",
        _ => "Fluent"
    };

    /// <summary>
    /// Разбирает имя набора из ресурса <see cref="IconSetNameKey"/>; неизвестное или
    /// пустое значение даёт <see cref="IconSet.Fluent"/> (набор по умолчанию).
    /// </summary>
    public static IconSet ParseIconSet(string? name)
        => string.Equals(name, "Outline", StringComparison.OrdinalIgnoreCase)
            ? IconSet.Outline
            : IconSet.Fluent;

    /// <summary>
    /// Ключ ресурса геометрии иконки для активного набора (Задача 5): для
    /// <see cref="IconSet.Outline"/> — ключ с суффиксом ".Outline" (словарь
    /// IconsOutline.xaml / IconsOutline.axaml), для <see cref="IconSet.Fluent"/> —
    /// исходный ключ (Icons.xaml / Icons.axaml). Оба словаря подключены в ресурсы
    /// приложения одновременно, поэтому суффикс исключает конфликт ключей.
    /// </summary>
    public static string IconKeyForSet(string key, IconSet set)
        => set == IconSet.Outline ? key + ".Outline" : key;

    /// <summary>
    /// Ключи ресурсов для резолва иконки в порядке приоритета: ключ активного набора,
    /// затем исходный ключ (fallback на Fluent — только для Outline, когда контурного
    /// варианта нет), затем запасная «папка». Используется конвертером иконок и
    /// <c>IconHelper</c> обеих платформ.
    /// </summary>
    public static IReadOnlyList<string> IconResolutionCandidates(string key, IconSet set)
    {
        var effective = IconKeyForSet(key, set);
        if (set == IconSet.Outline && !string.Equals(effective, key, StringComparison.Ordinal))
            return new[] { effective, key, "IconFolder" };
        return new[] { effective, "IconFolder" };
    }

    /// <summary>
    /// Ключ локализации отображаемого имени полноценной темы по её идентификатору
    /// (используется в настройках обеих платформ, Задача 3). Неизвестный или пустой
    /// идентификатор даёт ключ светлой темы — как и <see cref="Resolve"/>.
    /// </summary>
    public static string ThemeNameKey(string? id) => id?.ToLowerInvariant() switch
    {
        "dark" => "Theme.Dark",
        "blue" => "Theme.Blue",
        "green" => "Theme.Green",
        _ => "Theme.Light"
    };

    /// <summary>
    /// Суффикс, добавляемый к имени пользовательской схемы, если оно совпадает с именем
    /// встроенной полноценной темы (issue #376): так встроенная тема не затирает
    /// пользовательскую и наоборот.
    /// </summary>
    public const string CustomSchemeSuffix = " (пользовательская)";

    /// <summary>
    /// true, если имя совпадает с именем встроенной полноценной темы
    /// (Светлая/Тёмная/Синяя/Зелёная) без учёта регистра и крайних пробелов.
    /// </summary>
    public static bool IsBuiltInThemeName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;
        var trimmed = name.Trim();
        foreach (var theme in ThemeCatalog.BuiltIn)
        {
            if (string.Equals(theme.Name, trimmed, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Возвращает безопасное имя пользовательской схемы: если имя совпадает с именем
    /// встроенной полноценной темы, к нему добавляется <see cref="CustomSchemeSuffix"/>
    /// (issue #376). Иначе имя возвращается без изменений (с обрезкой крайних пробелов).
    /// </summary>
    public static string ResolveCustomSchemeName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return name ?? string.Empty;
        var trimmed = name.Trim();
        return IsBuiltInThemeName(trimmed) ? trimmed + CustomSchemeSuffix : trimmed;
    }
}