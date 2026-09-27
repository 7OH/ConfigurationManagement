using Configuration_Management.ViewModels;
using Xunit;

namespace ConfigurationManagement.Tests;

/// <summary>
/// Тесты чистой логики командной палитры (Ctrl+K): фильтрация, ранжирование,
/// навигация и сброс выбора при смене запроса.
/// </summary>
public sealed class CommandPaletteViewModelTests
{
    private static CommandPaletteItem Base(string name, string subtitle = "") => new()
    {
        Kind = CommandPaletteItemKind.Base,
        Id = "id-" + name,
        Title = name,
        Subtitle = subtitle
    };

    private static CommandPaletteItem Command(string title) => new()
    {
        Kind = CommandPaletteItemKind.Command,
        Id = "cmd-" + title,
        Title = title
    };

    private static CommandPaletteViewModel CreateVm()
    {
        var vm = new CommandPaletteViewModel();
        vm.SetSource(
            new[] { Base("Бухгалтерия", "Файловая"), Base("Зарплата", "Сервер"), Base("Архив бухгалтерии") },
            new[] { Command("Настройки"), Command("Синхронизация с ibases.v8i") });
        return vm;
    }

    [Fact]
    public void EmptyQuery_ReturnsAll_BasesFirst()
    {
        var vm = CreateVm();
        vm.ApplyQuery("");

        Assert.Equal(5, vm.VisibleItems.Count);
        Assert.Equal(CommandPaletteItemKind.Base, vm.VisibleItems[0].Kind);
        Assert.Equal(CommandPaletteItemKind.Command, vm.VisibleItems[^1].Kind);
    }

    [Fact]
    public void Filter_IsCaseInsensitive_AndMatchesSubtitle()
    {
        var vm = CreateVm();

        vm.ApplyQuery("зарп");
        Assert.Single(vm.VisibleItems);
        Assert.Equal("Зарплата", vm.VisibleItems[0].Title);

        vm.ApplyQuery("сервер");
        Assert.Single(vm.VisibleItems);
        Assert.Equal("Зарплата", vm.VisibleItems[0].Title);
    }

    [Fact]
    public void Filter_PrefixMatch_RanksAboveContains()
    {
        var vm = new CommandPaletteViewModel();
        vm.SetSource(
            new[] { Base("Бухгалтерия"), Base("Новая бухгалтерия") },
            System.Array.Empty<CommandPaletteItem>());

        vm.ApplyQuery("бух");
        Assert.Equal(2, vm.VisibleItems.Count);
        Assert.Equal("Бухгалтерия", vm.VisibleItems[0].Title);
    }

    [Fact]
    public void Filter_MultiWord_RequiresAllWords()
    {
        var vm = CreateVm();

        vm.ApplyQuery("архив бухгалтер");
        Assert.Single(vm.VisibleItems);
        Assert.Equal("Архив бухгалтерии", vm.VisibleItems[0].Title);

        vm.ApplyQuery("архив зарплата");
        Assert.Empty(vm.VisibleItems);
    }

    [Fact]
    public void QueryChange_ResetsSelection()
    {
        var vm = CreateVm();
        vm.ApplyQuery("");
        vm.MoveDown();
        vm.MoveDown();
        Assert.Equal(2, vm.SelectedIndex);

        vm.ApplyQuery("настройки");
        Assert.Equal(0, vm.SelectedIndex);
        Assert.Equal("Настройки", vm.Current!.Title);
    }

    [Fact]
    public void MoveDownUp_RespectsBounds()
    {
        var vm = CreateVm();
        vm.ApplyQuery("");

        Assert.True(vm.MoveDown());
        Assert.True(vm.MoveDown());
        Assert.True(vm.MoveDown());
        Assert.True(vm.MoveDown());
        Assert.False(vm.MoveDown()); // 5 элементов, дальше нельзя
        Assert.Equal(4, vm.SelectedIndex);

        Assert.True(vm.MoveUp());
        Assert.True(vm.MoveUp());
        Assert.True(vm.MoveUp());
        Assert.True(vm.MoveUp());
        Assert.False(vm.MoveUp());
        Assert.Equal(0, vm.SelectedIndex);
    }

    [Fact]
    public void Reset_ClearsQueryAndSelectsFirst()
    {
        var vm = CreateVm();
        vm.ApplyQuery("настройки");
        vm.Reset();

        Assert.Equal("", vm.Query);
        Assert.Equal(5, vm.VisibleItems.Count);
        Assert.Equal(0, vm.SelectedIndex);
    }

    [Fact]
    public void EmptyResult_CurrentIsNull()
    {
        var vm = CreateVm();
        vm.ApplyQuery("неттакого");

        Assert.Empty(vm.VisibleItems);
        Assert.Null(vm.Current);
    }
}
