using System.Text.Json;
using System.Text.Json.Serialization;
using Configuration_Management.Localization;

namespace Configuration_Management.Models;

/// <summary>
/// Цветовая схема (тема оформления) приложения: именованный набор из двух палитр —
/// для светлого (<see cref="LightColors"/>) и тёмного (<see cref="DarkColors"/>) режима.
/// Накладывается поверх базовой темы. Поддерживает выгрузку/загрузку в JSON-файл,
/// а также создание собственных тем.
/// </summary>
public class ColorScheme
{
    /// <summary>Название схемы (темы).</summary>
    public string Name { get; set; } = "Light";

    /// <summary>
    /// Устаревшее поле для чтения старых JSON-файлов (миграция в <see cref="Normalize"/>).
    /// В новые файлы не записывается.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool IsDark { get; set; }

    /// <summary>
    /// Устаревшее поле для чтения старых JSON-файлов (миграция в <see cref="Normalize"/>).
    /// В новые файлы не записывается.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, string>? Colors { get; set; }

    /// <summary>
    /// Палитра для светлого режима: ключ ресурса (имя Color-ресурса из темы, напр.
    /// <c>AccentColor</c>, либо имя кисти для ресурсов без отдельного цвета, напр.
    /// <c>ScrollThumbBrush</c>) → значение в формате #RRGGBB.
    /// </summary>
    public Dictionary<string, string> LightColors { get; set; } = new();

    /// <summary>Палитра для тёмного режима (тот же формат ключей, что у <see cref="LightColors"/>).</summary>
    public Dictionary<string, string> DarkColors { get; set; } = new();

    /// <summary>
    /// Ключи ресурсов цветов и соответствующие им ключи локализации подписей
    /// для редактора в настройках. Технический ключ ресурса (первый элемент)
    /// хранится и сравнивается — его НЕ переводим; переводится только подпись.
    /// </summary>
    private static readonly (string Key, string LabelKey)[] _definitionKeys = new (string, string)[]
    {
        ("AccentColor", "Color.Accent"),
        ("AccentHoverColor", "Color.AccentHover"),
        ("AccentPressedColor", "Color.AccentPressed"),
        ("SidebarColor", "Color.Sidebar"),
        ("SidebarHoverColor", "Color.SidebarHover"),
        ("SidebarSelectedColor", "Color.SidebarSelected"),
        ("ContentBackgroundColor", "Color.ContentBackground"),
        ("CardBackgroundColor", "Color.CardBackground"),
        ("BorderColor", "Color.Border"),
        ("TextPrimaryColor", "Color.TextPrimary"),
        ("TextSecondaryColor", "Color.TextSecondary"),
        ("TextOnAccentColor", "Color.TextOnAccent"),
        ("ButtonTextColor", "Color.ButtonText"),
        ("FavoriteColor", "Color.Favorite"),
        ("FolderColor", "Color.Folder"),
        ("FavoriteFolderColor", "Color.FavoriteFolder"),
        ("ItemHoverColor", "Color.ItemHover"),
        ("ItemSelectedColor", "Color.ItemSelected"),
        ("AvatarBackgroundColor", "Color.AvatarBackground"),
        ("AvatarTextColor", "Color.AvatarText"),
        ("SecondaryButtonBackgroundColor", "Color.SecondaryButtonBackground"),
        ("SecondaryButtonHoverColor", "Color.SecondaryButtonHover"),
        ("SecondaryButtonPressedColor", "Color.SecondaryButtonPressed"),
        ("TreeHoverColor", "Color.TreeHover"),
        ("TreeSelectedColor", "Color.TreeSelected"),
        ("ScrollTrackBrush", "Color.ScrollTrack"),
        ("ScrollThumbBrush", "Color.ScrollThumb"),
        ("ScrollThumbHoverBrush", "Color.ScrollThumbHover"),
        ("ScrollThumbPressedBrush", "Color.ScrollThumbPressed")
    };

    /// <summary>
    /// Возвращает упорядоченное описание редактируемых цветов:
    /// ключ ресурса и локализованная человекочитаемая подпись для редактора в настройках.
    /// </summary>
    public static IReadOnlyList<(string Key, string Label)> Definitions =>
        _definitionKeys.Select(d => (d.Key, LocalizationManager.T(d.LabelKey))).ToArray();

    /// <summary>Возвращает локализованную подпись для ключа цвета (если ключ неизвестен — сам ключ).</summary>
    public static string GetLabel(string key)
    {
        foreach (var (k, labelKey) in _definitionKeys)
        {
            if (string.Equals(k, key, StringComparison.OrdinalIgnoreCase))
                return LocalizationManager.T(labelKey);
        }
        return key;
    }

    /// <summary>Возвращает активную палитру (светлую или тёмную) по варианту темы.</summary>
    public Dictionary<string, string> Palette(bool dark)
        => dark ? DarkColors : LightColors;

    /// <summary>
    /// Возвращает значение цвета указанной палитры по ключу. Если ключа нет — значение
    /// по умолчанию для этого варианта (для неузнаваемых ключей — нейтральный запасной).
    /// </summary>
    public string PaletteValue(bool dark, string key)
    {
        var dict = Palette(dark);
        if (dict.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
            return value;

        var defaults = dark ? DefaultDarkPalette() : DefaultLightPalette();
        if (defaults.TryGetValue(key, out var def) && !string.IsNullOrWhiteSpace(def))
            return def;

        return key.EndsWith("Brush", StringComparison.OrdinalIgnoreCase)
            ? (dark ? "#3B4A5F" : "#B6C2D2")
            : (dark ? "#FFB300" : "#FDBF00");
    }

    /// <summary>
    /// Приводит схему к актуальному виду: переносит устаревшую одиночную палитру
    /// (<see cref="Colors"/>/<see cref="IsDark"/>) в соответствующее поле и заполняет
    /// отсутствующую палитру значениями по умолчанию. Вызывается при загрузке из JSON
    /// и перед применением схемы.
    /// </summary>
    public void Normalize()
    {
        if (Colors is { Count: > 0 })
        {
            var target = Palette(IsDark);
            var other = Palette(!IsDark);
            if (target.Count == 0)
            {
                foreach (var kvp in Colors)
                    target[kvp.Key] = kvp.Value;
            }
            if (other.Count == 0)
            {
                foreach (var kvp in (IsDark ? DefaultLightPalette() : DefaultDarkPalette()))
                    other[kvp.Key] = kvp.Value;
            }
            // Устаревшие поля больше не нужны.
            Colors = null;
        }

        FillMissing(LightColors, DefaultLightPalette());
        FillMissing(DarkColors, DefaultDarkPalette());
    }

    private static void FillMissing(Dictionary<string, string> target, Dictionary<string, string> defaults)
    {
        foreach (var kvp in defaults)
        {
            if (!target.ContainsKey(kvp.Key))
                target[kvp.Key] = kvp.Value;
        }
    }

    /// <summary>Создаёт копию схемы (независимые наборы цветов обеих палитр).</summary>
    public ColorScheme Clone() => new()
    {
        Name = Name,
        IsDark = IsDark,
        Colors = Colors is null ? null : new Dictionary<string, string>(Colors, StringComparer.Ordinal),
        LightColors = new Dictionary<string, string>(LightColors, StringComparer.Ordinal),
        DarkColors = new Dictionary<string, string>(DarkColors, StringComparer.Ordinal)
    };

    /// <summary>
    /// Собирает единую схему с двумя палитрами из устаревших полей настроек:
    /// старого одиночного <paramref name="active"/> и/или раздельных слотов
    /// <paramref name="light"/>/<paramref name="dark"/>. Возвращает нормализованную
    /// схему, готовую к применению и сохранению в новом формате.
    /// </summary>
    public static ColorScheme FromLegacy(ColorScheme? active, ColorScheme? light, ColorScheme? dark)
    {
        var hasSlots = light is not null || dark is not null;
        var source = hasSlots ? null : active;
        var merged = source?.Clone() ?? CreateLight();
        merged.Name = source?.Name ?? light?.Name ?? dark?.Name ?? "Светлая";

        if (hasSlots)
        {
            if (light is not null)
            {
                light.Normalize();
                merged.LightColors = new Dictionary<string, string>(light.Palette(false), StringComparer.Ordinal);
                if (light.DarkColors.Count > 0)
                    merged.DarkColors = new Dictionary<string, string>(light.DarkColors, StringComparer.Ordinal);
            }
            if (dark is not null)
            {
                dark.Normalize();
                merged.DarkColors = new Dictionary<string, string>(dark.Palette(true), StringComparer.Ordinal);
                if (dark.LightColors.Count > 0)
                    merged.LightColors = new Dictionary<string, string>(dark.LightColors, StringComparer.Ordinal);
            }
        }

        merged.Normalize();
        return merged;
    }

    /// <summary>Создаёт встроенную схему «Светлая» (несёт обе палитры).</summary>
    public static ColorScheme CreateLight() => Create("Светлая", false);

    /// <summary>Создаёт встроенную схему «Тёмная» (несёт обе палитры).</summary>
    public static ColorScheme CreateDark() => Create("Тёмная", true);

    /// <summary>
    /// Создаёт схему с палитрами по умолчанию для обоих режимов. Параметр <paramref name="isDark"/>
    /// сохраняется только в устаревшем поле <see cref="IsDark"/> (для совместимости); сама схема
    /// всегда содержит и светлую, и тёмную палитру.
    /// </summary>
    public static ColorScheme Create(string name, bool isDark)
    {
        var scheme = new ColorScheme { Name = name, IsDark = isDark };
        foreach (var (key, value) in DefaultLightPalette())
            scheme.LightColors[key] = value;
        foreach (var (key, value) in DefaultDarkPalette())
            scheme.DarkColors[key] = value;
        return scheme;
    }

    /// <summary>Палитра по умолчанию для светлого режима (соответствует LightTheme).</summary>
    private static Dictionary<string, string> DefaultLightPalette() => new(StringComparer.Ordinal)
    {
        ["AccentColor"] = "#FDBF00",
        ["AccentHoverColor"] = "#E0A800",
        ["AccentPressedColor"] = "#C49400",
        ["SidebarColor"] = "#1E293B",
        ["SidebarHoverColor"] = "#273549",
        ["SidebarSelectedColor"] = "#FDBF00",
        ["ContentBackgroundColor"] = "#F1F5F9",
        ["CardBackgroundColor"] = "#FFFFFF",
        ["BorderColor"] = "#E2E8F0",
        ["TextPrimaryColor"] = "#000000",
        ["TextSecondaryColor"] = "#64748B",
        ["TextOnAccentColor"] = "#FFFFFF",
        ["ButtonTextColor"] = "#000000",
        ["FavoriteColor"] = "#F59E0B",
        ["FolderColor"] = "#2D6CDF",
        ["FavoriteFolderColor"] = "#8B5CF6",
        ["ItemHoverColor"] = "#FFF3CD",
        ["ItemSelectedColor"] = "#FFE69C",
        ["AvatarBackgroundColor"] = "#FFF3CD",
        ["AvatarTextColor"] = "#8A6D00",
        ["SecondaryButtonBackgroundColor"] = "#FFF3CD",
        ["SecondaryButtonHoverColor"] = "#FFE69C",
        ["SecondaryButtonPressedColor"] = "#FFD54D",
        ["TreeHoverColor"] = "#FFF9E6",
        ["TreeSelectedColor"] = "#FFD54D",
        ["ScrollTrackBrush"] = "#E8EDF3",
        ["ScrollThumbBrush"] = "#B6C2D2",
        ["ScrollThumbHoverBrush"] = "#94A3B8",
        ["ScrollThumbPressedBrush"] = "#7C8BA0"
    };

    /// <summary>Палитра по умолчанию для тёмного режима (соответствует DarkTheme).</summary>
    private static Dictionary<string, string> DefaultDarkPalette() => new(StringComparer.Ordinal)
    {
        ["AccentColor"] = "#FFB300",
        ["AccentHoverColor"] = "#FFCA28",
        ["AccentPressedColor"] = "#FF8F00",
        ["SidebarColor"] = "#111827",
        ["SidebarHoverColor"] = "#1F2937",
        ["SidebarSelectedColor"] = "#FFB300",
        ["ContentBackgroundColor"] = "#0F172A",
        ["CardBackgroundColor"] = "#1E293B",
        ["BorderColor"] = "#334155",
        ["TextPrimaryColor"] = "#F1F5F9",
        ["TextSecondaryColor"] = "#CBD5E1",
        ["TextOnAccentColor"] = "#FFFFFF",
        ["ButtonTextColor"] = "#000000",
        ["FavoriteColor"] = "#FBBF24",
        ["FolderColor"] = "#3B82F6",
        ["FavoriteFolderColor"] = "#8B5CF6",
        ["ItemHoverColor"] = "#334155",
        ["ItemSelectedColor"] = "#1E3A5F",
        ["AvatarBackgroundColor"] = "#1E3A5F",
        ["AvatarTextColor"] = "#FFB300",
        ["SecondaryButtonBackgroundColor"] = "#FFF3CD",
        ["SecondaryButtonHoverColor"] = "#FFE69C",
        ["SecondaryButtonPressedColor"] = "#FFD54D",
        ["TreeHoverColor"] = "#334155",
        ["TreeSelectedColor"] = "#B45309",
        ["ScrollTrackBrush"] = "#16202E",
        ["ScrollThumbBrush"] = "#3B4A5F",
        ["ScrollThumbHoverBrush"] = "#52657F",
        ["ScrollThumbPressedBrush"] = "#6B80A0"
    };

    /// <summary>
    /// Строит цветовую схему (пару светлой и тёмной палитр) автоматически из одного
    /// базового цвета (issue #271). Базовый цвет становится акцентным; остальные ключи
    /// выводятся HSL-трансформациями (сдвиг светлости/насыщенности), сохраняя оттенок.
    /// Результат пригоден для ручной правки в редакторе перед сохранением.
    /// </summary>
    public static ColorScheme GenerateFromColor(string baseHex)
    {
        var (r, g, b) = ParseRgb(baseHex);
        RgbToHsl(r, g, b, out var h, out var s, out _);

        var scheme = new ColorScheme { Name = "Scheme.FromColor" };
        scheme.LightColors = BuildGeneratedPalette(h, s, dark: false);
        scheme.DarkColors = BuildGeneratedPalette(h, s, dark: true);
        return scheme;
    }

    private static Dictionary<string, string> BuildGeneratedPalette(double h, double s, bool dark)
    {
        string C(double lightness, double saturationFactor = 1.0)
            => HslToHex(h, Math.Clamp(s * saturationFactor, 0.08, 1.0), Math.Clamp(lightness, 0.0, 1.0));

        if (dark)
        {
            return new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["AccentColor"] = C(0.55, 0.9),
                ["AccentHoverColor"] = C(0.60, 0.9),
                ["AccentPressedColor"] = C(0.45, 0.9),
                ["SidebarColor"] = C(0.12, 0.35),
                ["SidebarHoverColor"] = C(0.18, 0.40),
                ["SidebarSelectedColor"] = C(0.55, 0.9),
                ["ContentBackgroundColor"] = C(0.12, 0.40),
                ["CardBackgroundColor"] = C(0.18, 0.40),
                ["BorderColor"] = C(0.28, 0.35),
                ["TextPrimaryColor"] = "#F1F5F9",
                ["TextSecondaryColor"] = C(0.80, 0.30),
                ["TextOnAccentColor"] = "#000000",
                ["ButtonTextColor"] = "#000000",
                ["FavoriteColor"] = C(0.60, 0.90),
                ["FolderColor"] = C(0.60, 0.80),
                ["FavoriteFolderColor"] = C(0.60, 0.60),
                ["ItemHoverColor"] = C(0.25, 0.40),
                ["ItemSelectedColor"] = C(0.30, 0.50),
                ["AvatarBackgroundColor"] = C(0.30, 0.50),
                ["AvatarTextColor"] = C(0.55, 0.90),
                ["SecondaryButtonBackgroundColor"] = C(0.90, 0.50),
                ["SecondaryButtonHoverColor"] = C(0.85, 0.60),
                ["SecondaryButtonPressedColor"] = C(0.80, 0.65),
                ["TreeHoverColor"] = C(0.25, 0.40),
                ["TreeSelectedColor"] = C(0.40, 0.50),
                ["ScrollTrackBrush"] = C(0.14, 0.40),
                ["ScrollThumbBrush"] = C(0.32, 0.35),
                ["ScrollThumbHoverBrush"] = C(0.40, 0.35),
                ["ScrollThumbPressedBrush"] = C(0.48, 0.35)
            };
        }

        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["AccentColor"] = C(0.50, 0.85),
            ["AccentHoverColor"] = C(0.44, 0.85),
            ["AccentPressedColor"] = C(0.38, 0.85),
            ["SidebarColor"] = C(0.16, 0.45),
            ["SidebarHoverColor"] = C(0.22, 0.45),
            ["SidebarSelectedColor"] = C(0.52, 0.90),
            ["ContentBackgroundColor"] = C(0.93, 0.35),
            ["CardBackgroundColor"] = C(0.99, 0.15),
            ["BorderColor"] = C(0.88, 0.30),
            ["TextPrimaryColor"] = "#000000",
            ["TextSecondaryColor"] = C(0.45, 0.40),
            ["TextOnAccentColor"] = "#FFFFFF",
            ["ButtonTextColor"] = "#000000",
            ["FavoriteColor"] = C(0.50, 0.90),
            ["FolderColor"] = C(0.50, 0.80),
            ["FavoriteFolderColor"] = C(0.55, 0.60),
            ["ItemHoverColor"] = C(0.94, 0.50),
            ["ItemSelectedColor"] = C(0.88, 0.60),
            ["AvatarBackgroundColor"] = C(0.94, 0.50),
            ["AvatarTextColor"] = C(0.35, 0.70),
            ["SecondaryButtonBackgroundColor"] = C(0.94, 0.50),
            ["SecondaryButtonHoverColor"] = C(0.88, 0.60),
            ["SecondaryButtonPressedColor"] = C(0.82, 0.65),
            ["TreeHoverColor"] = C(0.96, 0.40),
            ["TreeSelectedColor"] = C(0.84, 0.65),
            ["ScrollTrackBrush"] = C(0.91, 0.30),
            ["ScrollThumbBrush"] = C(0.75, 0.30),
            ["ScrollThumbHoverBrush"] = C(0.68, 0.30),
            ["ScrollThumbPressedBrush"] = C(0.60, 0.30)
        };
    }

    private static (byte R, byte G, byte B) ParseRgb(string hex)
    {
        hex = (hex ?? string.Empty).Trim().TrimStart('#');
        if (hex.Length == 6 && byte.TryParse(hex.Substring(0, 2), System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture, out var rr)
            && byte.TryParse(hex.Substring(2, 2), System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture, out var gg)
            && byte.TryParse(hex.Substring(4, 2), System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture, out var bb))
            return (rr, gg, bb);
        return (253, 191, 0); // запасной жёлтый акцент
    }

    private static void RgbToHsl(byte r, byte g, byte b, out double h, out double s, out double l)
    {
        double rd = r / 255.0, gd = g / 255.0, bd = b / 255.0;
        double max = Math.Max(rd, Math.Max(gd, bd)), min = Math.Min(rd, Math.Min(gd, bd));
        l = (max + min) / 2.0;
        if (Math.Abs(max - min) < 1e-9)
        {
            h = 0; s = 0;
            return;
        }
        double d = max - min;
        s = l > 0.5 ? d / (2.0 - max - min) : d / (max + min);
        if (Math.Abs(max - rd) < 1e-9) h = (gd - bd) / d + (gd < bd ? 6 : 0);
        else if (Math.Abs(max - gd) < 1e-9) h = (bd - rd) / d + 2;
        else h = (rd - gd) / d + 4;
        h /= 6.0;
    }

    private static string HslToHex(double h, double s, double l)
    {
        double q = l < 0.5 ? l * (1.0 + s) : l + s - l * s;
        double p = 2.0 * l - q;
        byte To(double t)
        {
            t = ((t % 1.0) + 1.0) % 1.0;
            double v;
            if (t < 1.0 / 6.0) v = p + (q - p) * 6.0 * t;
            else if (t < 0.5) v = q;
            else if (t < 2.0 / 3.0) v = p + (q - p) * (2.0 / 3.0 - t) * 6.0;
            else v = p;
            return (byte)Math.Round(v * 255.0);
        }
        byte r = To(h + 1.0 / 3.0), g = To(h), b = To(h - 1.0 / 3.0);
        return $"#{r:X2}{g:X2}{b:X2}";
    }

    // ---- Сериализация схемы в JSON ----

    /// <summary>Сериализует схему в JSON-строку (пишутся только <see cref="Name"/> и обе палитры).</summary>
    public string ToJson()
    {
        return JsonSerializer.Serialize(this, JsonOptions);
    }

    /// <summary>Десериализует схему из JSON-строки. При ошибке возвращает null.</summary>
    public static ColorScheme? FromJson(string json)
    {
        try
        {
            var scheme = JsonSerializer.Deserialize<ColorScheme>(json, JsonOptions);
            scheme?.Normalize();
            return scheme;
        }
        catch
        {
            return null;
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        // Кириллицу и прочие не-ASCII символы (например, имена схем) записываем читаемыми
        // UTF-8, а не \uXXXX-последовательностями (issue #170). Влияет только на запись.
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };
}