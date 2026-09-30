using System.Xml.Linq;
using Configuration_Management.Models;
using Configuration_Management.Services;
using Xunit;

namespace ConfigurationManagement.Tests;

/// <summary>
/// Тесты чистого маппинга расписания заданий в форматы планировщиков ОС
/// (<see cref="OsScheduleMapper"/>): cron-строка Linux, управляемый блок crontab,
/// XML-определение задачи Windows (schtasks) и разбор статуса из /Query /XML.
/// </summary>
public sealed class OsScheduleMapperTests
{
    private static ScheduledTask NewTask(string time = "02:00", params DayOfWeek[] days) => new()
    {
        Enabled = true,
        Time = time,
        DaysOfWeek = days.ToList()
    };

    // ---------------------------------------------------------------------
    // BuildCronSchedule
    // ---------------------------------------------------------------------

    [Fact]
    public void CronSchedule_Daily_AllDays()
    {
        var task = NewTask("02:30");

        Assert.Equal("30 2 * * *", OsScheduleMapper.BuildCronSchedule(task));
    }

    [Fact]
    public void CronSchedule_Weekdays_MondayWednesdayFriday()
    {
        var task = NewTask("09:15", DayOfWeek.Monday, DayOfWeek.Wednesday, DayOfWeek.Friday);

        Assert.Equal("15 9 * * 1,3,5", OsScheduleMapper.BuildCronSchedule(task));
    }

    [Fact]
    public void CronSchedule_SundayAndSaturday_SundayIsZero()
    {
        var task = NewTask("00:00", DayOfWeek.Sunday, DayOfWeek.Saturday);

        Assert.Equal("0 0 * * 0,6", OsScheduleMapper.BuildCronSchedule(task));
    }

    [Fact]
    public void CronSchedule_UnsortedAndDuplicatedDays_Normalized()
    {
        var task = NewTask("08:00", DayOfWeek.Friday, DayOfWeek.Monday, DayOfWeek.Friday);

        Assert.Equal("0 8 * * 1,5", OsScheduleMapper.BuildCronSchedule(task));
    }

    [Fact]
    public void CronSchedule_InvalidTime_TreatedAsMidnight()
    {
        var task = NewTask("not-a-time");

        Assert.Equal("0 0 * * *", OsScheduleMapper.BuildCronSchedule(task));
    }

    // ---------------------------------------------------------------------
    // BuildCronEntry
    // ---------------------------------------------------------------------

    [Fact]
    public void CronEntry_PathWithSpaces_Quoted()
    {
        var task = NewTask("02:00");
        task.Id = "task123";

        var entry = OsScheduleMapper.BuildCronEntry(
            task, "/opt/My App/ConfigurationManagement", "prof1", "/home/user/.config/cm/logs/scheduler.log");

        Assert.StartsWith("0 2 * * * \"/opt/My App/ConfigurationManagement\" ", entry);
    }

    [Fact]
    public void CronEntry_ContainsRunTaskAndProfile()
    {
        var task = NewTask("02:00");
        task.Id = "task123";

        var entry = OsScheduleMapper.BuildCronEntry(
            task, "/usr/bin/ConfigurationManagement", "prof1", "/tmp/cm.log");

        Assert.Contains("--run-task task123", entry);
        Assert.Contains("--profile prof1", entry);
    }

    [Fact]
    public void CronEntry_RedirectsOutputToLog()
    {
        var task = NewTask("02:00");

        var entry = OsScheduleMapper.BuildCronEntry(
            task, "/usr/bin/ConfigurationManagement", "prof1", "/home/user/data dir/scheduler.log");

        Assert.EndsWith(">> \"/home/user/data dir/scheduler.log\" 2>&1", entry);
    }

    // ---------------------------------------------------------------------
    // Управляемый блок crontab
    // ---------------------------------------------------------------------

    [Fact]
    public void ReplaceManagedBlock_NoExistingBlock_Appends()
    {
        const string userCrontab = "0 1 * * * /usr/bin/backup\n";

        var updated = OsScheduleMapper.ReplaceManagedBlock(userCrontab, BuildSampleBlock());

        Assert.StartsWith("0 1 * * * /usr/bin/backup", updated);
        Assert.Contains(OsScheduleMapper.CrontabBeginMarker, updated);
        Assert.Contains(OsScheduleMapper.CrontabEndMarker, updated);
        Assert.Contains("# id=task1", updated);
    }

    [Fact]
    public void ReplaceManagedBlock_ExistingBlock_ReplacedUserLinesKept()
    {
        const string crontab =
            "0 1 * * * /usr/bin/backup\n" +
            "# BEGIN ConfigurationManagement\n" +
            "# id=old\n" +
            "5 5 * * * /old/exe --run-task old\n" +
            "# END ConfigurationManagement\n" +
            "0 2 * * * /usr/bin/cleanup\n";

        var updated = OsScheduleMapper.ReplaceManagedBlock(crontab, BuildSampleBlock());

        Assert.Contains("0 1 * * * /usr/bin/backup", updated);
        Assert.Contains("0 2 * * * /usr/bin/cleanup", updated);
        Assert.DoesNotContain("old", updated);
        Assert.Contains("# id=task1", updated);
        Assert.Contains("--run-task task1", updated);
    }

    [Fact]
    public void ReplaceManagedBlock_EmptyBlock_RemovesBlock()
    {
        const string crontab =
            "0 1 * * * /usr/bin/backup\n" +
            "# BEGIN ConfigurationManagement\n" +
            "# id=task1\n" +
            "5 5 * * * /old/exe --run-task task1\n" +
            "# END ConfigurationManagement\n";

        var updated = OsScheduleMapper.ReplaceManagedBlock(crontab, "");

        Assert.DoesNotContain(OsScheduleMapper.CrontabBeginMarker, updated);
        Assert.DoesNotContain("task1", updated);
        Assert.Contains("0 1 * * * /usr/bin/backup", updated);
    }

    [Fact]
    public void ReplaceManagedBlock_CrlfInput_NormalizedToLf()
    {
        var crontab = "0 1 * * * /usr/bin/backup\r\n";

        var updated = OsScheduleMapper.ReplaceManagedBlock(crontab, BuildSampleBlock());

        Assert.DoesNotContain("\r", updated);
    }

    [Fact]
    public void ExtractManagedTaskIds_OnlyFromBlock()
    {
        const string crontab =
            "0 1 * * * /usr/bin/backup\n" +
            "# id=outside\n" +
            "# BEGIN ConfigurationManagement\n" +
            "# id=task1\n" +
            "5 5 * * * /exe --run-task task1\n" +
            "# id=task2\n" +
            "6 6 * * * /exe --run-task task2\n" +
            "# END ConfigurationManagement\n";

        var ids = OsScheduleMapper.ExtractManagedTaskIds(crontab);

        Assert.Equal(new[] { "task1", "task2" }, ids);
    }

    [Fact]
    public void BuildCronBlock_RoundTrip_ReturnsIds()
    {
        var block = OsScheduleMapper.BuildCronBlock(new Dictionary<string, string>
        {
            ["a"] = "30 2 * * * /exe --run-task a",
            ["b"] = "0 9 * * 1,3,5 /exe --run-task b"
        });

        Assert.StartsWith(OsScheduleMapper.CrontabBeginMarker + "\n", block);
        Assert.EndsWith(OsScheduleMapper.CrontabEndMarker, block);
        Assert.Equal(new[] { "a", "b" }, OsScheduleMapper.ExtractManagedTaskIds(block));
    }

    private static string BuildSampleBlock() =>
        OsScheduleMapper.BuildCronBlock(new Dictionary<string, string>
        {
            ["task1"] = "30 2 * * * /exe --run-task task1 --profile prof1 >> /log 2>&1"
        });

    // ---------------------------------------------------------------------
    // BuildWindowsTaskXml
    // ---------------------------------------------------------------------

    [Fact]
    public void WindowsXml_Daily_ScheduleByDay()
    {
        var task = NewTask("02:00");

        var xml = OsScheduleMapper.BuildWindowsTaskXml(task, @"C:\App\ConfigurationManagement.exe", "prof1");
        var root = XDocument.Parse(xml).Root!;
        var ns = root.Name.Namespace;

        Assert.NotNull(root.Element(ns + "Triggers")?.Element(ns + "CalendarTrigger")?
            .Element(ns + "ScheduleByDay")?.Element(ns + "DaysInterval"));
        Assert.Null(root.Descendants(ns + "ScheduleByWeek").FirstOrDefault());
    }

    [Fact]
    public void WindowsXml_Weekly_DaysOfWeekElements()
    {
        var task = NewTask("09:00", DayOfWeek.Monday, DayOfWeek.Friday);

        var xml = OsScheduleMapper.BuildWindowsTaskXml(task, @"C:\App\ConfigurationManagement.exe", "prof1");
        var root = XDocument.Parse(xml).Root!;
        var ns = root.Name.Namespace;

        var week = root.Descendants(ns + "ScheduleByWeek").Single();
        Assert.Equal("1", (string?)week.Element(ns + "WeeksInterval"));
        var days = week.Element(ns + "DaysOfWeek")!.Elements().Select(e => e.Name.LocalName).ToList();
        Assert.Equal(new[] { "Monday", "Friday" }, days);
    }

    [Fact]
    public void WindowsXml_StartBoundary_LocalTimeFormat()
    {
        var task = NewTask("14:30");

        var xml = OsScheduleMapper.BuildWindowsTaskXml(task, @"C:\App\ConfigurationManagement.exe", "prof1");
        var root = XDocument.Parse(xml).Root!;
        var ns = root.Name.Namespace;

        var boundary = (string?)root.Descendants(ns + "StartBoundary").Single();
        Assert.Equal(DateTime.Today.ToString("yyyy-MM-dd") + "T14:30:00", boundary);
    }

    [Fact]
    public void WindowsXml_Exec_CommandAndArguments()
    {
        var task = NewTask("02:00");
        task.Id = "task123";

        var xml = OsScheduleMapper.BuildWindowsTaskXml(
            task, @"C:\Program Files\ConfigurationManagement\ConfigurationManagement.exe", "prof1");
        var root = XDocument.Parse(xml).Root!;
        var ns = root.Name.Namespace;

        var exec = root.Descendants(ns + "Exec").Single();
        Assert.Equal(@"C:\Program Files\ConfigurationManagement\ConfigurationManagement.exe",
            (string?)exec.Element(ns + "Command"));
        Assert.Equal("--run-task task123 --profile prof1", (string?)exec.Element(ns + "Arguments"));
    }

    [Fact]
    public void WindowsXml_PathWithAmpersandAndCyrillic_RoundTrips()
    {
        var path = @"F:\Yandex.Disk\Конфигурации & Co\ConfigurationManagement.exe";
        var task = NewTask("02:00");

        var xml = OsScheduleMapper.BuildWindowsTaskXml(task, path, "prof1");

        // Парсинг обратно даёт исходный путь — экранирование </& корректно.
        var root = XDocument.Parse(xml).Root!;
        var ns = root.Name.Namespace;
        Assert.Equal(path, (string?)root.Descendants(ns + "Command").Single());
        Assert.Contains("&", xml);
    }

    [Fact]
    public void WindowsXml_DisabledTask_EnabledFalseInSettingsAndTrigger()
    {
        var task = NewTask("02:00");
        task.Enabled = false;

        var xml = OsScheduleMapper.BuildWindowsTaskXml(task, @"C:\App\ConfigurationManagement.exe", "prof1");
        var root = XDocument.Parse(xml).Root!;
        var ns = root.Name.Namespace;

        Assert.Equal("false", (string?)root.Element(ns + "Settings")?.Element(ns + "Enabled"));
        Assert.Equal("false", (string?)root.Descendants(ns + "CalendarTrigger").Single()
            .Element(ns + "Enabled"));
    }

    [Fact]
    public void WindowsXml_InteractiveTokenWithoutAdmin()
    {
        var task = NewTask("02:00");

        var xml = OsScheduleMapper.BuildWindowsTaskXml(task, @"C:\App\ConfigurationManagement.exe", "prof1");
        var root = XDocument.Parse(xml).Root!;
        var ns = root.Name.Namespace;

        Assert.Equal("InteractiveToken", (string?)root.Descendants(ns + "LogonType").Single());
        Assert.Equal("LeastPrivilege", (string?)root.Descendants(ns + "RunLevel").Single());
    }

    // ---------------------------------------------------------------------
    // ParseWindowsTaskStatus
    // ---------------------------------------------------------------------

    [Fact]
    public void ParseWindowsTaskStatus_FullXml_ParsesAllFields()
    {
        const string xml = """
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <Settings><Enabled>true</Enabled></Settings>
              <LastRunTime>2026-09-29T02:00:00</LastRunTime>
              <LastTaskResult>267011</LastTaskResult>
              <NextRunTime>2026-09-30T02:00:00</NextRunTime>
            </Task>
            """;

        var status = OsScheduleMapper.ParseWindowsTaskStatus(xml);

        Assert.NotNull(status);
        Assert.True(status!.Registered);
        Assert.True(status.Enabled);
        Assert.Equal(new DateTime(2026, 9, 29, 2, 0, 0), status.LastRunTime);
        Assert.Equal(267011, status.LastTaskResult);
        Assert.Equal(new DateTime(2026, 9, 30, 2, 0, 0), status.NextRunTime);
    }

    [Fact]
    public void ParseWindowsTaskStatus_DisabledTask_ParsesEnabledFalse()
    {
        const string xml = """
            <Task xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <Settings><Enabled>false</Enabled></Settings>
            </Task>
            """;

        var status = OsScheduleMapper.ParseWindowsTaskStatus(xml);

        Assert.NotNull(status);
        Assert.False(status!.Enabled);
    }

    [Fact]
    public void ParseWindowsTaskStatus_NeverRun_EpochDateIsNull()
    {
        const string xml = """
            <Task xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <LastRunTime>1601-01-01T00:00:00</LastRunTime>
              <LastTaskResult>267009</LastTaskResult>
            </Task>
            """;

        var status = OsScheduleMapper.ParseWindowsTaskStatus(xml);

        Assert.NotNull(status);
        Assert.Null(status!.LastRunTime);
        Assert.Equal(267009, status.LastTaskResult);
    }

    [Fact]
    public void ParseWindowsTaskStatus_MissingOptionalFields_Tolerated()
    {
        const string xml = """
            <Task xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <Settings><Enabled>true</Enabled></Settings>
            </Task>
            """;

        var status = OsScheduleMapper.ParseWindowsTaskStatus(xml);

        Assert.NotNull(status);
        Assert.True(status!.Enabled);
        Assert.Null(status.LastRunTime);
        Assert.Null(status.NextRunTime);
        Assert.Null(status.LastTaskResult);
    }

    [Fact]
    public void ParseWindowsTaskStatus_Garbage_ReturnsNull()
    {
        Assert.Null(OsScheduleMapper.ParseWindowsTaskStatus("not an xml"));
        Assert.Null(OsScheduleMapper.ParseWindowsTaskStatus("<Wrong/>"));
        Assert.Null(OsScheduleMapper.ParseWindowsTaskStatus(""));
    }
}