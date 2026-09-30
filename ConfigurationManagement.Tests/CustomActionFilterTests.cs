using Configuration_Management.Models;
using Configuration_Management.Services;
using Xunit;

namespace ConfigurationManagement.Tests;

/// <summary>
/// Тесты чистой логики отбора пользовательских действий и целей (0.3.9.195, функция 7):
/// какие действия показываются в контексте (одиночная база / мультивыделение / группа)
/// и фильтрация видимых целей с учётом приватности.
/// </summary>
public sealed class CustomActionFilterTests
{
    private static CustomAction Action(string name, CustomActionScope scope, bool supportsBatch = false) => new()
    {
        Name = name,
        Scope = scope,
        SupportsBatch = supportsBatch
    };

    private static Infobase Base(string name, bool isPrivate = false) => new()
    {
        Id = "id-" + name,
        Name = name,
        IsPrivate = isPrivate,
        Connection = new ConnectionSettings { Type = ConnectionType.File, FilePath = @"C:\bases\" + name }
    };

    [Fact]
    public void SelectActions_SingleBase_ReturnsOnlyBaseAndBoth()
    {
        var actions = new[]
        {
            Action("База", CustomActionScope.Base),
            Action("Группа", CustomActionScope.Group),
            Action("Оба", CustomActionScope.Both)
        };

        var result = CustomActionFilter.SelectActions(actions, CustomActionContext.SingleBase);

        Assert.Equal(new[] { "База", "Оба" }, result.Select(a => a.Name));
    }

    [Fact]
    public void SelectActions_Batch_ReturnsOnlySupportsBatchRegardlessOfScope()
    {
        var actions = new[]
        {
            Action("Без пакета", CustomActionScope.Base),
            Action("Пакет-база", CustomActionScope.Base, supportsBatch: true),
            Action("Пакет-группа", CustomActionScope.Group, supportsBatch: true)
        };

        var result = CustomActionFilter.SelectActions(actions, CustomActionContext.Batch);

        Assert.Equal(new[] { "Пакет-база", "Пакет-группа" }, result.Select(a => a.Name));
    }

    [Fact]
    public void SelectActions_Group_ReturnsOnlyGroupAndBoth()
    {
        var actions = new[]
        {
            Action("База", CustomActionScope.Base),
            Action("Группа", CustomActionScope.Group),
            Action("Оба", CustomActionScope.Both)
        };

        var result = CustomActionFilter.SelectActions(actions, CustomActionContext.Group);

        Assert.Equal(new[] { "Группа", "Оба" }, result.Select(a => a.Name));
    }

    [Fact]
    public void SelectActions_EmptyActions_ReturnsEmpty()
    {
        Assert.Empty(CustomActionFilter.SelectActions(new CustomAction[0], CustomActionContext.SingleBase));
        Assert.Empty(CustomActionFilter.SelectActions(new CustomAction[0], CustomActionContext.Batch));
        Assert.Empty(CustomActionFilter.SelectActions(new CustomAction[0], CustomActionContext.Group));
    }

    [Fact]
    public void SelectActions_PreservesOriginalOrder()
    {
        var actions = new[]
        {
            Action("Зета", CustomActionScope.Base),
            Action("Альфа", CustomActionScope.Both),
            Action("Мю", CustomActionScope.Base)
        };

        var result = CustomActionFilter.SelectActions(actions, CustomActionContext.SingleBase);

        Assert.Equal(new[] { "Зета", "Альфа", "Мю" }, result.Select(a => a.Name));
    }

    [Fact]
    public void FilterVisibleTargets_PrivateBase_ExcludedWhenProfileLocked()
    {
        var targets = new[] { Base("Открытая"), Base("Секретная", isPrivate: true) };

        var result = CustomActionFilter.FilterVisibleTargets(targets, canShowPrivateBases: false);

        Assert.Single(result);
        Assert.Equal("Открытая", result[0].Name);
    }

    [Fact]
    public void FilterVisibleTargets_PrivateBase_KeptWhenProfileUnlocked()
    {
        var targets = new[] { Base("Открытая"), Base("Секретная", isPrivate: true) };

        var result = CustomActionFilter.FilterVisibleTargets(targets, canShowPrivateBases: true);

        Assert.Equal(2, result.Count);
        Assert.Equal(new[] { "Открытая", "Секретная" }, result.Select(b => b.Name));
    }

    [Fact]
    public void FilterVisibleTargets_NonPrivateBases_AlwaysKept()
    {
        var targets = new[] { Base("Первая"), Base("Вторая") };

        Assert.Equal(2, CustomActionFilter.FilterVisibleTargets(targets, canShowPrivateBases: false).Count);
        Assert.Equal(2, CustomActionFilter.FilterVisibleTargets(targets, canShowPrivateBases: true).Count);
    }

    [Fact]
    public void FilterVisibleTargets_Empty_ReturnsEmpty()
    {
        Assert.Empty(CustomActionFilter.FilterVisibleTargets(new Infobase[0], canShowPrivateBases: true));
    }
}