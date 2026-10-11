using System;
using System.Collections.Generic;
using System.Globalization;

namespace Configuration_Management.Services;

/// <summary>
/// Оставшееся время загрузки (issue #360): форматирование и оценка «сколько
/// осталось до окончания». Формат — «00:25»/«16:13» (mm:ss) до часа и
/// «2:15:18» (h:mm:ss) свыше часа.
/// </summary>
public static class DownloadEta
{
    /// <summary>Форматирует оставшееся время: mm:ss до часа, h:mm:ss свыше часа.</summary>
    public static string Format(TimeSpan eta)
    {
        var ts = eta < TimeSpan.Zero ? TimeSpan.Zero : eta;
        return ts.TotalHours >= 1
            ? string.Create(CultureInfo.InvariantCulture, $"{(int)ts.TotalHours}:{ts:mm\\:ss}")
            : ts.ToString(@"mm\:ss", CultureInfo.InvariantCulture);
    }

    /// <summary>Добавляет к тексту суффикс « · 16:13», если оценка есть и больше нуля;
    /// иначе текст возвращается без изменений.</summary>
    public static string AppendSuffix(string text, TimeSpan? eta)
    {
        if (eta is not { } value || value <= TimeSpan.Zero)
            return text;
        return $"{text} · {Format(value)}";
    }

    /// <summary>Максимальная оценка из набора (issue #360): время, когда завершатся
    /// ВСЕ загрузки; пустая строка — оценок ещё нет. Для индикатора главного окна.</summary>
    public static string FormatMax(IEnumerable<TimeSpan?> estimates)
    {
        TimeSpan? max = null;
        foreach (var estimate in estimates)
        {
            if (estimate is not { } value || value <= TimeSpan.Zero)
                continue;
            if (max is not { } current || value > current)
                max = value;
        }

        return max is { } result ? Format(result) : string.Empty;
    }
}

/// <summary>
/// Оценщик оставшегося времени одной загрузки (issue #360): сглаженная скорость
/// по отчётам прогресса (0..1) и оценка «сколько осталось». Часы инжектируются
/// через <paramref name="utcNow"/> — тесты ETA детерминированы.
/// </summary>
public sealed class DownloadEtaTracker
{
    /// <summary>Минимальная длительность загрузки (сек.), после которой оценка
    /// начинает показываться — в первые секунды ETA слишком «скачет».</summary>
    public const double MinElapsedSecondsForEta = 2.0;

    /// <summary>Вес нового замера скорости при сглаживании (0..1).</summary>
    private const double SmoothingAlpha = 0.3;

    private readonly Func<DateTime> _utcNow;
    private DateTime _startedAt;
    private DateTime _lastAt;
    private double _lastProgress;
    private double _ratePerSecond;
    private bool _hasSample;

    public DownloadEtaTracker(Func<DateTime>? utcNow = null)
        => _utcNow = utcNow ?? (static () => DateTime.UtcNow);

    /// <summary>Последняя оценка оставшегося времени или null (данных ещё мало).</summary>
    public TimeSpan? EstimatedRemaining { get; private set; }

    /// <summary>Принимает новый отчёт прогресса (0..1, клампится) и пересчитывает
    /// оценку; возвращает её же.</summary>
    public TimeSpan? Update(double progress)
    {
        progress = Math.Clamp(progress, 0, 1);
        var now = _utcNow();

        if (!_hasSample)
        {
            _hasSample = true;
            _startedAt = now;
            _lastAt = now;
            _lastProgress = progress;
            EstimatedRemaining = null;
            return null;
        }

        var sinceLastSeconds = (now - _lastAt).TotalSeconds;
        var delta = progress - _lastProgress;
        if (sinceLastSeconds > 0 && delta > 0)
        {
            var rate = delta / sinceLastSeconds;
            _ratePerSecond = _ratePerSecond <= 0
                ? rate
                : _ratePerSecond * (1 - SmoothingAlpha) + rate * SmoothingAlpha;
        }

        _lastAt = now;
        _lastProgress = progress;

        if (progress >= 1)
        {
            EstimatedRemaining = TimeSpan.Zero;
            return EstimatedRemaining;
        }

        var elapsedSeconds = (now - _startedAt).TotalSeconds;
        if (elapsedSeconds < MinElapsedSecondsForEta || _ratePerSecond <= 0)
        {
            EstimatedRemaining = null;
            return null;
        }

        var remainingSeconds = (1 - progress) / _ratePerSecond;
        if (double.IsNaN(remainingSeconds) || double.IsInfinity(remainingSeconds) || remainingSeconds < 0)
        {
            EstimatedRemaining = null;
            return null;
        }

        EstimatedRemaining = TimeSpan.FromSeconds(remainingSeconds);
        return EstimatedRemaining;
    }
}
