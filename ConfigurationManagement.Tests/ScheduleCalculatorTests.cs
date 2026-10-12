using Configuration_Management.Models;
using Configuration_Management.Services;
using Xunit;

namespace ConfigurationManagement.Tests;

public sealed class ScheduleCalculatorTests
{
    private static ScheduledTask NewTask(string time = "02:00", params DayOfWeek[] days) => new()
    {
        Enabled = true,
        Time = time,
        DaysOfWeek = days.ToList()
    };

    [Fact]
    public void DailyTask_BeforeTime_NextRunIsToday()
    {
        var task = NewTask("14:30");
        var from = new DateTime(2026, 9, 24, 10, 0, 0); // до времени запуска

        var next = ScheduleCalculator.ComputeNextRun(task, from);

        Assert.Equal(new DateTime(2026, 9, 24, 14, 30, 0), next);
    }

    [Fact]
    public void DailyTask_AfterTime_NextRunIsTomorrow()
    {
        var task = NewTask("14:30");
        var from = new DateTime(2026, 9, 24, 16, 0, 0); // после времени запуска

        var next = ScheduleCalculator.ComputeNextRun(task, from);

        Assert.Equal(new DateTime(2026, 9, 25, 14, 30, 0), next);
    }

    [Fact]
    public void DailyTask_ExactlyAtTime_NextRunIsNow()
    {
        var task = NewTask("14:30");
        var from = new DateTime(2026, 9, 24, 14, 30, 0);

        var next = ScheduleCalculator.ComputeNextRun(task, from);

        Assert.Equal(from, next);
    }

    [Fact]
    public void WeekdayTask_SkipsToNextAllowedDay()
    {
        // 2026-09-24 — четверг (Thursday). Задание только на понедельник.
        var task = NewTask("09:00", DayOfWeek.Monday);
        var from = new DateTime(2026, 9, 24, 12, 0, 0);

        var next = ScheduleCalculator.ComputeNextRun(task, from);

        // Ближайший понедельник — 2026-09-28.
        Assert.Equal(new DateTime(2026, 9, 28, 9, 0, 0), next);
    }

    [Fact]
    public void WeekdayTask_SameDayBeforeTime_ReturnsToday()
    {
        // 2026-09-24 — четверг. Задание на четверг и субботу.
        var task = NewTask("09:00", DayOfWeek.Thursday, DayOfWeek.Saturday);
        var from = new DateTime(2026, 9, 24, 7, 0, 0);

        var next = ScheduleCalculator.ComputeNextRun(task, from);

        Assert.Equal(new DateTime(2026, 9, 24, 9, 0, 0), next);
    }

    [Fact]
    public void DisabledTask_ReturnsNull()
    {
        var task = NewTask("09:00");
        task.Enabled = false;

        Assert.Null(ScheduleCalculator.ComputeNextRun(task, new DateTime(2026, 9, 24, 8, 0, 0)));
    }

    [Fact]
    public void InvalidTime_TreatedAsMidnight()
    {
        var task = NewTask("not-a-time");
        var from = new DateTime(2026, 9, 24, 10, 0, 0);

        var next = ScheduleCalculator.ComputeNextRun(task, from);

        Assert.Equal(new DateTime(2026, 9, 25, 0, 0, 0), next);
    }

    // ======================= Повтор с интервалом (issue #366/#367) =======================

    [Fact]
    public void RepeatEnabled_Hourly_ReturnsNextStepAfterFrom()
    {
        // Задание с повтором «каждый час», старт серии в 02:00.
        var task = NewTask("02:00");
        task.RepeatEnabled = true;
        task.RepeatIntervalValue = 1;
        task.RepeatIntervalUnit = ScheduledTaskRepeatUnit.Hours;
        var from = new DateTime(2026, 9, 24, 3, 15, 0); // между шагами 03:00 и 04:00

        var next = ScheduleCalculator.ComputeNextRun(task, from);

        Assert.Equal(new DateTime(2026, 9, 24, 4, 0, 0), next);
    }

    [Fact]
    public void RepeatEnabled_Hourly_BeforeFirstStep_ReturnsFirstStep()
    {
        var task = NewTask("02:00");
        task.RepeatEnabled = true;
        task.RepeatIntervalValue = 1;
        task.RepeatIntervalUnit = ScheduledTaskRepeatUnit.Hours;
        var from = new DateTime(2026, 9, 24, 1, 30, 0);

        var next = ScheduleCalculator.ComputeNextRun(task, from);

        Assert.Equal(new DateTime(2026, 9, 24, 2, 0, 0), next);
    }

    [Fact]
    public void RepeatEnabled_Minutes_MultipleStepsPerDay()
    {
        // «каждые 10 минут» — серия в рамках дня: 02:00, 02:10, …
        var task = NewTask("02:00");
        task.RepeatEnabled = true;
        task.RepeatIntervalValue = 10;
        task.RepeatIntervalUnit = ScheduledTaskRepeatUnit.Minutes;
        var from = new DateTime(2026, 9, 24, 2, 23, 0);

        var next = ScheduleCalculator.ComputeNextRun(task, from);

        Assert.Equal(new DateTime(2026, 9, 24, 2, 30, 0), next);
    }

    [Fact]
    public void RepeatEnabled_SeriesExhaustedToday_MovesToNextAllowedDay()
    {
        // Серия 02:00, 03:00, … 23:00 (шаг час) исчерпана к 23:59 — следующий шаг на следующий день.
        var task = NewTask("02:00");
        task.RepeatEnabled = true;
        task.RepeatIntervalValue = 1;
        task.RepeatIntervalUnit = ScheduledTaskRepeatUnit.Hours;
        var from = new DateTime(2026, 9, 24, 23, 59, 0);

        var next = ScheduleCalculator.ComputeNextRun(task, from);

        Assert.Equal(new DateTime(2026, 9, 25, 2, 0, 0), next);
    }

    [Fact]
    public void RepeatDisabled_IgnoresIntervalValue()
    {
        // Повтор выключен — интервал не влияет: следующий запуск завтра в то же время.
        var task = NewTask("14:30");
        task.RepeatEnabled = false;
        task.RepeatIntervalValue = 1;
        task.RepeatIntervalUnit = ScheduledTaskRepeatUnit.Hours;
        var from = new DateTime(2026, 9, 24, 16, 0, 0);

        var next = ScheduleCalculator.ComputeNextRun(task, from);

        Assert.Equal(new DateTime(2026, 9, 25, 14, 30, 0), next);
    }

    [Fact]
    public void GetRepeatInterval_ConvertsUnits()
    {
        Assert.Equal(TimeSpan.FromMinutes(60), ScheduledTask.GetRepeatInterval(1, ScheduledTaskRepeatUnit.Hours));
        Assert.Equal(TimeSpan.FromMinutes(10), ScheduledTask.GetRepeatInterval(10, ScheduledTaskRepeatUnit.Minutes));
        Assert.Equal(TimeSpan.FromSeconds(30), ScheduledTask.GetRepeatInterval(30, ScheduledTaskRepeatUnit.Seconds));
        Assert.Equal(TimeSpan.FromDays(2), ScheduledTask.GetRepeatInterval(2, ScheduledTaskRepeatUnit.Days));
        Assert.Equal(TimeSpan.Zero, ScheduledTask.GetRepeatInterval(0, ScheduledTaskRepeatUnit.Minutes));
        Assert.Equal(TimeSpan.Zero, ScheduledTask.GetRepeatInterval(-5, ScheduledTaskRepeatUnit.Minutes));
    }
}