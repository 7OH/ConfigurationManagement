using System.Linq;
using Configuration_Management.Models;
using Configuration_Management.ViewModels;
using Xunit;

namespace ConfigurationManagement.Tests;

/// <summary>
/// Фильтр списка заданий по расписанию (issue #372, п. 1–2): отбор по типу действия
/// и поиск по всем полям (имя, тип, расписание, результат, Id).
/// </summary>
public class ScheduledTaskListFilterTests
{
    private static ScheduledTaskItemViewModel Item(string name, ScheduledTaskKind kind)
    {
        var task = new ScheduledTask
        {
            Id = "id_" + name,
            Name = name,
            Kind = kind,
            Time = "09:00"
        };
        return new ScheduledTaskItemViewModel(task);
    }

    [Fact]
    public void Apply_NoFilters_ReturnsAllItems()
    {
        var items = new[] { Item("A", ScheduledTaskKind.Backup), Item("B", ScheduledTaskKind.UpdateApp) };

        var result = ScheduledTaskListFilter.Apply(items, null, null);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void Apply_KindFilter_ReturnsOnlyThatKind()
    {
        var items = new[]
        {
            Item("UpdApp", ScheduledTaskKind.UpdateApp),
            Item("BackupA", ScheduledTaskKind.Backup),
            Item("BackupB", ScheduledTaskKind.Backup)
        };

        var result = ScheduledTaskListFilter.Apply(items, ScheduledTaskKind.Backup, null);

        Assert.Equal(2, result.Count);
        Assert.All(result, i => Assert.Equal(ScheduledTaskKind.Backup, i.Task.Kind));
    }

    [Fact]
    public void Apply_SearchMatchesName_CaseInsensitive()
    {
        var items = new[] { Item("Резервная копия УТ", ScheduledTaskKind.Backup), Item("Розница", ScheduledTaskKind.UpdateApp) };

        var result = ScheduledTaskListFilter.Apply(items, null, "РЕЗЕРВНАЯ");

        Assert.Single(result);
        Assert.Equal("Резервная копия УТ", result[0].Name);
    }

    [Fact]
    public void Apply_SearchMatchesKindTextOrSummary()
    {
        var items = new[]
        {
            Item("Задание платформы", ScheduledTaskKind.PlatformUpdateCheck)
        };

        // Поиск по тексту типа (в тестовой среде T возвращает ключ локализации).
        var byKind = ScheduledTaskListFilter.Apply(items, null, "Schedule.Kind.PlatformUpdateCheck");
        Assert.Single(byKind);

        // Поиск по сводке расписания: «09:00, ежедневно».
        var bySummary = ScheduledTaskListFilter.Apply(items, null, "09:00");
        Assert.Single(bySummary);

        // Поиск по Id.
        var byId = ScheduledTaskListFilter.Apply(items, null, "id_Задание");
        Assert.Single(byId);
    }

    [Fact]
    public void Apply_CombinesKindAndSearch()
    {
        var items = new[]
        {
            Item("Альфа", ScheduledTaskKind.Backup),
            Item("Бета", ScheduledTaskKind.UpdateApp)
        };

        Assert.Empty(ScheduledTaskListFilter.Apply(items, ScheduledTaskKind.Backup, "Гамма"));
        Assert.Single(ScheduledTaskListFilter.Apply(items, ScheduledTaskKind.Backup, "Альфа"));
    }

    [Fact]
    public void Apply_NullItems_ReturnsEmpty()
    {
        Assert.Empty(ScheduledTaskListFilter.Apply(null, null, ""));
    }
}