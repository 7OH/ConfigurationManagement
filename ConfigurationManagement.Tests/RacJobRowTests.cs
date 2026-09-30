using Configuration_Management.Localization;
using Configuration_Management.Models;
using Configuration_Management.ViewModels;
using Xunit;

namespace ConfigurationManagement.Tests;

/// <summary>
/// Тесты строки вкладки «Регламентные задания» (<see cref="RacJobRow"/>): форматирование
/// времени/результата, локализация состояния, признак предопределённости, доступность
/// действий «Приостановить/Возобновить» и текст деталей.
/// </summary>
public sealed class RacJobRowTests
{
    private static RacJobInfo Job(
        string state = "scheduled",
        string name = "Обмен данными",
        System.Guid? infobaseId = null,
        bool predefined = false,
        string schedule = "",
        string result = "")
    {
        return new RacJobInfo
        {
            Id = System.Guid.Parse("aaaaaaaa-1111-2222-3333-444455556666"),
            InfobaseId = infobaseId,
            Name = name,
            MethodName = "ВыполнитьОбмен",
            Predefined = predefined,
            Schedule = schedule,
            State = state,
            NextStart = new System.DateTime(2026, 10, 1, 3, 0, 0),
            LastStart = new System.DateTime(2026, 9, 30, 3, 0, 0),
            Result = result
        };
    }

    [Fact]
    public void Constructor_StoresInfobaseName()
    {
        var row = new RacJobRow(Job(infobaseId: System.Guid.NewGuid()), "Бухгалтерия");

        Assert.Equal("Бухгалтерия", row.InfobaseName);
        Assert.Equal("Обмен данными", row.Name);
        Assert.Equal("ВыполнитьОбмен", row.MethodName);
    }

    [Fact]
    public void StateText_IsLocalized_ByRawState()
    {
        Assert.Equal(LocalizationManager.T("ServerMonitor.Job.State.Running"),
            new RacJobRow(Job("running")).StateText);
        Assert.Equal(LocalizationManager.T("ServerMonitor.Job.State.Scheduled"),
            new RacJobRow(Job("scheduled")).StateText);
        Assert.Equal(LocalizationManager.T("ServerMonitor.Job.State.Paused"),
            new RacJobRow(Job("paused")).StateText);
        Assert.Equal("—", new RacJobRow(Job("")).StateText);
    }

    [Fact]
    public void CanPause_CanResume_FollowState()
    {
        Assert.True(new RacJobRow(Job("scheduled")).CanPause);
        Assert.False(new RacJobRow(Job("scheduled")).CanResume);

        Assert.True(new RacJobRow(Job("running")).CanPause);

        Assert.False(new RacJobRow(Job("paused")).CanPause);
        Assert.True(new RacJobRow(Job("paused")).CanResume);

        Assert.False(new RacJobRow(Job("disabled")).CanPause);
        Assert.False(new RacJobRow(Job("disabled")).CanResume);

        Assert.False(new RacJobRow(Job("")).CanPause);
    }

    [Fact]
    public void PredefinedText_UsesYesNo()
    {
        Assert.Equal(LocalizationManager.T("Common.Yes"), new RacJobRow(Job(predefined: true)).PredefinedText);
        Assert.Equal(LocalizationManager.T("Common.No"), new RacJobRow(Job(predefined: false)).PredefinedText);
    }

    [Fact]
    public void Schedule_EmptyShowsDash()
    {
        Assert.Equal("—", new RacJobRow(Job()).Schedule);
        Assert.Equal("0 0 3 * * ? *", new RacJobRow(Job(schedule: "0 0 3 * * ? *")).Schedule);
    }

    [Fact]
    public void ResultText_TruncatesOverMaxLength()
    {
        var longResult = new string('x', RacJobRow.MaxResultLength + 20);
        var row = new RacJobRow(Job(result: longResult));

        Assert.EndsWith("…", row.ResultText);
        Assert.True(row.ResultText.Length <= RacJobRow.MaxResultLength + 1);
    }

    [Fact]
    public void DetailsText_ContainsAllKeyFields()
    {
        var row = new RacJobRow(
            Job(infobaseId: System.Guid.Parse("cccccccc-1111-2222-3333-444455556666"),
                schedule: "0 0 3 * * ? *", result: "Завершено успешно"),
            "Бухгалтерия");

        var details = row.DetailsText;

        Assert.Contains("job: aaaaaaaa-1111-2222-3333-444455556666", details);
        Assert.Contains("infobase: cccccccc-1111-2222-3333-444455556666", details);
        Assert.Contains("infobase-name: Бухгалтерия", details);
        Assert.Contains("name: Обмен данными", details);
        Assert.Contains("method-name: ВыполнитьОбмен", details);
        Assert.Contains("schedule: 0 0 3 * * ? *", details);
        Assert.Contains("state: scheduled", details);
        Assert.Contains("next-start: 2026-10-01T03:00:00", details);
        Assert.Contains("last-start: 2026-09-30T03:00:00", details);
        Assert.Contains("result: Завершено успешно", details);
    }
}