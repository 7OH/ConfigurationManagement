using Configuration_Management.ViewModels;
using Xunit;

namespace ConfigurationManagement.Tests;

/// <summary>
/// Тесты ViewModel редактора структуры меню «Утилиты» (issue #374): создание группы
/// с именем/иконкой, переименование, удаление с переносом элементов в корень,
/// доступность команд и назначение иконок. Чистый .NET — без UI.
/// </summary>
public sealed class UtilitiesMenuEditorViewModelTests
{
    private static UtilitiesMenuEditorViewModel CreateViewModel() => new(null);

    // ---- Создание группы ----

    [Fact]
    public void AddGroup_CreatesGroupWithTitleInRoot()
    {
        var vm = CreateViewModel();
        var before = vm.Roots.Count;

        vm.AddGroupCommand.Execute(new UtilitiesGroupEditParams("Моя группа", null));

        Assert.Equal(before + 1, vm.Roots.Count);
        var group = vm.Roots[^1];
        Assert.Equal("group", group.Node.Kind);
        Assert.Equal("Моя группа", group.Node.CustomTitle);
        Assert.Equal("Моя группа", group.Title);
        Assert.True(vm.Groups.Contains(group));
        Assert.True(ReferenceEquals(vm.SelectedItem, group));
    }

    [Fact]
    public void AddGroup_WithEmptyTitle_UsesDefaultName()
    {
        var vm = CreateViewModel();
        var before = vm.Roots.Count;

        vm.AddGroupCommand.Execute(new UtilitiesGroupEditParams("   ", null));

        Assert.Equal(before + 1, vm.Roots.Count);
        Assert.False(string.IsNullOrWhiteSpace(vm.Roots[^1].Node.CustomTitle));
    }

    [Fact]
    public void AddGroup_InsideSelectedGroup_AddsChild()
    {
        var vm = CreateViewModel();
        // Выбираем первую группу дерева (стандартная компоновка содержит группы).
        var parent = vm.Groups[0];
        vm.SelectedItem = parent;
        var before = parent.Children.Count;

        vm.AddGroupCommand.Execute(new UtilitiesGroupEditParams("Вложенная", null));

        Assert.Equal(before + 1, parent.Children.Count);
        var child = parent.Children[^1];
        Assert.Equal("Вложенная", child.Node.CustomTitle);
        Assert.True(ReferenceEquals(child.Parent, parent));
    }

    [Fact]
    public void AddGroup_WithIconKey_SetsIconAndColor()
    {
        var vm = CreateViewModel();
        var icon = vm.CatalogIcons.FirstOrDefault();
        if (icon is null)
            return; // Каталог иконок пуст — нечего проверять.

        vm.AddGroupCommand.Execute(new UtilitiesGroupEditParams("С иконкой", icon.Key));

        var group = vm.Roots[^1];
        Assert.Equal(icon.Key, group.Node.IconKey);
        Assert.Equal(icon.Color, group.Node.IconColor);
        Assert.Null(group.Node.ExternalIconPath);
    }

    // ---- Переименование ----

    [Fact]
    public void EditGroup_RenamesSelectedGroup()
    {
        var vm = CreateViewModel();
        var group = vm.Groups[0];
        vm.SelectedItem = group;

        vm.EditGroupCommand.Execute(new UtilitiesGroupEditParams("Новое имя", null));

        Assert.Equal("Новое имя", group.Node.CustomTitle);
        Assert.Equal("Новое имя", group.Title);
    }

    [Fact]
    public void EditGroup_WithoutSelection_DoesNothing()
    {
        var vm = CreateViewModel();
        vm.SelectedItem = null;

        vm.EditGroupCommand.Execute(new UtilitiesGroupEditParams("Имя", null));

        // Ничего не сломалось, дерево не изменилось.
        Assert.True(vm.Roots.Count > 0);
    }

    // ---- Удаление ----

    [Fact]
    public void DeleteGroup_EmptyGroup_RemovesIt()
    {
        var vm = CreateViewModel();
        vm.AddGroupCommand.Execute(new UtilitiesGroupEditParams("Пустая", null));
        var group = vm.SelectedItem!;
        var before = vm.Roots.Count;

        vm.DeleteGroupCommand.Execute(false);

        Assert.Equal(before - 1, vm.Roots.Count);
        Assert.False(vm.Roots.Contains(group));
        Assert.False(vm.Groups.Contains(group));
        Assert.Null(vm.SelectedItem);
    }

    [Fact]
    public void DeleteGroup_WithItems_MoveToRoot_MovesChildrenToRoot()
    {
        var vm = CreateViewModel();
        // Создаём группу и кладём в неё пункт.
        vm.AddGroupCommand.Execute(new UtilitiesGroupEditParams("Группа", null));
        var group = vm.SelectedItem!;
        var item = vm.Roots.FirstOrDefault(n => string.Equals(n.Node.Kind, "item", StringComparison.Ordinal));
        if (item is null)
            return;
        vm.SelectedItem = item;
        vm.SelectedGroup = group;
        vm.MoveToGroupCommand.Execute(null);
        Assert.Equal(1, group.Children.Count);

        // Удаляем группу с переносом в корень.
        var indexBefore = vm.Roots.IndexOf(group);
        vm.SelectedItem = group;
        vm.DeleteGroupCommand.Execute(true);

        Assert.False(vm.Roots.Contains(group));
        Assert.False(vm.Groups.Contains(group));
        // Элемент перенесён в корень на место группы.
        Assert.True(vm.Roots.Contains(item));
        Assert.Null(item.Parent);
        Assert.Equal(indexBefore, vm.Roots.IndexOf(item));
    }

    [Fact]
    public void DeleteGroup_WithItems_WithoutMoveToRoot_DeletesWithChildren()
    {
        var vm = CreateViewModel();
        vm.AddGroupCommand.Execute(new UtilitiesGroupEditParams("Группа", null));
        var group = vm.SelectedItem!;
        var item = vm.Roots.FirstOrDefault(n => string.Equals(n.Node.Kind, "item", StringComparison.Ordinal));
        if (item is null)
            return;
        vm.SelectedItem = item;
        vm.SelectedGroup = group;
        vm.MoveToGroupCommand.Execute(null);

        vm.SelectedItem = group;
        vm.DeleteGroupCommand.Execute(false);

        Assert.False(vm.Roots.Contains(group));
        // Элемент удалён вместе с группой (не перенесён в корень).
        Assert.False(vm.Roots.Contains(item));
    }

    [Fact]
    public void DeleteGroup_NestedGroup_MovesAllToRoot()
    {
        var vm = CreateViewModel();
        // Группа → вложенная группа → пункт.
        vm.AddGroupCommand.Execute(new UtilitiesGroupEditParams("Внешняя", null));
        var outer = vm.SelectedItem!;
        vm.AddGroupCommand.Execute(new UtilitiesGroupEditParams("Внутренняя", null));
        var inner = vm.SelectedItem!;
        Assert.True(ReferenceEquals(inner.Parent, outer));

        var item = vm.Roots.FirstOrDefault(n => string.Equals(n.Node.Kind, "item", StringComparison.Ordinal));
        if (item is null)
            return;
        vm.SelectedItem = item;
        vm.SelectedGroup = inner;
        vm.MoveToGroupCommand.Execute(null);
        Assert.Equal(1, inner.Children.Count);

        // Удаляем внешнюю группу с переносом в корень: внутренняя подгруппа — в корень,
        // её содержимое (пункт) остаётся внутри неё.
        vm.SelectedItem = outer;
        vm.DeleteGroupCommand.Execute(true);

        Assert.False(vm.Roots.Contains(outer));
        Assert.True(vm.Roots.Contains(inner));
        Assert.False(vm.Roots.Contains(item));
        Assert.Null(inner.Parent);
        Assert.True(ReferenceEquals(item.Parent, inner));
        Assert.True(vm.Groups.Contains(inner));
    }

    // ---- Доступность команд ----

    [Fact]
    public void CanEditAndDeleteGroup_OnlyForSelectedGroup()
    {
        var vm = CreateViewModel();
        Assert.False(vm.CanEditGroup);
        Assert.False(vm.CanDeleteGroup);

        var group = vm.Groups[0];
        vm.SelectedItem = group;
        Assert.True(vm.CanEditGroup);
        Assert.True(vm.CanDeleteGroup);

        var item = vm.Roots.FirstOrDefault(n => string.Equals(n.Node.Kind, "item", StringComparison.Ordinal));
        if (item is not null)
        {
            vm.SelectedItem = item;
            Assert.False(vm.CanEditGroup);
            Assert.False(vm.CanDeleteGroup);
        }
    }

    // ---- Иконки ----

    [Fact]
    public void ApplyCatalogIcon_SetsIconAndClearsExternal()
    {
        var vm = CreateViewModel();
        var icon = vm.CatalogIcons.FirstOrDefault();
        if (icon is null)
            return;
        var item = vm.Roots.FirstOrDefault(n => string.Equals(n.Node.Kind, "item", StringComparison.Ordinal));
        if (item is null)
            return;
        item.Node.ExternalIconPath = "C:/icon.png";
        vm.SelectedItem = item;

        vm.SetCatalogIconCommand.Execute(icon.Key);

        Assert.Equal(icon.Key, item.Node.IconKey);
        Assert.Equal(icon.Color, item.Node.IconColor);
        Assert.Null(item.Node.ExternalIconPath);
    }

    [Fact]
    public void ClearIcon_ResetsAllIconFields()
    {
        var vm = CreateViewModel();
        var item = vm.Roots.FirstOrDefault(n => string.Equals(n.Node.Kind, "item", StringComparison.Ordinal));
        if (item is null)
            return;
        item.Node.IconKey = "Cog";
        item.Node.IconColor = "#FFFFFF";
        item.Node.ExternalIconPath = "C:/icon.png";
        vm.SelectedItem = item;

        vm.ClearIconCommand.Execute(null);

        Assert.Null(item.Node.IconKey);
        Assert.Null(item.Node.IconColor);
        Assert.Null(item.Node.ExternalIconPath);
    }

    [Fact]
    public void IconOption_LabelContainsKeyAndColor()
    {
        var option = new UtilitiesIconOption("Cog", "#FFFFFF");
        Assert.Equal("Cog (#FFFFFF)", option.Label);
    }

    // ---- Разделители (issue #374) ----

    [Fact]
    public void AddSeparator_NoSelection_AddsToRoot()
    {
        var vm = CreateViewModel();
        var before = vm.Roots.Count;

        vm.AddSeparatorCommand.Execute(null);

        Assert.Equal(before + 1, vm.Roots.Count);
        var sep = vm.Roots[^1];
        Assert.Equal("separator", sep.Node.Kind);
        Assert.True(ReferenceEquals(vm.SelectedItem, sep));
    }

    [Fact]
    public void AddSeparator_AfterSelected_InsertsSibling()
    {
        var vm = CreateViewModel();
        var item = vm.Roots.FirstOrDefault(n => string.Equals(n.Node.Kind, "item", StringComparison.Ordinal));
        if (item is null)
            return;
        vm.SelectedItem = item;
        var index = vm.Roots.IndexOf(item);

        vm.AddSeparatorCommand.Execute(null);

        Assert.Equal("separator", vm.Roots[index + 1].Node.Kind);
        Assert.True(ReferenceEquals(vm.SelectedItem, vm.Roots[index + 1]));
    }

    [Fact]
    public void DeleteSeparator_RemovesIt()
    {
        var vm = CreateViewModel();
        vm.AddSeparatorCommand.Execute(null);
        var sep = vm.SelectedItem!;
        var before = vm.Roots.Count;

        vm.DeleteGroupCommand.Execute(false);

        Assert.Equal(before - 1, vm.Roots.Count);
        Assert.False(vm.Roots.Contains(sep));
        Assert.Null(vm.SelectedItem);
    }

    // ---- Перенос в корень (issue #374) ----

    [Fact]
    public void MoveToRoot_MovesSelectedToRoot()
    {
        var vm = CreateViewModel();
        var group = vm.Groups[0];
        var item = vm.Roots.FirstOrDefault(n => string.Equals(n.Node.Kind, "item", StringComparison.Ordinal));
        if (item is null)
            return;
        // Кладём пункт в группу.
        vm.SelectedItem = item;
        vm.SelectedGroup = group;
        vm.MoveToGroupCommand.Execute(null);
        Assert.True(ReferenceEquals(item.Parent, group));

        // Переносим в корень.
        vm.MoveToRootCommand.Execute(null);

        Assert.Null(item.Parent);
        Assert.True(vm.Roots.Contains(item));
    }

    [Fact]
    public void MoveToRoot_RootItem_DoesNothing()
    {
        var vm = CreateViewModel();
        var item = vm.Roots.FirstOrDefault(n => string.Equals(n.Node.Kind, "item", StringComparison.Ordinal));
        if (item is null)
            return;
        vm.SelectedItem = item;
        var before = vm.Roots.Count;

        vm.MoveToRootCommand.Execute(null);

        Assert.Equal(before, vm.Roots.Count);
        Assert.True(vm.Roots.Contains(item));
    }

    // ---- Перемещение группы в группу (issue #374) ----

    [Fact]
    public void MoveGroupIntoGroup_MovesGroup()
    {
        var vm = CreateViewModel();
        vm.AddGroupCommand.Execute(new UtilitiesGroupEditParams("Внешняя", null));
        var outer = vm.SelectedItem!;
        vm.AddGroupCommand.Execute(new UtilitiesGroupEditParams("Внутренняя", null));
        var inner = vm.SelectedItem!;
        Assert.True(ReferenceEquals(inner.Parent, outer));

        // Переносим внешнюю группу в другую группу дерева.
        var target = vm.Groups.FirstOrDefault(g => !ReferenceEquals(g, outer) && !ReferenceEquals(g, inner));
        if (target is null)
            return;
        vm.SelectedItem = outer;
        vm.SelectedGroup = target;
        Assert.True(vm.CanMoveToGroup);

        vm.MoveToGroupCommand.Execute(null);

        Assert.True(ReferenceEquals(outer.Parent, target));
        Assert.True(target.Children.Contains(outer));
    }

    [Fact]
    public void MoveGroupIntoItself_NotAllowed()
    {
        var vm = CreateViewModel();
        vm.AddGroupCommand.Execute(new UtilitiesGroupEditParams("Группа", null));
        var group = vm.SelectedItem!;

        vm.SelectedItem = group;
        vm.SelectedGroup = group;

        Assert.False(vm.CanMoveToGroup);
    }

    [Fact]
    public void MoveGroupIntoOwnDescendant_NotAllowed()
    {
        var vm = CreateViewModel();
        vm.AddGroupCommand.Execute(new UtilitiesGroupEditParams("Внешняя", null));
        var outer = vm.SelectedItem!;
        vm.AddGroupCommand.Execute(new UtilitiesGroupEditParams("Внутренняя", null));
        var inner = vm.SelectedItem!;

        // Внешнюю нельзя вложить во внутреннюю (свою подгруппу).
        vm.SelectedItem = outer;
        vm.SelectedGroup = inner;

        Assert.False(vm.CanMoveToGroup);
    }

    // ---- Эффективные иконки (issue #374) ----

    [Fact]
    public void EffectiveIconKey_ForItem_ReturnsCatalogIcon()
    {
        var vm = CreateViewModel();
        var item = vm.Roots.FirstOrDefault(n => string.Equals(n.Node.Kind, "item", StringComparison.Ordinal));
        if (item is null)
            return;
        var catalog = Configuration_Management.Services.UtilitiesMenuCatalog.Find(item.Node.ItemId);

        Assert.Equal(catalog?.WpfIconKind ?? string.Empty, item.EffectiveIconKey);
        Assert.Equal(catalog?.IconColor ?? string.Empty, item.EffectiveIconColor);
    }

    [Fact]
    public void EffectiveIconKey_ForBuiltInGroup_ReturnsCatalogIcon()
    {
        var vm = CreateViewModel();
        var group = vm.Groups.FirstOrDefault(g => !string.IsNullOrWhiteSpace(g.Node.GroupId));
        if (group is null)
            return;

        Assert.Equal(
            Configuration_Management.Services.UtilitiesMenuCatalog.GroupWpfIconKind(group.Node.GroupId!),
            group.EffectiveIconKey);
    }

    [Fact]
    public void EffectiveIconKey_CustomIconOverridesCatalog()
    {
        var vm = CreateViewModel();
        var icon = vm.CatalogIcons.FirstOrDefault();
        if (icon is null)
            return;
        var item = vm.Roots.FirstOrDefault(n => string.Equals(n.Node.Kind, "item", StringComparison.Ordinal));
        if (item is null)
            return;
        item.Node.IconKey = icon.Key;
        item.Node.IconColor = icon.Color;

        Assert.Equal(icon.Key, item.EffectiveIconKey);
        Assert.Equal(icon.Color, item.EffectiveIconColor);
    }
}