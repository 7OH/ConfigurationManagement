using System.Linq;
using Configuration_Management.Localization;
using Configuration_Management.Models;
using Configuration_Management.Services;
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

    // ==================== Группа «Привязка» (issue #346) ====================

    private static OneCConfigType LinkConfig(string code = "BP", string name = "Бухгалтерия предприятия",
        string? nick = "Accounting30") => new()
    {
        Code = code,
        Name = name,
        Nick = nick ?? string.Empty,
        Editions = { new OneCConfigEdition { Name = "Редакция 3.1", Red = "3.1" } },
    };

    [Fact]
    public void LoadFrom_TransfersLinkFields()
    {
        var ib = new Infobase
        {
            UpdateConfigCode = "ZUP",
            UpdateUrlOverride = "https://manual.example/",
            UpdateUrlSegment = "ZUP31",
        };
        var vm = new ConnectionSettingsViewModel();

        vm.LoadFrom(ib);

        Assert.Equal("ZUP", vm.UpdateConfigCode);
        Assert.Equal("https://manual.example/", vm.UpdateUrlOverride);
        Assert.Equal("ZUP31", vm.UpdateUrlSegment);
    }

    [Fact]
    public void ApplyTo_WritesLinkFields()
    {
        var vm = new ConnectionSettingsViewModel
        {
            UpdateConfigCode = "BP",
            UpdateUrlOverride = "https://override.example/",
            UpdateUrlSegment = "seg",
        };
        var ib = new Infobase { Connection = new ConnectionSettings() };

        vm.ApplyTo(ib);

        Assert.Equal("BP", ib.UpdateConfigCode);
        Assert.Equal("https://override.example/", ib.UpdateUrlOverride);
        Assert.Equal("seg", ib.UpdateUrlSegment);
    }

    [Fact]
    public void ApplyTo_EmptyLinkFields_ResetInfobase()
    {
        // Сброс связи (кнопка «Очистить»): пустые строки из ViewModel перетирают значения базы,
        // чтобы для новой базы (ещё не в репозитории) сброс сохранялся штатно через ApplyTo.
        var vm = new ConnectionSettingsViewModel();
        var ib = new Infobase
        {
            UpdateConfigCode = "BP",
            UpdateUrlOverride = "https://old.example/",
            UpdateUrlSegment = "old-seg",
            Connection = new ConnectionSettings(),
        };

        vm.ApplyTo(ib);

        Assert.Equal(string.Empty, ib.UpdateConfigCode);
        Assert.Equal(string.Empty, ib.UpdateUrlOverride);
        Assert.Equal(string.Empty, ib.UpdateUrlSegment);
    }

    [Fact]
    public void RefreshLinkState_NoCode_ReturnsNotSetAndNoLink()
    {
        var vm = new ConnectionSettingsViewModel();
        vm.SetConfigTypes(new[] { LinkConfig() });

        vm.RefreshLinkState();

        Assert.False(vm.HasLink);
        Assert.Equal("—", vm.LinkDisplay);
        Assert.Equal(string.Empty, vm.LinkUrl);
    }

    [Fact]
    public void BuildLinkSummary_KnownConfigAndEdition_Formats()
    {
        var config = LinkConfig();
        var edition = ConfigTypeMatcher.FindEditionByVersion(config, "3.1.142.32");
        Assert.NotNull(edition);

        var summary = ConnectionSettingsViewModel.BuildLinkSummary(
            config, edition, "https://releases.1c.ru/project/Accounting30");

        Assert.Equal(
            "Бухгалтерия предприятия · " +
            string.Format(LocalizationManager.T("Conn.LinkEditionFormat"), "3.1") +
            " · https://releases.1c.ru/project/Accounting30",
            summary);
    }

    [Fact]
    public void BuildLinkSummary_ConfigWithoutNick_OmitsUrl()
    {
        // Конфигурация без ника и без ручной ссылки — URL пуст, остаётся только имя.
        var config = LinkConfig(nick: null);

        var summary = ConnectionSettingsViewModel.BuildLinkSummary(config, null, string.Empty);

        Assert.Equal("Бухгалтерия предприятия", summary);
    }

    [Fact]
    public void BuildLinkSummary_UrlOverride_PrefersOverride()
    {
        var config = LinkConfig();
        var edition = ConfigTypeMatcher.FindEditionByVersion(config, "3.1");

        var summary = ConnectionSettingsViewModel.BuildLinkSummary(
            config, edition, "https://my.example/custom", isManualUrl: true);

        Assert.Contains("https://my.example/custom", summary);
        Assert.Contains(LocalizationManager.T("Conn.LinkManualUrlMark"), summary);
        Assert.DoesNotContain("releases.1c.ru", summary);
    }

    [Fact]
    public void RefreshLinkState_AfterCodeChange_UpdatesHasLink()
    {
        var config = LinkConfig();
        var vm = new ConnectionSettingsViewModel();
        vm.SetConfigTypes(new[] { config });
        vm.SetUrlBuilder((c, e, o) => "https://updates/" + c!.Code);
        vm.RefreshLinkState();
        Assert.False(vm.HasLink);
        Assert.Equal("—", vm.LinkDisplay);

        vm.UpdateConfigCode = "BP";
        Assert.True(vm.HasLink); // сеттер кода сразу оповещает о производном признаке

        vm.RefreshLinkState();

        Assert.True(vm.HasLink);
        Assert.Equal("https://updates/BP", vm.LinkUrl);
        Assert.Contains("Бухгалтерия предприятия", vm.LinkDisplay);
        Assert.Contains("https://updates/BP", vm.LinkDisplay);
    }

    [Fact]
    public void ClearLink_ResetsFieldsAndRefreshesState()
    {
        var vm = new ConnectionSettingsViewModel
        {
            UpdateConfigCode = "BP",
            UpdateUrlOverride = "https://x.example/",
            UpdateUrlSegment = "seg",
        };

        vm.ClearLink();

        Assert.False(vm.HasLink);
        Assert.Equal(string.Empty, vm.UpdateConfigCode);
        Assert.Equal(string.Empty, vm.UpdateUrlOverride);
        Assert.Equal(string.Empty, vm.UpdateUrlSegment);
        Assert.Equal("—", vm.LinkDisplay);
    }

    // ==================== Разбор единого адреса хранилища (issues #140/#348) ====================

    [Fact]
    public void SplitRepositoryConnectionString_TcpAddress_KeepsSchemeInServer()
    {
        // Кнопка «Вставить» (вкладка «Хранилище»): адрес из 1С «tcp://dev:555/base» разбирается
        // так, что сервер сохраняет протокол — как в подсказке поля и модели Server (issue #348).
        var vm = new ConnectionSettingsViewModel();

        vm.SplitRepositoryConnectionString("tcp://dev:555/base");

        Assert.Equal("tcp://dev:555", vm.RepositoryServer);
        Assert.Equal("base", vm.RepositoryName);
    }

    [Fact]
    public void SplitRepositoryConnectionString_FileAddress_KeepsSchemeInServer()
    {
        // Файловое хранилище: схема «file://» и путь остаются в адресе сервера,
        // имя хранилища — последний сегмент (как при импорте из стартера, issue #163).
        var vm = new ConnectionSettingsViewModel();

        vm.SplitRepositoryConnectionString("file:///path/store");

        Assert.Equal("file:///path", vm.RepositoryServer);
        Assert.Equal("store", vm.RepositoryName);
    }

    [Fact]
    public void SplitRepositoryConnectionString_HttpScheme_KeepsScheme()
    {
        var vm = new ConnectionSettingsViewModel();

        vm.SplitRepositoryConnectionString("https://repo.example.ru/storage");

        Assert.Equal("https://repo.example.ru", vm.RepositoryServer);
        Assert.Equal("storage", vm.RepositoryName);
    }

    [Fact]
    public void SplitRepositoryConnectionString_WithoutScheme_Regression()
    {
        // Без схемы — прежнее поведение разбора (issue #140): сервер без протокола, имя отдельно.
        var vm = new ConnectionSettingsViewModel();

        vm.SplitRepositoryConnectionString("dev:555/base");

        Assert.Equal("dev:555", vm.RepositoryServer);
        Assert.Equal("base", vm.RepositoryName);
    }

    [Fact]
    public void SplitRepositoryConnectionString_NoName_SchemeStaysInServer()
    {
        var vm = new ConnectionSettingsViewModel();

        vm.SplitRepositoryConnectionString("tcp://dev:555");

        Assert.Equal("tcp://dev:555", vm.RepositoryServer);
        Assert.Equal(string.Empty, vm.RepositoryName);
    }

    [Fact]
    public void SplitRepositoryConnectionString_TrailingSlash_DropsEmptyName()
    {
        var vm = new ConnectionSettingsViewModel();

        vm.SplitRepositoryConnectionString("tcp://dev:555/");

        Assert.Equal("tcp://dev:555", vm.RepositoryServer);
        Assert.Equal(string.Empty, vm.RepositoryName);
    }

    [Fact]
    public void SplitThenApplyTo_OneCLauncherPath_NoDoubleScheme()
    {
        // Сквозная проверка (issue #348): вставка адреса → Server со схемой → сборка пути
        // для OneCLauncher даёт ровно один «tcp://» (без «tcp://tcp://»).
        var vm = new ConnectionSettingsViewModel();
        vm.SplitRepositoryConnectionString("tcp://dev:555/base");
        var ib = new Infobase { Connection = new ConnectionSettings() };

        vm.ApplyTo(ib);

        var arg = OneCLauncher.BuildRepositoryDumpCfgArgument(ib, @"C:\out\v.cf", null);
        Assert.Contains(" /ConfigurationRepositoryF \"tcp://dev:555/base\"", arg);
        Assert.DoesNotContain("tcp://tcp://", arg);
    }
}