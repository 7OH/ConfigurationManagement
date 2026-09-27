using Configuration_Management.Models;
using Configuration_Management.Services;
using Xunit;

namespace ConfigurationManagement.Tests;

/// <summary>
/// Юнит-тесты «догоняющего выполнения» пропущенных заданий по расписанию (функция №7):
/// чистая логика <see cref="ScheduleCatchUpCalculator"/> и оркестрация
/// <see cref="SchedulerService.RunCatchUpAsync"/>. По образцу ScheduleCalculatorTests —
/// без UI и файловой системы.
/// </summary>
public sealed class ScheduleCatchUpTests
{
    private static ScheduledTask NewTask(string time = "02:00", params DayOfWeek[] days) => new()
    {
        Enabled = true,
        Time = time,
        DaysOfWeek = days.ToList(),
        Kind = ScheduledTaskKind.Backup
    };

    /// <summary>Превращает локальный момент в UTC-метку (инвариантно к часовому поясу машины).</summary>
    private static DateTime UtcOf(DateTime local) => local.ToUniversalTime();

    // ---- Определение пропуска: базовые случаи ----

    [Fact]
    public void NeverRun_DailyTask_TimeAlreadyPassed_Missed()
    {
        var task = NewTask("02:00");
        var now = new DateTime(2026, 9, 24, 10, 0, 0); // чт, плановое время сегодня уже наступило

        Assert.True(ScheduleCatchUpCalculator.IsMissed(task, now));
    }

    [Fact]
    public void RanYesterday_BeforeTodaysPlannedTime_Missed()
    {
        var task = NewTask("02:00");
        task.LastRunUtc = UtcOf(new DateTime(2026, 9, 23, 1, 0, 0)); // вчера до планового времени
        var now = new DateTime(2026, 9, 24, 10, 0, 0);

        Assert.True(ScheduleCatchUpCalculator.IsMissed(task, now));
    }

    [Fact]
    public void RanToday_AfterPlannedTime_NotMissed()
    {
        var task = NewTask("02:00");
        task.LastRunUtc = UtcOf(new DateTime(2026, 9, 24, 9, 0, 0)); // уже выполнялось сегодня после планового
        var now = new DateTime(2026, 9, 24, 10, 0, 0);

        Assert.False(ScheduleCatchUpCalculator.IsMissed(task, now));
    }

    [Fact]
    public void PlannedTimeInFuture_NotMissed()
    {
        // Задание выполнялось сегодня утром, следующее плановое время — сегодня позже:
        // наступивших плановых моментов позже последнего запуска нет.
        var task = NewTask("14:00");
        task.LastRunUtc = UtcOf(new DateTime(2026, 9, 24, 9, 0, 0));
        var now = new DateTime(2026, 9, 24, 10, 0, 0);

        Assert.False(ScheduleCatchUpCalculator.IsMissed(task, now));
    }

    [Fact]
    public void NullLastRunUtc_UsesLegacyLastRunAt()
    {
        // Обратная совместимость: у старых сохранённых заданий нет LastRunUtc — берём LastRunAt.
        var task = NewTask("02:00");
        task.LastRunUtc = null;
        task.LastRunAt = new DateTime(2026, 9, 23, 1, 0, 0); // вчера до планового времени
        var now = new DateTime(2026, 9, 24, 10, 0, 0);

        Assert.True(ScheduleCatchUpCalculator.IsMissed(task, now));
    }

    [Fact]
    public void LastRunUtc_PreferredOverLegacyLastRunAt()
    {
        // Оба поля заполнены, но LastRunUtc актуальнее устаревшего LastRunAt.
        var task = NewTask("02:00");
        task.LastRunUtc = UtcOf(new DateTime(2026, 9, 24, 9, 0, 0)); // выполнялось сегодня после планового
        task.LastRunAt = new DateTime(2026, 9, 23, 1, 0, 0);
        var now = new DateTime(2026, 9, 24, 10, 0, 0);

        Assert.False(ScheduleCatchUpCalculator.IsMissed(task, now));
    }

    // ---- Дни недели ----

    [Fact]
    public void WrongDayOfWeek_NotMissed()
    {
        // Задание только на субботу и выполнялось в прошлую субботу после планового времени:
        // среди последних 7 дней плановых моментов позже последнего запуска нет.
        var task = NewTask("02:00", DayOfWeek.Saturday);
        task.LastRunUtc = UtcOf(new DateTime(2026, 9, 19, 3, 0, 0));
        var now = new DateTime(2026, 9, 24, 10, 0, 0); // чт

        Assert.False(ScheduleCatchUpCalculator.IsMissed(task, now));
    }

    [Fact]
    public void WeekdayTask_MissedDayWithinFreshness_Missed()
    {
        // Задание только на среду, никогда не выполнялось, сейчас пятница:
        // пропущенный плановый момент (среда) в пределах окна давности.
        var task = NewTask("02:00", DayOfWeek.Wednesday);
        var now = new DateTime(2026, 9, 25, 10, 0, 0); // пт

        Assert.True(ScheduleCatchUpCalculator.IsMissed(task, now));
    }

    // ---- Исключение по типу ----

    [Fact]
    public void UpdateAppKind_NotMissed()
    {
        // Обновление приложения не догоняется: найденное обновление установится
        // при следующей автоматической проверке.
        var task = NewTask("02:00");
        task.Kind = ScheduledTaskKind.UpdateApp;
        var now = new DateTime(2026, 9, 24, 10, 0, 0);

        Assert.False(ScheduleCatchUpCalculator.IsMissed(task, now));
    }

    // ---- Порог давности ----

    [Fact]
    public void DefaultMaxAge_IsSevenDays()
    {
        Assert.Equal(TimeSpan.FromDays(7), ScheduleCatchUpCalculator.DefaultMaxAge);
    }

    [Fact]
    public void MissOlderThanMaxAge_NotMissed()
    {
        // Плановый день (среда) был 2 дня назад, а порог — 1 сутки.
        var task = NewTask("02:00", DayOfWeek.Wednesday);
        var now = new DateTime(2026, 9, 25, 10, 0, 0); // пт

        Assert.False(ScheduleCatchUpCalculator.IsMissed(task, now, TimeSpan.FromDays(1)));
    }

    [Fact]
    public void MissWithinMaxAge_Missed()
    {
        var task = NewTask("02:00", DayOfWeek.Wednesday);
        var now = new DateTime(2026, 9, 25, 10, 0, 0); // пт

        Assert.True(ScheduleCatchUpCalculator.IsMissed(task, now, TimeSpan.FromDays(3)));
    }

    [Fact]
    public void VeryOldLastRun_OnlyFreshMissCaughtUp()
    {
        // Последний запуск был больше месяца назад, но вчерашний плановый момент свежий —
        // задание догоняется один раз (а не «за все пропущенные дни»).
        var task = NewTask("02:00");
        task.LastRunUtc = UtcOf(new DateTime(2026, 8, 1, 3, 0, 0));
        var now = new DateTime(2026, 9, 24, 10, 0, 0);

        Assert.True(ScheduleCatchUpCalculator.IsMissed(task, now));
    }

    // ---- Выключенное задание ----

    [Fact]
    public void DisabledTask_NotMissed()
    {
        var task = NewTask("02:00");
        task.Enabled = false;
        var now = new DateTime(2026, 9, 24, 10, 0, 0);

        Assert.False(ScheduleCatchUpCalculator.IsMissed(task, now));
    }

    [Fact]
    public void NullTask_NotMissed()
    {
        Assert.False(ScheduleCatchUpCalculator.IsMissed(null!, new DateTime(2026, 9, 24, 10, 0, 0)));
    }

    // ---- Оркестрация SchedulerService.RunCatchUpAsync ----

    [Fact]
    public async Task CatchUpOptionDisabled_DoesNothing()
    {
        var store = new FakeTaskStore();
        store.Tasks.Add(NewTask("00:00"));
        using var scheduler = CreateScheduler(store);

        var executed = await scheduler.RunCatchUpAsync(catchUpEnabled: false);

        Assert.Equal(0, executed);
        Assert.Equal(0, store.SaveCount);
        Assert.Null(store.Tasks[0].LastRunAt);
        Assert.Null(store.Tasks[0].LastRunUtc);
    }

    [Fact]
    public async Task CatchUpOptionEnabled_ExecutesMissedTaskOnce()
    {
        var store = new FakeTaskStore();
        var task = NewTask("00:00");
        store.Tasks.Add(task);
        using var scheduler = CreateScheduler(store);

        var executed = await scheduler.RunCatchUpAsync(catchUpEnabled: true);

        Assert.Equal(1, executed);
        Assert.Equal(1, store.SaveCount);
        Assert.NotNull(task.LastRunAt);
        Assert.NotNull(task.LastRunUtc);
        Assert.False(task.LastRunSuccess); // база не задана — результат «ошибка», но выполнение зафиксировано
    }

    [Fact]
    public async Task CatchUpOptionEnabled_RunsOnlyMissedTasks()
    {
        var store = new FakeTaskStore();
        // Пропущенное задание (никогда не выполнялось).
        var missed = NewTask("00:00");
        // Задание, выполнявшееся сегодня после планового времени (не пропущено).
        var done = NewTask("00:00");
        done.LastRunUtc = DateTime.UtcNow;
        store.Tasks.Add(missed);
        store.Tasks.Add(done);
        using var scheduler = CreateScheduler(store);

        var executed = await scheduler.RunCatchUpAsync(catchUpEnabled: true);

        Assert.Equal(1, executed);
        Assert.NotNull(missed.LastRunAt);
        Assert.Null(done.LastRunAt); // не выполнялось повторно
    }

    private static SchedulerService CreateScheduler(FakeTaskStore store) => new(
        store,
        null!,
        null!,
        null!,
        null!,
        null!,
        new GitHubReleaseService(),
        null!,
        new FakeNotifications());

    /// <summary>Фейковое хранилище заданий в памяти (без файловой системы).</summary>
    private sealed class FakeTaskStore : IScheduledTaskStore
    {
        public List<ScheduledTask> Tasks { get; } = new();
        public int SaveCount { get; private set; }

        public string TasksDirectory => "";
        public IReadOnlyList<ScheduledTask> LoadAll() => Tasks;
        public void Save(ScheduledTask task) => SaveCount++;
        public void Delete(string id) { }
        public ScheduledTask? Get(string id) => Tasks.FirstOrDefault(t => t.Id == id);
    }

    /// <summary>Фейк системных уведомлений — только считает вызовы.</summary>
    private sealed class FakeNotifications : INotificationService
    {
        public void Show(string title, string message) { }
    }
}