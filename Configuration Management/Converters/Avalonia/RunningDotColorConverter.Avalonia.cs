#if LINUX
using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Configuration_Management.Models;

namespace Configuration_Management.Converters
{
    /// <summary>
    /// Avalonia-версия: кисть точки индикатора «база сейчас запущена» (issue #310):
    /// зелёная #22C55E (отвечает), оранжевая #F59E0B (завис — первый опрос без отклика),
    /// красная #EF4444 (не отвечает несколько опросов подряд).
    /// </summary>
    public class RunningDotColorConverter : IValueConverter
    {
        private static readonly SolidColorBrush Responding = Create("#22C55E");
        private static readonly SolidColorBrush Hung = Create("#F59E0B");
        private static readonly SolidColorBrush Critical = Create("#EF4444");

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return value switch
            {
                RunningDotStatus.Hung => Hung,
                RunningDotStatus.Critical => Critical,
                _ => Responding
            };
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotSupportedException();

        private static SolidColorBrush Create(string hex) => new(Color.Parse(hex));
    }
}
#endif