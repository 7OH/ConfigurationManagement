using System.Linq;
using Configuration_Management.Models;
using Configuration_Management.ViewModels;
using Xunit;

namespace ConfigurationManagement.Tests;

/// <summary>
/// Тесты ViewModel окна свойств базы: наполнение выпадающего списка «Конфигурация»
/// (issue #338) — отсев пустых значений, дедупликация без учёта регистра и сортировка.
/// </summary>
public sealed class ConnectionSettingsViewModelTests
{
    [Fact]
    public void SetAvailableConfigurations_DeduplicatesSortsAndDropsEmpty()
    {
        var vm = new ConnectionSettingsViewModel();
        vm.SetAvailableConfigurations(new[]
        {
            "ЗУП 3.1",
            "  бухгалтерия предприятия  ",
            "",
            "   ",
            "ЗУП 3.1",
            "Бухгалтерия предприятия",
        });

        // Без дублей (регистронезависимо), по алфавиту; пустые и пробельные отброшены.
        // Distinct(OrdinalIgnoreCase) сохраняет первое вхождение, поэтому регистр —
        // как у первого встреченного значения.
        Assert.Equal(new[] { "бухгалтерия предприятия", "ЗУП 3.1" }, vm.AvailableConfigurations.ToArray());
    }

    [Fact]
    public void SetAvailableConfigurations_Null_LeavesListEmpty()
    {
        var vm = new ConnectionSettingsViewModel();
        vm.SetAvailableConfigurations(new[] { "ERP 2.5" });

        vm.SetAvailableConfigurations(null);

        Assert.Empty(vm.AvailableConfigurations);
    }

    [Fact]
    public void SetAvailableConfigurations_OnlyEmpty_LeavesListEmpty()
    {
        var vm = new ConnectionSettingsViewModel();
        vm.SetAvailableConfigurations(new[] { "", "  ", null! });

        Assert.Empty(vm.AvailableConfigurations);
    }

    // ======================= Обрезка сегмента локали при сохранении (issue #332) =======================

    [Fact]
    public void ApplyTo_WebUrlWithLocaleSegment_StripsSegment()
    {
        var vm = new ConnectionSettingsViewModel();
        vm.ConnectionType = ConnectionType.WebServer;
        vm.WebUrl = "https://accounting.demo.1c.ru/accounting/ru_RU/";
        var ib = new Infobase { Connection = new ConnectionSettings() };

        vm.ApplyTo(ib);

        // Сегмент локали убран из сохраняемого адреса (как при «Перейти по ссылке»).
        Assert.Equal("https://accounting.demo.1c.ru/accounting/", ib.Connection!.WebUrl);
    }

    [Fact]
    public void ApplyTo_WebUrlWithoutLocaleSegment_KeepsUrl()
    {
        var vm = new ConnectionSettingsViewModel();
        vm.ConnectionType = ConnectionType.WebServer;
        vm.WebUrl = "https://host/base/";
        var ib = new Infobase { Connection = new ConnectionSettings() };

        vm.ApplyTo(ib);

        Assert.Equal("https://host/base/", ib.Connection!.WebUrl);
    }

    [Fact]
    public void ApplyTo_EmptyWebUrl_StaysEmpty()
    {
        var vm = new ConnectionSettingsViewModel();
        vm.ConnectionType = ConnectionType.File;
        vm.FilePath = @"C:\bases\accounting";
        var ib = new Infobase { Connection = new ConnectionSettings() };

        vm.ApplyTo(ib);

        Assert.Equal(string.Empty, ib.Connection!.WebUrl);
    }

    // ============ Объединение имён из баз и типовых конфигураций (issue #338/#321) ============

    [Fact]
    public void MergeAvailableConfigurations_AddsConfigNamesFromTypes()
    {
        var result = ConnectionSettingsViewModel.MergeAvailableConfigurations(
            new[] { "BP", "ZUP" },
            new[]
            {
                new OneCConfigType { ConfigName = "BP" },          // дубль с базой
                new OneCConfigType { ConfigName = "Retail" },      // только в типовых
                new OneCConfigType { ConfigName = "ERP" },         // только в типовых
                new OneCConfigType { ConfigName = "" },            // пустое — отбросить
            });

        Assert.Equal(new[] { "BP", "ERP", "Retail", "ZUP" }, result.ToArray());
    }

    [Fact]
    public void MergeAvailableConfigurations_TrimsAndDropsEmptyFromBoth()
    {
        var result = ConnectionSettingsViewModel.MergeAvailableConfigurations(
            new[] { "  Retail  ", "", "   ", null! },
            new[]
            {
                new OneCConfigType { ConfigName = "  ERP  " },
                new OneCConfigType { ConfigName = "" },
            });

        Assert.Equal(new[] { "ERP", "Retail" }, result.ToArray());
    }

    [Fact]
    public void MergeAvailableConfigurations_Intersection_DeduplicatedCaseInsensitive()
    {
        // Значение типовой конфигурации повторяет имя из базы без учёта регистра —
        // в списке остаётся одно вхождение (первое, из баз).
        var result = ConnectionSettingsViewModel.MergeAvailableConfigurations(
            new[] { "Бухгалтерия предприятия", "ЗУП 3.1" },
            new[]
            {
                new OneCConfigType { ConfigName = "бухгалтерия предприятия" },
                new OneCConfigType { ConfigName = "ЗУП 3.1" },
            });

        Assert.Equal(new[] { "Бухгалтерия предприятия", "ЗУП 3.1" }, result.ToArray());
    }

    [Fact]
    public void MergeAvailableConfigurations_NullSources_ReturnsEmpty()
    {
        var result = ConnectionSettingsViewModel.MergeAvailableConfigurations(null, null);

        Assert.Empty(result);
    }

    [Fact]
    public void SetAvailableConfigurations_MergedResult_FeedsComboList()
    {
        var merged = ConnectionSettingsViewModel.MergeAvailableConfigurations(
            new[] { "BP" },
            new[] { new OneCConfigType { ConfigName = "ERP" } });
        var vm = new ConnectionSettingsViewModel();

        vm.SetAvailableConfigurations(merged);

        Assert.Equal(new[] { "BP", "ERP" }, vm.AvailableConfigurations.ToArray());
    }
}