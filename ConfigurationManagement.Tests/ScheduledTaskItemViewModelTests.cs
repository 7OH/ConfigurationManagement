using System.Collections.Generic;
using Configuration_Management.Localization;
using Configuration_Management.Models;
using Configuration_Management.ViewModels;
using Xunit;

namespace ConfigurationManagement.Tests;

/// <summary>
/// Строка списка заданий: активность (issue #372, п. 6), сводка расписания с
/// повтором (п. 7) и склонение единиц интервала.
/// </summary>
public class ScheduledTaskItemViewModelTests
{
    [Fact]
    public void IsActive_Setter_SavesTaskAndRaisesNotification()
    {
        var task = new ScheduledTask { Enabled = true };
        ScheduledTaskItemViewModel? saved = null;
        var changed = false;
        var vm = new ScheduledTaskItemViewModel(task, activity: item => saved = item);
        vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(ScheduledTaskItemViewModel.IsActive)) changed = true; };

        vm.IsActive = false;

        Assert.False(task.Enabled);
        Assert.Same(vm, saved);
        Assert.True(changed);
    }

    [Fact]
    public void IsActive_SetSameValue_DoesNotSave()
    {
        var task = new ScheduledTask { Enabled = true };
        var saves = 0;
        var vm = new ScheduledTaskItemViewModel(task, activity: _ => saves++);

        vm.IsActive = true;

        Assert.Equal(0, saves);
    }

    [Fact]
    public void SetActive_UpdatesOnlyModel()
    {
        var task = new ScheduledTask { Enabled = true };
        var vm = new ScheduledTaskItemViewModel(task);

        vm.SetActive(false);

        Assert.False(vm.IsActive);
        Assert.False(task.Enabled);
    }

    [Theory]
    [InlineData(1, new[] { "секунду", "секунды", "секунд" }, "секунду")]
    [InlineData(2, new[] { "секунду", "секунды", "секунд" }, "секунды")]
    [InlineData(5, new[] { "секунду", "секунды", "секунд" }, "секунд")]
    [InlineData(21, new[] { "час", "часа", "часов" }, "час")]
    [InlineData(22, new[] { "час", "часа", "часов" }, "часа")]
    [InlineData(25, new[] { "час", "часа", "часов" }, "часов")]
    [InlineData(11, new[] { "минуту", "минуты", "минут" }, "минут")]
    [InlineData(12, new[] { "минуту", "минуты", "минут" }, "минут")]
    [InlineData(1, new[] { "day", "days", "days" }, "day", "en")]
    [InlineData(3, new[] { "day", "days", "days" }, "days", "en")]
    public void Pluralize_ChoosesCorrectRussianForm(int value, string[] forms, string expected, string language = "ru")
    {
        Assert.Equal(expected, ScheduledTaskItemViewModel.Pluralize(value, forms, language));
    }

    [Fact]
    public void ScheduleSummary_DisabledRepeat_HasNoRepeatSuffix()
    {
        var task = new ScheduledTask { Time = "09:00", DaysOfWeek = new List<DayOfWeek>(), RepeatEnabled = false };
        var vm = new ScheduledTaskItemViewModel(task);

        // В тестовой среде T возвращает ключ; сравниваем через сам T.
        var expected = "09:00, " + LocalizationManager.T("Schedule.EveryDay");
        Assert.Equal(expected, vm.ScheduleSummary);
    }

    [Fact]
    public void ScheduleSummary_EnabledRepeat_AddsIntervalSuffix()
    {
        var task = new ScheduledTask
        {
            Time = "09:00",
            DaysOfWeek = new List<DayOfWeek>(),
            RepeatEnabled = true,
            RepeatIntervalValue = 2,
            RepeatIntervalUnit = ScheduledTaskRepeatUnit.Hours
        };
        var vm = new ScheduledTaskItemViewModel(task);

        // В тестовой среде LocalizationManager не инициализирован: T возвращает ключ,
        // поэтому единица измерения — первый элемент-«один» ключа формы (форм не найти).
        var expectedSuffix = ", " + string.Format(
            LocalizationManager.T("Schedule.Repeat.Summary"), 2,
            ScheduledTaskItemViewModel.Pluralize(2,
                (LocalizationManager.T("Schedule.Repeat.Unit.Hours.Forms") ?? "").Split(','),
                LocalizationManager.Instance.CurrentLanguage));
        Assert.EndsWith(expectedSuffix, vm.ScheduleSummary, System.StringComparison.Ordinal);
    }
}