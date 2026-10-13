using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using Configuration_Management.Themes;

namespace Configuration_Management.Converters;

/// <summary>
/// Преобразует ключ иконки (имя Geometry в Icons.xaml) в Geometry для Path.Data.
/// Резолв учитывает активный набор иконок (Задача 5): для Outline ищет ключ с
/// суффиксом ".Outline" (IconsOutline.xaml), при отсутствии — бесшовный fallback
/// на Fluent-геометрию (Icons.xaml), затем на запасную «папку».
/// Результаты кэшируются по эффективному ключу (включая суффикс набора), поэтому
/// переключение набора не даёт устаревших значений — TryFindResource не вызывается
/// на каждую строку списка.
/// </summary>
public class IconKeyToGeometryConverter : IValueConverter
{
    private static readonly ConcurrentDictionary<string, Geometry> Cache = new(StringComparer.Ordinal);

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var key = value as string;
        if (string.IsNullOrWhiteSpace(key))
            key = "IconFolder";

        var set = ThemeFeatureMap.ParseIconSet(
            Application.Current?.Resources[ThemeFeatureMap.IconSetNameKey] as string);
        var effectiveKey = ThemeFeatureMap.IconKeyForSet(key, set);
        return Cache.GetOrAdd(effectiveKey, _ => Resolve(key, set));
    }

    private static Geometry Resolve(string key, IconSet set)
    {
        foreach (var candidate in ThemeFeatureMap.IconResolutionCandidates(key, set))
        {
            if (Application.Current?.TryFindResource(candidate) is Geometry geometry)
                return geometry;
        }

        return Geometry.Empty;
    }

    /// <summary>
    /// Резолвит геометрию иконки по ключу с учётом активного набора (Задача 5).
    /// Используется там, где иконка получается по ключу напрямую (без привязки),
    /// например в превью выбора иконки группы. Результат кэшируется по эффективному
    /// ключу, как и в <see cref="Convert"/>.
    /// </summary>
    public static Geometry ResolveGeometry(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            key = "IconFolder";

        var set = ThemeFeatureMap.ParseIconSet(
            Application.Current?.Resources[ThemeFeatureMap.IconSetNameKey] as string);
        var effectiveKey = ThemeFeatureMap.IconKeyForSet(key, set);
        return Cache.GetOrAdd(effectiveKey, _ => Resolve(key, set));
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
