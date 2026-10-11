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
}
