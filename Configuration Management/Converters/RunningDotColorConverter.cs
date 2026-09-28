using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using Configuration_Management.Models;

namespace Configuration_Management.Converters;

/// <summary>
/// Цвет точки индикатора «база сейчас запущена» (issue #310): зелёный #22C55E
/// (процесс отвечает), оранжевый #F59E0B (завис — не отвечает при первом опросе),
/// красный #EF4444 (не отвечает несколько опросов подряд). Для скрытой точки
/// возвращается зелёный — она всё равно не видна (Visibility по IsRunning).
/// </summary>
public class RunningDotColorConverter : IValueConverter
{
    private static readonly Color Responding = Color.FromRgb(0x22, 0xC5, 0x5E);
    private static readonly Color Hung = Color.FromRgb(0xF5, 0x9E, 0x0B);
    private static readonly Color Critical = Color.FromRgb(0xEF, 0x44, 0x44);

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value switch
        {
            RunningDotStatus.Hung => Hung,
            RunningDotStatus.Critical => Critical,
            _ => Responding
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}