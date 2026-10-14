using System.Collections.Generic;
using Configuration_Management.Models;
using Configuration_Management.ViewModels;
using Xunit;

namespace ConfigurationManagement.Tests;

/// <summary>
/// Тесты формы задания по расписанию (issue #372): поиск пункта списка по значению
/// для установки <c>SelectedItem</c> (закрытый ModernComboBox показывал ключ вместо названия).
/// </summary>
public sealed class ScheduledTaskEditViewModelTests
{
    private static IReadOnlyList<ScheduledTaskOption> Options() => new[]
    {
        new ScheduledTaskOption("Backup", "Резервная копия"),
        new ScheduledTaskOption("UpdateConfig", "Обновление конфигурации"),
        new ScheduledTaskOption("CheckIntegrity", "Проверка целостности"),
    };

    [Fact]
    public void FindOption_KnownValue_ReturnsOption()
    {
        var option = ScheduledTaskEditViewModel.FindOption(Options(), "UpdateConfig");

        Assert.NotNull(option);
        Assert.Equal("UpdateConfig", option!.Value);
        Assert.Equal("Обновление конфигурации", option.Text);
    }

    [Fact]
    public void FindOption_UnknownValue_ReturnsNull()
    {
        Assert.Null(ScheduledTaskEditViewModel.FindOption(Options(), "Unknown"));
    }

    [Fact]
    public void FindOption_NullOrEmptyValue_ReturnsNull()
    {
        Assert.Null(ScheduledTaskEditViewModel.FindOption(Options(), null));
        Assert.Null(ScheduledTaskEditViewModel.FindOption(Options(), string.Empty));
    }

    [Fact]
    public void FindOption_NullOptions_ReturnsNull()
    {
        Assert.Null(ScheduledTaskEditViewModel.FindOption(null, "Backup"));
    }

    [Fact]
    public void FindOption_IsCaseSensitiveByValue()
    {
        // Значения — стабильные ключи (Backup), регистр учитывается.
        Assert.Null(ScheduledTaskEditViewModel.FindOption(Options(), "backup"));
    }

    // ---- Кэширование списков опций (issue #372) ----

    [Fact]
    public void KindOptions_AreCached_FindOptionReturnsInstanceFromSameList()
    {
        // Списки опций должны возвращать одни и те же экземпляры при каждом обращении:
        // иначе SelectedItem (из FindOption) не совпадает с ItemsSource, и закрытый
        // комбобокс показывает ключ вместо названия.
        var vm = new ScheduledTaskEditViewModel(null, new List<BackupScenario>(), new List<Infobase>());

        var options = vm.KindOptions;
        var found = ScheduledTaskEditViewModel.FindOption(vm.KindOptions, "Backup");

        Assert.NotNull(found);
        Assert.True(options.Contains(found!));
    }

    [Fact]
    public void NotifyOptions_AreCached_FindOptionReturnsInstanceFromSameList()
    {
        var vm = new ScheduledTaskEditViewModel(null, new List<BackupScenario>(), new List<Infobase>());

        var options = vm.NotifyOptions;
        var found = ScheduledTaskEditViewModel.FindOption(vm.NotifyOptions, "MainWindow");

        Assert.NotNull(found);
        Assert.True(options.Contains(found!));
    }

    [Fact]
    public void RepeatUnitOptions_AreCached_FindOptionReturnsInstanceFromSameList()
    {
        var vm = new ScheduledTaskEditViewModel(null, new List<BackupScenario>(), new List<Infobase>());

        var options = vm.RepeatUnitOptions;
        var found = ScheduledTaskEditViewModel.FindOption(vm.RepeatUnitOptions, "Minutes");

        Assert.NotNull(found);
        Assert.True(options.Contains(found!));
    }

    [Fact]
    public void ScenarioOptions_AreCached_FindOptionReturnsInstanceFromSameList()
    {
        var scenarios = new List<BackupScenario> { new() { Id = "s1", Name = "Сценарий 1" } };
        var vm = new ScheduledTaskEditViewModel(null, scenarios, new List<Infobase>());

        var options = vm.ScenarioOptions;
        var found = ScheduledTaskEditViewModel.FindOption(vm.ScenarioOptions, "s1");

        Assert.NotNull(found);
        Assert.True(options.Contains(found!));
    }

    [Fact]
    public void InfobaseOptions_AreCached_FindOptionReturnsInstanceFromSameList()
    {
        var bases = new List<Infobase> { new() { Id = "b1", Name = "База 1" } };
        var vm = new ScheduledTaskEditViewModel(null, new List<BackupScenario>(), bases);

        var options = vm.InfobaseOptions;
        var found = ScheduledTaskEditViewModel.FindOption(vm.InfobaseOptions, "b1");

        Assert.NotNull(found);
        Assert.True(options.Contains(found!));
    }
}
