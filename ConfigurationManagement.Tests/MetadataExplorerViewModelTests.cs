using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Configuration_Management.Models;
using Configuration_Management.Services;
using Configuration_Management.ViewModels;
using Xunit;

namespace ConfigurationManagement.Tests;

/// <summary>
/// Тесты ViewModel окна «Обозреватель метаданных» (0.3.9.133, этап 2): дефолты,
/// выбор источника и валидация, построение корня дерева (подсистемы + «Без подсистемы»,
/// пустая конфигурация — без дерева), ленивое раскрытие узлов (EnsureLoaded один раз),
/// детали выбранного объекта, Dispose удаляет временный каталог, ошибки сервиса —
/// статус-строка без исключений. Fake <see cref="IMetadataExplorerService"/> + временная
/// выгрузка на лету (образец ConfigurationDiffTests / RepositoryBrowserViewModelTests).
/// </summary>
public sealed class MetadataExplorerViewModelTests : IDisposable
{
    private readonly string _tempRoot;

    public MetadataExplorerViewModelTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "cm_metavm_tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempRoot, recursive: true); } catch { /* ignore */ }
    }

    private static Infobase CreateBase(string name = "Бухгалтерия") => new()
    {
        Id = "test-id",
        Name = name,
        PlatformVersion = "8.3.27.1644"
    };

    /// <summary>Шапка XML-файла объекта выгрузки (namespace из реальной выгрузки 8.3.27).</summary>
    private const string Ns =
        "<MetaDataObject xmlns=\"http://v8.1c.ru/8.3/MDClasses\" " +
        "xmlns:app=\"http://v8.1c.ru/8.2/managed-application/core\" " +
        "xmlns:v8=\"http://v8.1c.ru/8.1/data/core\" " +
        "xmlns:xr=\"http://v8.1c.ru/8.3/xcf/readable\" version=\"2.20\">";

    // ===================== Дефолты =====================

    [Fact]
    public void Defaults_EmptyCollections_TitleEmpty_CommandsExist()
    {
        var vm = new MetadataExplorerViewModel(
            new[] { CreateBase() },
            CreateBase(),
            new FakeMetadataExplorerService(),
            new RecordingDialogs());

        Assert.Single(vm.Bases);
        Assert.NotNull(vm.SelectedBase);
        Assert.Equal("Бухгалтерия", vm.SelectedBase.Name);
        Assert.True(vm.IsSourceBase);
        Assert.False(vm.IsSourceCf);
        Assert.Empty(vm.TreeNodes);
        Assert.Equal(string.Empty, vm.ConfigurationTitle);
        Assert.Equal(string.Empty, vm.StatusText);
        Assert.Equal(string.Empty, vm.ErrorMessage);
        Assert.False(vm.IsBusy);
        Assert.False(vm.HasDetails);
        Assert.NotNull(vm.LoadCommand);
        Assert.NotNull(vm.BrowseCfCommand);
    }

    [Fact]
    public void Constructor_SelectedBaseNotInList_FallsBackToFirst()
    {
        var vm = new MetadataExplorerViewModel(
            new[] { CreateBase("Первая"), CreateBase("Вторая") },
            CreateBase("Отсутствующая"),
            new FakeMetadataExplorerService(),
            new RecordingDialogs());

        Assert.Equal("Первая", vm.SelectedBase!.Name);
    }

    // ===================== Источник и валидация =====================

    [Fact]
    public void SourceMode_SwitchingToCf_KeepsBaseForSelection()
    {
        var vm = new MetadataExplorerViewModel(
            new[] { CreateBase() }, CreateBase(),
            new FakeMetadataExplorerService(), new RecordingDialogs());

        vm.IsSourceCf = true;
        Assert.Equal(MetadataExplorerViewModel.SourceMode.Cf, vm.Mode);
        Assert.True(vm.IsSourceCf);

        vm.IsSourceBase = true;
        Assert.Equal(MetadataExplorerViewModel.SourceMode.Base, vm.Mode);
    }

    [Fact]
    public void BrowseCf_OpenFileDialogSetsPath()
    {
        var dialogs = new RecordingDialogs { OpenFileResult = @"C:\tmp\etap.cf" };
        var vm = new MetadataExplorerViewModel(
            new[] { CreateBase() }, CreateBase(),
            new FakeMetadataExplorerService(), dialogs);

        vm.BrowseCf();

        Assert.Equal(@"C:\tmp\etap.cf", vm.CfPath);
    }

    [Fact]
    public async Task Load_EmptyCfPath_ValidationError_ServiceNotCalled()
    {
        var fake = new FakeMetadataExplorerService();
        var vm = new MetadataExplorerViewModel(
            new[] { CreateBase() }, CreateBase(), fake, new RecordingDialogs());
        vm.IsSourceCf = true;
        vm.CfPath = "   ";

        await vm.LoadAsync();

        Assert.NotEmpty(vm.ErrorMessage);
        Assert.NotEmpty(vm.StatusText);
        Assert.False(vm.IsBusy);
        Assert.Equal(0, fake.CfCalls);
    }

    [Fact]
    public async Task Load_NonExistingCfFile_ValidationError()
    {
        var fake = new FakeMetadataExplorerService();
        var vm = new MetadataExplorerViewModel(
            new[] { CreateBase() }, CreateBase(), fake, new RecordingDialogs());
        vm.IsSourceCf = true;
        vm.CfPath = Path.Combine(_tempRoot, "нет_такого.cf");

        await vm.LoadAsync();

        Assert.NotEmpty(vm.ErrorMessage);
        Assert.Equal(0, fake.CfCalls);
    }

    // ===================== Загрузка: корень дерева =====================

    [Fact]
    public async Task Load_FromBase_BuildsRoot_SubsystemsAndNoSubsystemNodes()
    {
        var dumpDir = CreateDumpWithSubsystems();
        var fake = new FakeMetadataExplorerService
        {
            BaseResult = new MetadataDump(dumpDir, "Разведка", "1.2.3.4")
        };
        var vm = new MetadataExplorerViewModel(
            new[] { CreateBase() }, CreateBase(), fake, new RecordingDialogs());

        await vm.LoadAsync();

        Assert.False(vm.IsBusy);
        Assert.Equal(string.Empty, vm.ErrorMessage);
        Assert.Equal("Разведка 1.2.3.4", vm.ConfigurationTitle);
        Assert.Equal(1, fake.BaseCalls);

        var root = Assert.Single(vm.TreeNodes);
        Assert.Equal(MetadataTreeNodeKind.Root, root.Kind);
        // Имя корня строится через Loc — в тестах локализация отдаёт ключ as-is,
        // поэтому проверяем только роль узла и шапку (та строится без локализации).
        Assert.NotEmpty(root.DisplayName);

        // Дети корня: «Подсистемы» + «Без подсистемы».
        Assert.Equal(2, root.Children.Count);
        Assert.Equal(MetadataTreeNodeKind.Subsystem, root.Children[0].Kind);
        Assert.Equal(MetadataTreeNodeKind.NoSubsystemGroup, root.Children[1].Kind);

        // «Без подсистемы» не загружено до раскрытия (заглушка-раскрывалка).
        Assert.False(root.Children[1].IsLoaded);
        Assert.Single(root.Children[1].Children); // placeholder
    }

    [Fact]
    public async Task Load_NoSubsystems_AllObjectsNode()
    {
        var dumpDir = CreateDumpWithoutSubsystems();
        var fake = new FakeMetadataExplorerService
        {
            BaseResult = new MetadataDump(dumpDir, "Простая", "2.0.0.1")
        };
        var vm = new MetadataExplorerViewModel(
            new[] { CreateBase() }, CreateBase(), fake, new RecordingDialogs());

        await vm.LoadAsync();

        var root = Assert.Single(vm.TreeNodes);
        var group = Assert.Single(root.Children);
        Assert.Equal(MetadataTreeNodeKind.NoSubsystemGroup, group.Kind);
    }

    [Fact]
    public async Task Load_EmptyConfiguration_NoTree_StatusEmpty()
    {
        var dumpDir = Path.Combine(_tempRoot, "dump_empty_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dumpDir);
        WriteFile(Path.Combine(dumpDir, "Configuration.xml"),
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" + Ns +
            "<Configuration><Properties><Name>Пустая</Name><Synonym/><Comment/>" +
            "<Version>1.0.0.0</Version></Properties><ChildObjects/></Configuration></MetaDataObject>");
        WriteFile(Path.Combine(dumpDir, "Languages", "ru.xml"), "<root/>");

        var fake = new FakeMetadataExplorerService
        {
            BaseResult = new MetadataDump(dumpDir, "Пустая", "1.0.0.0")
        };
        var vm = new MetadataExplorerViewModel(
            new[] { CreateBase() }, CreateBase(), fake, new RecordingDialogs());

        await vm.LoadAsync();

        // Пустая конфигурация: дерево без узлов, статус «конфигурация пуста».
        Assert.Empty(vm.TreeNodes);
        Assert.NotEmpty(vm.StatusText);
        Assert.Equal("Пустая 1.0.0.0", vm.ConfigurationTitle);
    }

    // ===================== Ленивое раскрытие =====================

    [Fact]
    public async Task EnsureLoaded_LoadsChildrenOnce_ForSubsystemAndType()
    {
        var dumpDir = CreateDumpWithSubsystems();
        var fake = new FakeMetadataExplorerService
        {
            BaseResult = new MetadataDump(dumpDir, "Разведка", "1.2.3.4")
        };
        var vm = new MetadataExplorerViewModel(
            new[] { CreateBase() }, CreateBase(), fake, new RecordingDialogs());

        // Строим дерево: ApplyDump применяется синхронно (dispatchToUi = null в тестах).
        await vm.LoadAsync();

        var root = Assert.Single(vm.TreeNodes);
        var subsystemsContainer = root.Children[0]; // «Подсистемы» (контейнер)
        Assert.Equal(MetadataTreeNodeKind.Subsystem, subsystemsContainer.Kind);
        Assert.False(subsystemsContainer.IsLoaded);

        // До раскрытия — только заглушка-раскрывалка.
        Assert.Single(subsystemsContainer.Children);

        subsystemsContainer.EnsureLoaded();

        Assert.True(subsystemsContainer.IsLoaded);
        var subsystemNode = Assert.Single(subsystemsContainer.Children);
        Assert.Equal(MetadataTreeNodeKind.Subsystem, subsystemNode.Kind);
        Assert.False(subsystemNode.IsLoaded);

        // Раскрытие подсистемы: дети — тип из состава Content.
        subsystemNode.EnsureLoaded();
        var typeNode = Assert.Single(subsystemNode.Children);
        Assert.Equal(MetadataTreeNodeKind.Type, typeNode.Kind);
        Assert.NotEmpty(typeNode.DisplayName); // локализация типа через MetadataTypeLocalizer (ключ as-is в тестах)

        // Идемпотентность: повторные вызовы не дублируют детей.
        var countAfterFirst = subsystemNode.Children.Count;
        subsystemNode.EnsureLoaded();
        Assert.Equal(countAfterFirst, subsystemNode.Children.Count);

        // Раскрытие типа подгружает объекты один раз.
        Assert.False(typeNode.IsLoaded);
        typeNode.EnsureLoaded();
        Assert.True(typeNode.IsLoaded);
        Assert.Single(typeNode.Children);
        var objectNode = typeNode.Children[0];
        Assert.Equal(MetadataTreeNodeKind.Object, objectNode.Kind);
        Assert.Equal("Контрагенты", objectNode.ObjectSummary!.Name);

        typeNode.EnsureLoaded();
        Assert.Single(typeNode.Children);
    }

    // ===================== Детали объекта =====================

    [Fact]
    public async Task LoadDetailsAsync_ObjectNode_FillsDetailsPanel()
    {
        var dumpDir = CreateDumpWithoutSubsystems();
        var fake = new FakeMetadataExplorerService
        {
            BaseResult = new MetadataDump(dumpDir, "Простая", "2.0.0.1")
        };
        var vm = new MetadataExplorerViewModel(
            new[] { CreateBase() }, CreateBase(), fake, new RecordingDialogs());
        await vm.LoadAsync();

        var root = Assert.Single(vm.TreeNodes);
        var group = Assert.Single(root.Children);
        group.EnsureLoaded();
        var catalogNode = group.Children.First(c => c.Kind == MetadataTreeNodeKind.Type);
        catalogNode.EnsureLoaded();
        var objectNode = catalogNode.Children.Single(c => !c.IsPlaceholder);

        vm.SelectedNode = objectNode;
        await vm.LoadDetailsAsync(objectNode);

        Assert.True(vm.HasDetails);
        Assert.Equal("Контрагенты", vm.DetailsName);
        Assert.Equal("Контрагенты", vm.DetailsSynonym);
        Assert.Equal("Справочник контрагентов", vm.DetailsComment);
        Assert.Equal("1", vm.DetailsAttributesText);   // Catalogs/Контрагенты/Attributes/ИНН.xml
        Assert.Equal("0", vm.DetailsFormsText);
        Assert.Equal("Простая 2.0.0.1", vm.ConfigurationTitle);
        Assert.Contains("Контрагенты", vm.DetailsRelPath);
        Assert.NotEmpty(vm.DetailsHierarchicalText);   // «Да»/«Нет» (локализовано, OrderedHierarchical=true)
    }

    [Fact]
    public async Task LoadDetailsAsync_NonObjectNode_ClearsPanel()
    {
        var dumpDir = CreateDumpWithoutSubsystems();
        var fake = new FakeMetadataExplorerService
        {
            BaseResult = new MetadataDump(dumpDir, "Простая", "2.0.0.1")
        };
        var vm = new MetadataExplorerViewModel(
            new[] { CreateBase() }, CreateBase(), fake, new RecordingDialogs());
        await vm.LoadAsync();

        vm.SelectedNode = null;

        Assert.False(vm.HasDetails);
        Assert.Equal(string.Empty, vm.DetailsName);
    }

    // ===================== Dispose =====================

    [Fact]
    public async Task Dispose_DeletesTemporaryDumpDirectory()
    {
        var dumpDir = CreateDumpWithoutSubsystems();
        var fake = new FakeMetadataExplorerService
        {
            BaseResult = new MetadataDump(dumpDir, "Простая", "2.0.0.1")
        };
        var vm = new MetadataExplorerViewModel(
            new[] { CreateBase() }, CreateBase(), fake, new RecordingDialogs());
        await vm.LoadAsync();

        Assert.True(Directory.Exists(dumpDir));

        vm.Dispose();

        Assert.False(Directory.Exists(dumpDir));
    }

    // ===================== Ошибки сервиса =====================

    [Fact]
    public async Task Load_ServiceThrows_StatusError_NoCrash()
    {
        var fake = new FakeMetadataExplorerService
        {
            BaseException = new MetadataExplorerException("Ошибка выгрузки: база недоступна")
        };
        var vm = new MetadataExplorerViewModel(
            new[] { CreateBase() }, CreateBase(), fake, new RecordingDialogs());

        await vm.LoadAsync();

        Assert.False(vm.IsBusy);
        Assert.NotEmpty(vm.ErrorMessage);
        Assert.Contains("база недоступна", vm.ErrorMessage);
        Assert.Empty(vm.TreeNodes);
        Assert.Equal(string.Empty, vm.ConfigurationTitle);
    }

    [Fact]
    public async Task Load_FromCf_CallsDumpFromCfWithPath()
    {
        var dumpDir = CreateDumpWithoutSubsystems();
        var cfPath = Path.Combine(_tempRoot, "источник.cf");
        File.WriteAllText(cfPath, "fake cf content");

        var fake = new FakeMetadataExplorerService
        {
            CfResult = new MetadataDump(dumpDir, "ИзCf", "3.0.0.0")
        };
        var vm = new MetadataExplorerViewModel(
            new[] { CreateBase() }, CreateBase(), fake, new RecordingDialogs());
        vm.IsSourceCf = true;
        vm.CfPath = cfPath;

        await vm.LoadAsync();

        Assert.Equal(1, fake.CfCalls);
        Assert.Equal(cfPath, fake.LastCfPath);
        Assert.Equal("ИзCf 3.0.0.0", vm.ConfigurationTitle);
        Assert.Single(vm.TreeNodes);
    }

    // ===================== Фикстуры =====================

    /// <summary>Выгрузка с подсистемами: Subsystems/Основное + Catalogs/Контрагенты + Documents/ЗаказКлиента.</summary>
    private string CreateDumpWithSubsystems()
    {
        var dir = Path.Combine(_tempRoot, "dump_sys_" + Guid.NewGuid().ToString("N"));
        WriteFile(Path.Combine(dir, "Configuration.xml"),
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" + Ns +
            "<Configuration><Properties><Name>Разведка</Name><Synonym/><Comment/>" +
            "<Version>1.2.3.4</Version></Properties><ChildObjects/></Configuration></MetaDataObject>");
        WriteFile(Path.Combine(dir, "Subsystems", "Основное.xml"),
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" + Ns +
            "<Subsystem><Properties><Name>Основное</Name>" +
            "<Synonym><v8:item><v8:content>Основное</v8:content></v8:item></Synonym>" +
            "<Comment/><Content><v8:item><v8:content>Catalog.Контрагенты</v8:content></v8:item></Content>" +
            "</Properties><ChildObjects/></Subsystem></MetaDataObject>");
        WriteFile(Path.Combine(dir, "Catalogs", "Контрагенты.xml"),
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" + Ns +
            "<Catalog><Properties><Name>Контрагенты</Name>" +
            "<Synonym><v8:item><v8:content>Контрагенты</v8:content></v8:item></Synonym>" +
            "<Comment>Справочник контрагентов</Comment><Hierarchical>true</Hierarchical>" +
            "</Properties><ChildObjects/></Catalog></MetaDataObject>");
        WriteFile(Path.Combine(dir, "Catalogs", "Контрагенты", "Attributes", "ИНН.xml"),
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" + Ns +
            "<Attribute><Properties><Name>ИНН</Name></Properties></Attribute></MetaDataObject>");
        WriteFile(Path.Combine(dir, "Documents", "ЗаказКлиента.xml"),
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" + Ns +
            "<Document><Properties><Name>ЗаказКлиента</Name><Synonym/><Comment/></Properties>" +
            "<ChildObjects/></Document></MetaDataObject>");
        return dir;
    }

    /// <summary>Выгрузка без подсистем: Catalogs/Контрагенты + Documents/ЗаказКлиента.</summary>
    private string CreateDumpWithoutSubsystems()
    {
        var dir = Path.Combine(_tempRoot, "dump_obj_" + Guid.NewGuid().ToString("N"));
        WriteFile(Path.Combine(dir, "Configuration.xml"),
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" + Ns +
            "<Configuration><Properties><Name>Простая</Name><Synonym/><Comment/>" +
            "<Version>2.0.0.1</Version></Properties><ChildObjects/></Configuration></MetaDataObject>");
        WriteFile(Path.Combine(dir, "Catalogs", "Контрагенты.xml"),
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" + Ns +
            "<Catalog><Properties><Name>Контрагенты</Name>" +
            "<Synonym><v8:item><v8:content>Контрагенты</v8:content></v8:item></Synonym>" +
            "<Comment>Справочник контрагентов</Comment><OrderedHierarchical>true</OrderedHierarchical>" +
            "</Properties><ChildObjects/></Catalog></MetaDataObject>");
        WriteFile(Path.Combine(dir, "Catalogs", "Контрагенты", "Attributes", "ИНН.xml"),
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" + Ns +
            "<Attribute><Properties><Name>ИНН</Name></Properties></Attribute></MetaDataObject>");
        WriteFile(Path.Combine(dir, "Documents", "ЗаказКлиента.xml"),
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" + Ns +
            "<Document><Properties><Name>ЗаказКлиента</Name><Synonym/><Comment/></Properties>" +
            "<ChildObjects/></Document></MetaDataObject>");
        return dir;
    }

    private static void WriteFile(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    // ===================== Fakes =====================

    /// <summary>Fake-сервис выгрузки: фиксирует вызовы, возвращает заданные результаты.</summary>
    private sealed class FakeMetadataExplorerService : IMetadataExplorerService
    {
        public MetadataDump? BaseResult { get; set; }
        public MetadataDump? CfResult { get; set; }
        public Exception? BaseException { get; set; }
        public Exception? CfException { get; set; }

        public int BaseCalls { get; private set; }
        public int CfCalls { get; private set; }
        public string? LastCfPath { get; private set; }
        public string? LastPlatformVersion { get; private set; }

        public Task<MetadataDump> DumpFromBaseAsync(
            Infobase infobase, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
        {
            BaseCalls++;
            if (BaseException is not null)
                throw BaseException;
            progress?.Report("dump");
            return Task.FromResult(BaseResult!);
        }

        public Task<MetadataDump> DumpFromCfAsync(
            string cfPath, string platformVersion, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
        {
            CfCalls++;
            LastCfPath = cfPath;
            LastPlatformVersion = platformVersion;
            if (CfException is not null)
                throw CfException;
            progress?.Report("dump");
            return Task.FromResult(CfResult!);
        }
    }

    /// <summary>Запись диалогов для тестов: ничего не показывает, выбор файла — настраиваемый.</summary>
    private sealed class RecordingDialogs : IDialogService
    {
        public string? OpenFileResult { get; set; }

        public void ShowInfo(string message, string title = "") { }
        public void ShowWarning(string message, string title = "") { }
        public void ShowError(string message, string title = "") { }
        public bool Confirm(string message, string title = "") => true;
        public string? OpenFileDialog(string title = "", string filter = "", string? initialDirectory = null) => OpenFileResult;
        public string? SaveFileDialog(string title = "", string defaultFileName = "", string filter = "", string? initialDirectory = null) => null;
        public string? OpenFolderDialog(string title = "", string? initialDirectory = null) => null;
    }
}