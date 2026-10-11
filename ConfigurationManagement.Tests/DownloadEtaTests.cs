using System;
using System.Collections.Generic;
using Configuration_Management.Services;
using Xunit;

namespace ConfigurationManagement.Tests;

/// <summary>
/// Тесты оценки оставшегося времени загрузки (issue #360): форматирование
/// «00:25»/«16:13»/«2:15:18», сглаженная скорость, порог показа, суффикс текста.
/// </summary>
public sealed class DownloadEtaTests
{
    /// <summary>Управляемые часы для детерминированных тестов.</summary>
    private sealed class FakeClock
    {
        public DateTime UtcNow { get; set; } = new(2026, 10, 11, 12, 0, 0, DateTimeKind.Utc);

        public void AdvanceSeconds(double seconds) => UtcNow = UtcNow.AddSeconds(seconds);
    }

    [Theory]
    [InlineData(25, "00:25")]
    [InlineData(16 * 60 + 13, "16:13")]
    [InlineData(59 * 60 + 59, "59:59")]
    public void Format_UnderHour_IsMinuteSeconds(int seconds, string expected)
    {
        Assert.Equal(expected, DownloadEta.Format(TimeSpan.FromSeconds(seconds)));
    }

    [Theory]
    [InlineData(3600 + 15 * 60 + 18, "1:15:18")]
    [InlineData(2 * 3600 + 15 * 60 + 18, "2:15:18")]
    [InlineData(10 * 3600, "10:00:00")]
    public void Format_OverHour_IsHoursMinuteSeconds(int seconds, string expected)
    {
        Assert.Equal(expected, DownloadEta.Format(TimeSpan.FromSeconds(seconds)));
    }

    [Fact]
    public void Format_Negative_ClampedToZero()
    {
        Assert.Equal("00:00", DownloadEta.Format(TimeSpan.FromSeconds(-5)));
    }

    [Fact]
    public void AppendSuffix_WithEstimate_AppendsTime()
    {
        Assert.Equal("45 % · 16:13", DownloadEta.AppendSuffix("45 %", new TimeSpan(0, 16, 13)));
    }

    [Fact]
    public void AppendSuffix_WithoutEstimate_ReturnsTextUnchanged()
    {
        Assert.Equal("45 %", DownloadEta.AppendSuffix("45 %", null));
        Assert.Equal("45 %", DownloadEta.AppendSuffix("45 %", TimeSpan.Zero));
    }

    [Fact]
    public void FormatMax_TakesLongestEstimate_IgnoresEmpty()
    {
        var text = DownloadEta.FormatMax(new TimeSpan?[]
        {
            null,
            TimeSpan.FromSeconds(30),
            new TimeSpan(0, 16, 13),
            TimeSpan.Zero,
        });

        Assert.Equal("16:13", text);
    }

    [Fact]
    public void FormatMax_NoEstimates_IsEmpty()
    {
        Assert.Equal(string.Empty, DownloadEta.FormatMax(new TimeSpan?[] { null, null, TimeSpan.Zero }));
    }

    [Fact]
    public void Tracker_FirstSample_HasNoEstimate()
    {
        var clock = new FakeClock();
        var tracker = new DownloadEtaTracker(() => clock.UtcNow);

        var estimate = tracker.Update(0);

        Assert.Null(estimate);
        Assert.Null(tracker.EstimatedRemaining);
    }

    [Fact]
    public void Tracker_BeforeMinElapsed_HasNoEstimate()
    {
        var clock = new FakeClock();
        var tracker = new DownloadEtaTracker(() => clock.UtcNow);

        tracker.Update(0);
        clock.AdvanceSeconds(1);
        var estimate = tracker.Update(0.1);

        Assert.Null(estimate);
    }

    [Fact]
    public void Tracker_WithSteadyRate_EstimatesRemainingTime()
    {
        var clock = new FakeClock();
        var tracker = new DownloadEtaTracker(() => clock.UtcNow);

        tracker.Update(0);                 // t=0
        clock.AdvanceSeconds(10);
        var estimate = tracker.Update(0.5); // 50% за 10 с → остаток ~10 с

        Assert.NotNull(estimate);
        Assert.Equal(10, estimate!.Value.TotalSeconds, precision: 0);
    }

    [Fact]
    public void Tracker_AtFullProgress_IsZero()
    {
        var clock = new FakeClock();
        var tracker = new DownloadEtaTracker(() => clock.UtcNow);

        tracker.Update(0);
        clock.AdvanceSeconds(5);
        var estimate = tracker.Update(1);

        Assert.Equal(TimeSpan.Zero, estimate);
        Assert.Equal(TimeSpan.Zero, tracker.EstimatedRemaining);
    }

    [Fact]
    public void Tracker_NoProgressMovement_HasNoEstimate()
    {
        var clock = new FakeClock();
        var tracker = new DownloadEtaTracker(() => clock.UtcNow);

        tracker.Update(0);
        clock.AdvanceSeconds(10);
        var estimate = tracker.Update(0); // прогресс не растёт — скорости нет

        Assert.Null(estimate);
    }

    // ======================= Стабильность оценки (0.3.12.13, issue #360) =======================

    [Fact]
    public void Tracker_ManySmallReports_StaysCloseToIdealEstimate()
    {
        // Множество мелких отчётов («куски» одного файла / частые буферы) при
        // ровном темпе 5 %/сек: каждая опубликованная оценка ≈ идеальной (1-p)/rate —
        // сглаживание не даёт оценке «прыгать».
        var clock = new FakeClock();
        var tracker = new DownloadEtaTracker(() => clock.UtcNow);
        const double trueRate = 0.05;

        tracker.Update(0);
        var log = new List<string>();
        TimeSpan? previous = null;
        for (var i = 1; i <= 160; i++)
        {
            clock.AdvanceSeconds(0.1);
            var progress = i * 0.005; // ровно 5 %/сек
            var estimate = tracker.Update(progress);
            // Сравниваем только СВЕЖЕ опубликованные оценки (между публикациями
            // значение намеренно не меняется — обновление раз в секунду).
            if (!Equals(estimate, previous))
            {
                previous = estimate;
                if (estimate is { } value)
                {
                    var ideal = (1 - progress) / trueRate;
                    if (value.TotalSeconds < ideal * 0.9 || value.TotalSeconds > ideal * 1.1)
                        log.Add($"i={i} p={progress:G17} eta={value.TotalSeconds:G17} ideal={ideal:G17}");
                }
            }
        }

        Assert.True(log.Count == 0, string.Join("\n", log));
    }

    [Fact]
    public void Tracker_EtaPublishedAtMostOncePerSecond()
    {
        // Отчёты каждые 0.1 секунды — опубликованная оценка меняется не чаще
        // раза в секунду (плюс первый переход от null после порога 2 сек).
        var clock = new FakeClock();
        var tracker = new DownloadEtaTracker(() => clock.UtcNow);

        tracker.Update(0);
        var changes = new List<DateTime>();
        TimeSpan? last = null;
        for (var i = 1; i <= 60; i++)
        {
            clock.AdvanceSeconds(0.1);
            var estimate = tracker.Update(i * 0.01);
            if (!Equals(estimate, last))
            {
                changes.Add(clock.UtcNow);
                last = estimate;
            }
        }

        Assert.True(changes.Count >= 3, $"Оценка публиковалась слишком редко: {changes.Count}");
        for (var i = 1; i < changes.Count; i++)
        {
            var gap = (changes[i] - changes[i - 1]).TotalSeconds;
            Assert.True(gap >= 0.99, $"Оценка обновилась через {gap:F2} с (< 1 с)");
        }
    }

    [Fact]
    public void Tracker_BurstyChunkReports_EstimateStaysWithinReasonableBand()
    {
        // Всплески отчётов: раз в 2 секунды приходит серия из 20 «кусочков» с почти
        // одинаковыми временными метками (очередь UI-потока), общий темп 5 %/сек.
        // Раньше такой поток давал скачки «то 00:07, то 03:25»; теперь оценка
        // агрегируется за окно ≥ 1 с и остаётся в разумной полосе вокруг идеальной.
        var clock = new FakeClock();
        var tracker = new DownloadEtaTracker(() => clock.UtcNow);
        const double trueRate = 0.05;

        tracker.Update(0);
        var progress = 0.0;
        TimeSpan? previous = null;
        for (var burst = 0; burst < 9; burst++)
        {
            clock.AdvanceSeconds(2.0);
            for (var i = 0; i < 20; i++)
            {
                clock.AdvanceSeconds(0.0005); // метки внутри всплеска почти совпадают
                progress += 0.005;            // 20 × 0,5 % = 10 % за 2 секунды
                var estimate = tracker.Update(progress);
                // Проверяются только СВЕЖЕ опубликованные оценки: между публикациями
                // значение не меняется (обновление раз в секунду).
                if (Equals(estimate, previous))
                    continue;

                previous = estimate;
                if (estimate is not { } value)
                    continue;

                var ideal = (1 - progress) / trueRate;
                // Оценка никогда не «схлопывается» до нуля — регрессия «то 00:07».
                Assert.True(value.TotalSeconds >= ideal * 0.5,
                    $"Оценка схлопнулась: {value.TotalSeconds:F1} с при идеале {ideal:F1} с (burst {burst})");
                // После пары окон сглаживания — вблизи идеальной оценки.
                if (burst >= 2)
                    Assert.InRange(value.TotalSeconds, ideal * 0.5, ideal * 2.0);
            }
        }
    }

    [Fact]
    public void Tracker_SameInstantReports_DoNotChangeEstimate()
    {
        // Серия отчётов с одинаковой временной меткой (всплеск разом) не должна
        // ни изменить оценку, ни «взорвать» скорость мгновенным приростом.
        var clock = new FakeClock();
        var tracker = new DownloadEtaTracker(() => clock.UtcNow);

        tracker.Update(0);
        for (var i = 1; i <= 5; i++)
        {
            clock.AdvanceSeconds(1);
            tracker.Update(i * 0.05);
        }

        var before = tracker.EstimatedRemaining;
        Assert.NotNull(before);
        for (var i = 1; i <= 10; i++)
        {
            var estimate = tracker.Update(0.25 + i * 0.01);
            Assert.Equal(before, estimate);
        }
    }

    [Fact]
    public void Tracker_EmaSmoothing_DampsSuddenRateChange()
    {
        // Резкая смена темпа сглаживается EMA: оценка после одного «медленного»
        // окна не равна грубому (1-p)/rate нового темпа, а сдвинута в сторону
        // предыдущей скорости (прыжков «то 00:07, то 03:25» нет).
        var clock = new FakeClock();
        var tracker = new DownloadEtaTracker(() => clock.UtcNow);

        tracker.Update(0);
        for (var i = 1; i <= 5; i++)
        {
            clock.AdvanceSeconds(1);
            tracker.Update(i * 0.05); // 5 %/сек → ETA(0.25) ≈ 15 с
        }

        clock.AdvanceSeconds(1);
        var afterSpike = tracker.Update(0.26); // резкое замедление: 1 % за секунду
        Assert.NotNull(afterSpike);

        var naive = (1 - 0.26) / 0.01; // «сырая» оценка по одному окну: 74 с
        Assert.True(afterSpike!.Value.TotalSeconds < naive * 0.75,
            $"EMA не сгладила всплеск: {afterSpike.Value.TotalSeconds:F1} с ≈ сырой {naive:F1} с");
    }
}
