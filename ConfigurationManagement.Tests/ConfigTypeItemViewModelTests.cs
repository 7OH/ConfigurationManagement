using System;
using Configuration_Management.Models;
using Configuration_Management.ViewModels;
using Xunit;

namespace ConfigurationManagement.Tests;

/// <summary>
/// Тесты строки списка типовых конфигураций (issue #321): признаки предопределённой
/// (<see cref="ConfigTypeItemViewModel.IsBuiltIn"/>) и пользовательской копии
/// предопределённой (<see cref="ConfigTypeItemViewModel.IsOverride"/>), доступность
/// команды удаления только у пользовательских записей.
/// </summary>
public sealed class ConfigTypeItemViewModelTests
{
    private static ConfigTypeItemViewModel Create(
        OneCConfigType model,
        Action<ConfigTypeItemViewModel> edit,
        Action<ConfigTypeItemViewModel> delete) =>
        new(model, edit, delete);

    [Fact]
    public void IsOverride_ReflectsModelOverridesBuiltIn()
    {
        // Копия, созданная правкой встроенной строки (IsBuiltIn снят, OverridesBuiltIn=true).
        var vm = Create(new OneCConfigType { Code = "ZUP", Name = "ЗУП (копия)", OverridesBuiltIn = true },
            _ => { }, _ => { });

        Assert.True(vm.IsOverride);
        Assert.False(vm.IsBuiltIn);
    }

    [Fact]
    public void IsBuiltIn_True_ForBuiltInModel()
    {
        var vm = Create(new OneCConfigType { Code = "ZUP", Name = "ЗУП", IsBuiltIn = true }, _ => { }, _ => { });

        Assert.True(vm.IsBuiltIn);
        Assert.False(vm.IsOverride);
    }

    [Fact]
    public void DeleteCommand_Enabled_ForCustom_Disabled_ForBuiltIn()
    {
        var custom = Create(new OneCConfigType { Code = "X", Name = "Моя" }, _ => { }, _ => { });
        var builtIn = Create(new OneCConfigType { Code = "ZUP", IsBuiltIn = true }, _ => { }, _ => { });

        Assert.True(custom.DeleteCommand.CanExecute(null));
        Assert.False(builtIn.DeleteCommand.CanExecute(null));
    }

    [Fact]
    public void Name_UrlCode_EditionsSummary_FromModel()
    {
        var model = new OneCConfigType
        {
            Code = "MY",
            Name = "Моя конфигурация",
            UrlCode = string.Empty, // пустой сегмент → имя
            Editions = { new OneCConfigEdition { Name = "3.0", Red = "3.0" } },
        };
        var vm = Create(model, _ => { }, _ => { });

        Assert.Equal("Моя конфигурация", vm.Name);
        Assert.Equal("Моя конфигурация", vm.UrlCode); // fallback на имя
        Assert.Equal("3.0", vm.EditionsSummary);
    }
}