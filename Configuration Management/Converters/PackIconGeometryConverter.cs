using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using Configuration_Management.Themes;

namespace Configuration_Management.Converters;

/// <summary>
/// Геометрия значка materialDesign:PackIcon по активному набору иконок (0.3.12.60).
/// Используется неявным стилем PackIcon в App.xaml: шаблон рисует значок своим Path,
/// а Data берёт отсюда через MultiBinding (Kind, IconSetState.Current, родной Data).
/// Для наборов, отличных от Fluent, ищется ресурс Pack.&lt;Kind&gt;.&lt;Набор&gt;
/// (словари Icons&lt;Набор&gt;.xaml, генератор tools/icongen); если варианта нет —
/// рисуется родная геометрия PackIcon. Экземпляр <see cref="Back"/> отдаёт фоновый
/// полупрозрачный слой (Duotone-набор Glass) или null, если слоя нет.
/// </summary>
public sealed class PackIconGeometryConverter : IMultiValueConverter
{
    /// <summary>Основной слой значка.</summary>
    public static PackIconGeometryConverter Front { get; } = new(false);

    /// <summary>Фоновый (полупрозрачный) слой значка.</summary>
    public static PackIconGeometryConverter Back { get; } = new(true);

    private static readonly ConcurrentDictionary<string, Geometry?> SetCache = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, Geometry?> NativeCache = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<Type, Dictionary<long, string[]>> NamesByValue = new();

    private readonly bool _back;

    private PackIconGeometryConverter(bool back) => _back = back;

    public object? Convert(object?[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        var kind = values.Length > 0 ? values[0] as Enum : null;
        var set = values.Length > 1 && values[1] is IconSet s ? s : IconSet.Fluent;

        if (kind is not null && set != IconSet.Fluent)
        {
            foreach (var name in NamesOf(kind))
            {
                var key = ThemeFeatureMap.PackIconKey(name, set, _back);
                if (key is null)
                    continue;
                var geometry = SetCache.GetOrAdd(key,
                    k => Application.Current?.TryFindResource(k) as Geometry);
                if (geometry is not null)
                    return geometry;
            }
            if (_back)
                return null;
        }

        if (_back)
            return null;

        // Родная геометрия PackIcon (строка пути MDI) — как в исходном шаблоне.
        var native = values.Length > 2 ? values[2] : null;
        return native switch
        {
            Geometry g => g,
            string data when !string.IsNullOrWhiteSpace(data) => NativeCache.GetOrAdd(data, ParseOrNull),
            _ => null
        };
    }

    public object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();

    /// <summary>
    /// Имена значения перечисления PackIconKind: у MaterialDesignThemes есть синонимы
    /// с одинаковым числовым значением, и ToString() может вернуть не тот, что указан в
    /// разметке. Поэтому пробуются все имена значения — словари наборов содержат ключи
    /// по именам, использованным в проекте.
    /// </summary>
    private static string[] NamesOf(Enum kind)
    {
        var type = kind.GetType();
        var preferred = kind.ToString();
        long value;
        try
        {
            value = System.Convert.ToInt64(kind, CultureInfo.InvariantCulture);
        }
        catch
        {
            return new[] { preferred };
        }
        var byValue = NamesByValue.GetOrAdd(type, BuildNameMap);
        if (!byValue.TryGetValue(value, out var names) || names.Length <= 1)
            return new[] { preferred };
        // Имя из ToString() — первым, затем синонимы.
        return names.OrderBy(n => n == preferred ? 0 : 1).ToArray();
    }

    /// <summary>Карта «значение → все имена» для перечисления (строится один раз на тип).</summary>
    private static Dictionary<long, string[]> BuildNameMap(Type type)
    {
        var names = Enum.GetNames(type);
        var values = Enum.GetValues(type);
        var map = new Dictionary<long, List<string>>();
        for (var i = 0; i < names.Length && i < values.Length; i++)
        {
            var v = System.Convert.ToInt64(values.GetValue(i), CultureInfo.InvariantCulture);
            if (!map.TryGetValue(v, out var list))
                map[v] = list = new List<string>();
            list.Add(names[i]);
        }
        return map.ToDictionary(p => p.Key, p => p.Value.ToArray());
    }

    private static Geometry? ParseOrNull(string data)
    {
        try
        {
            var g = Geometry.Parse(data);
            if (g.CanFreeze)
                g.Freeze();
            return g;
        }
        catch
        {
            return null;
        }
    }
}
