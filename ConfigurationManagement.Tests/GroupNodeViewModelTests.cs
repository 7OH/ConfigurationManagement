using Configuration_Management.Models;
using Configuration_Management.ViewModels;
using Xunit;

namespace ConfigurationManagement.Tests;

/// <summary>
/// Тесты инварианта <see cref="GroupNodeViewModel.PopulateItems"/> (регресс 0.3.9.115,
/// issue #317): узел «Закреплённые» (<see cref="GroupNodeViewModel.PinnedMarker"/>)
/// хранит обёртки <see cref="PinnedInfobaseItem"/>, все остальные узлы — прямые
/// экземпляры <see cref="Infobase"/>. Именно эта разница требует, чтобы WPF-шаблон
/// строки базы применялся автоматически к <c>Infobase</c> (а не только по ключу
/// через ContentControl для закреплённых строк).
/// </summary>
public sealed class GroupNodeViewModelTests
{
    private static Infobase CreateBase(string name = "База") => new()
    {
        Name = name,
        Connection = new ConnectionSettings { Type = ConnectionType.File, FilePath = @"C:\bases\demo" }
    };

    [Fact]
    public void PopulateItems_PinnedNode_WrapsEachBaseIntoPinnedInfobaseItem()
    {
        var base1 = CreateBase("База 1");
        var base2 = CreateBase("База 2");
        var pinned = new GroupNodeViewModel(null, marker: GroupNodeViewModel.PinnedMarker);
        pinned.Infobases.Add(base1);
        pinned.Infobases.Add(base2);

        pinned.PopulateItems();

        Assert.Equal(2, pinned.Items.Count);
        Assert.All(pinned.Items, item => Assert.IsType<PinnedInfobaseItem>(item));
        Assert.Same(base1, Assert.IsType<PinnedInfobaseItem>(pinned.Items[0]).Base);
        Assert.Same(base2, Assert.IsType<PinnedInfobaseItem>(pinned.Items[1]).Base);
    }

    [Fact]
    public void PopulateItems_RegularGroup_KeepsRawInfobaseInstances()
    {
        var infobase = CreateBase();
        var group = new GroupNodeViewModel(new Group { Id = "g1", Name = "Группа" });
        group.Infobases.Add(infobase);

        group.PopulateItems();

        var item = Assert.Single(group.Items);
        Assert.Same(infobase, Assert.IsType<Infobase>(item));
    }

    [Fact]
    public void PopulateItems_NoGroupNode_KeepsRawInfobaseInstances()
    {
        var infobase = CreateBase();
        var noGroup = new GroupNodeViewModel(null, marker: GroupNodeViewModel.NoGroupMarker);
        noGroup.Infobases.Add(infobase);

        noGroup.PopulateItems();

        var item = Assert.Single(noGroup.Items);
        Assert.Same(infobase, Assert.IsType<Infobase>(item));
    }
}