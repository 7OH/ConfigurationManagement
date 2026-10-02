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
}