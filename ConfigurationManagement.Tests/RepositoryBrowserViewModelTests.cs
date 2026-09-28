using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Configuration_Management.Models;
using Configuration_Management.Services;
using Configuration_Management.ViewModels;
using Xunit;

namespace ConfigurationManagement.Tests;

/// <summary>
/// Тесты ViewModel «Обозревателя хранилища конфигурации» (0.3.9.128, этап 2 цикла
/// 0.3.9.127–0.3.9.130): дефолты (пустые коллекции, адрес/пользователь из свойств базы),
/// подключение через fake-сервис (GetHistoryAsync с корректными параметрами и эффективным
/// паролем), выбор версии → LoadVersionObjectsAsync, ошибки в статус-строку (окно не
/// роняем), пароль НЕ попадает в настройки (новых полей в AppSettings нет) и
/// CanExecute-предикат команды меню. Образец — ServerMonitorViewModelTests.
/// </summary>
public sealed class RepositoryBrowserViewModelTests
{
    private static Infobase CreateBase() => new()
    {
        Id = "test-id",
        Name = "Бухгалтерия",
        PlatformVersion = "8.3.27.1644",
        Repository = new RepositorySettings
        {
            Server = "tcp://repo-srv",
            RepositoryName = "main",
            User = "repoUser",
            Password = "repoPwd"
        }
    };

    private static RepositoryVersion Version(int number, string comment) =>
        new(number, new DateTime(2026, 9, 28, 10, 0, 0).AddMinutes(number), "Иванов", comment, IsCurrent: false);

    // ===================== Дефолты =====================

    [Fact]
    public void Defaults_EmptyCollections_AddressAndUserFromBase_CommandsExist()
    {
        var vm = new RepositoryBrowserViewModel(CreateBase(), new FakeRepositoryStorageService(), new RecordingDialogs());

        Assert.Equal("tcp://repo-srv\\main", vm.RepositoryAddress);
        Assert.Equal("repoUser", vm.UserName);
        Assert.Equal("repoPwd", vm.Password);
        Assert.Empty(vm.Versions);
        Assert.Empty(vm.Objects);
        Assert.Null(vm.SelectedVersion);
        Assert.False(vm.HasConnected);
        Assert.False(vm.IsBusy);
        Assert.False(vm.IsObjectsLoading);
        Assert.False(vm.IsHistoryLimited);
        Assert.NotNull(vm.ConnectCommand);
        Assert.NotNull(vm.RefreshCommand);
        Assert.NotNull(vm.SelectVersionCommand);
    }

    // ===================== Подключение =====================

    [Fact]
    public async Task ConnectAsync_CallsGetHistoryAsync_AppliesVersions_AndConnected()
    {
        var ib = CreateBase();
        var fake = new FakeRepositoryStorageService
        {
            HistoryResult = new[] { Version(1, "Первая версия"), Version(2, "Вторая версия") }
        };
        var vm = new RepositoryBrowserViewModel(ib, fake, new RecordingDialogs());

        await vm.ConnectAsync();

        Assert.True(vm.HasConnected);
        Assert.False(vm.IsBusy);
        Assert.Empty(vm.ErrorMessage);
        Assert.NotEmpty(vm.StatusText);

        // GetHistoryAsync вызван с корректными параметрами: без диапазона и с оригинальной базой,
        // пока пароль не менялся (эффективный пароль == пароль базы).
        Assert.Single(fake.HistoryCalls);
        Assert.Null(fake.HistoryCalls[0].nBegin);
        Assert.Null(fake.HistoryCalls[0].nEnd);
        Assert.Same(ib, fake.HistoryCalls[0].infobase);

        Assert.Equal(2, vm.Versions.Count);
        Assert.Equal(1, vm.Versions[0].Number);
        Assert.Equal("Вторая версия", vm.Versions[1].Comment);
        Assert.False(vm.IsHistoryLimited);
    }

    [Fact]
    public async Task ConnectAsync_LimitedHistory_MarksHistoryLimited()
    {
        var fake = new FakeRepositoryStorageService
        {
            HistoryResult = new[] { new RepositoryVersion(-1, DateTime.MinValue, string.Empty, "актуальная версия", IsCurrent: true) }
        };
        var vm = new RepositoryBrowserViewModel(CreateBase(), fake, new RecordingDialogs());

        await vm.ConnectAsync();

        Assert.True(vm.HasConnected);
        Assert.True(vm.IsHistoryLimited);
        Assert.Single(vm.Versions);
        Assert.Equal(-1, vm.Versions[0].Number);
    }

    [Fact]
    public async Task ConnectAsync_OnServiceFailure_ReportsError_WithoutConnected()
    {
        var fake = new FakeRepositoryStorageService
        {
            HistoryException = new RepositoryStorageException("Неверный пароль пользователя хранилища")
        };
        var vm = new RepositoryBrowserViewModel(CreateBase(), fake, new RecordingDialogs());

        await vm.ConnectAsync();

        Assert.False(vm.HasConnected);
        Assert.False(vm.IsBusy);
        Assert.Empty(vm.Versions);
        Assert.NotEmpty(vm.ErrorMessage);
        Assert.Contains("Неверный пароль", vm.ErrorMessage);
        Assert.NotEmpty(vm.StatusText);
    }

    // ===================== Выбор версии → состав =====================

    [Fact]
    public async Task SelectVersion_LoadsObjects_WithCorrectVersionNumber()
    {
        var fake = new FakeRepositoryStorageService
        {
            HistoryResult = new[] { Version(1, "Первая"), Version(2, "Вторая") },
            ObjectsResult = new[]
            {
                new RepositoryObjectInfo("Document", "Документ1", string.Empty, IsTopLevel: true),
                new RepositoryObjectInfo("Forms", "ФормаДокумента", "Документ1", IsTopLevel: false)
            }
        };
        var vm = new RepositoryBrowserViewModel(CreateBase(), fake, new RecordingDialogs());
        await vm.ConnectAsync();

        vm.SelectedVersion = vm.Versions[1];

        // Выбор версии → SelectVersionCommand → LoadVersionObjectsAsync с номером версии.
        Assert.Single(fake.LoadObjectsCalls);
        Assert.Equal(2, fake.LoadObjectsCalls[0].version);
        Assert.Equal("8.3.27.1644", fake.LoadObjectsCalls[0].platformVersion);

        Assert.Equal(2, vm.Objects.Count);
        Assert.Equal("Документ1", vm.Objects[0].Name);
        Assert.True(vm.Objects[0].IsTopLevel);
        Assert.False(vm.Objects[1].IsTopLevel);
        Assert.Equal("Документ1", vm.Objects[1].Owner);
        Assert.Contains("↳ ", vm.Objects[1].DisplayName);
        Assert.False(vm.IsObjectsLoading);
    }

    [Fact]
    public async Task SelectVersion_CurrentVersionRecord_LoadsObjects_WithoutVersionNumber()
    {
        var fake = new FakeRepositoryStorageService
        {
            HistoryResult = new[] { new RepositoryVersion(-1, DateTime.MinValue, string.Empty, "актуальная версия", IsCurrent: true) },
            ObjectsResult = new[] { new RepositoryObjectInfo("Catalog", "Справочник1", string.Empty, IsTopLevel: true) }
        };
        var vm = new RepositoryBrowserViewModel(CreateBase(), fake, new RecordingDialogs());
        await vm.ConnectAsync();

        Assert.True(vm.IsHistoryLimited);
        vm.SelectedVersion = vm.Versions[0];

        // Служебная запись «актуальная версия» выгружается без номера (DumpCfg без -v).
        Assert.Single(fake.LoadObjectsCalls);
        Assert.Null(fake.LoadObjectsCalls[0].version);
        Assert.Single(vm.Objects);
        Assert.Equal("Справочник1", vm.Objects[0].Name);
        Assert.NotEmpty(vm.Versions[0].NumberText);
    }

    [Fact]
    public async Task LoadObjectsAsync_OnFailure_ReportsError_WithoutCrash()
    {
        var fake = new FakeRepositoryStorageService
        {
            HistoryResult = new[] { Version(1, "Первая") },
            ObjectsException = new RepositoryStorageException("Не удалось распаковать версию")
        };
        var vm = new RepositoryBrowserViewModel(CreateBase(), fake, new RecordingDialogs());
        await vm.ConnectAsync();

        await vm.LoadObjectsAsync(vm.Versions[0]);

        // Ошибка — в статус-строку/ErrorMessage, окно не падает, состав пуст.
        Assert.Empty(vm.Objects);
        Assert.NotEmpty(vm.ErrorMessage);
        Assert.Contains("Не удалось распаковать", vm.ErrorMessage);
        Assert.NotEmpty(vm.StatusText);
        Assert.False(vm.IsObjectsLoading);
    }

    [Fact]
    public async Task RefreshAsync_WithoutConnection_DoesNothing()
    {
        var fake = new FakeRepositoryStorageService();
        var vm = new RepositoryBrowserViewModel(CreateBase(), fake, new RecordingDialogs());

        await vm.RefreshAsync();

        Assert.Empty(fake.HistoryCalls);
        Assert.Empty(vm.Versions);
        Assert.False(vm.IsBusy);
    }

    // ===================== Пароль в памяти =====================

    [Fact]
    public async Task Password_Override_DoesNotModifyBase_ButServiceGetsEffectivePassword()
    {
        var ib = CreateBase();
        var fake = new FakeRepositoryStorageService { HistoryResult = new[] { Version(1, "Первая") } };
        var vm = new RepositoryBrowserViewModel(ib, fake, new RecordingDialogs());

        vm.Password = "newPass";
        await vm.ConnectAsync();

        // Оригинальная база не модифицируется (настройки на диск не пишутся)…
        Assert.Equal("repoPwd", ib.Repository.Password);

        // …но сервис получает базу с эффективным паролем (остальные поля хранилища сохранены).
        var effective = fake.HistoryCalls[0].infobase;
        Assert.NotSame(ib, effective);
        Assert.Equal("newPass", effective.Repository.Password);
        Assert.Equal("tcp://repo-srv", effective.Repository.Server);
        Assert.Equal("main", effective.Repository.RepositoryName);
        Assert.Equal("repoUser", effective.Repository.User);
    }

    [Fact]
    public void AppSettings_HasNoNewRepositoryPasswordFields()
    {
        // Пароль хранилища живёт только в памяти окна: в AppSettings НЕ добавляется новых полей.
        Assert.Null(typeof(AppSettings).GetProperty("RepositoryPassword"));
        Assert.Null(typeof(AppSettings).GetProperty("RepositoryBrowserPassword"));
        Assert.Null(typeof(AppSettings).GetProperty("RepositoryBrowserUser"));
    }

    // ===================== Команда меню =====================

    [Fact]
    public void CanOpenRepositoryBrowser_Predicate_OnlyForBaseWithRepository()
    {
        Assert.False(MainViewModel.CanOpenRepositoryBrowser(null));

        var noRepo = new Infobase { Repository = new RepositorySettings() };
        Assert.False(MainViewModel.CanOpenRepositoryBrowser(noRepo));

        var serverOnly = new Infobase { Repository = new RepositorySettings { Server = "tcp://srv" } };
        Assert.True(MainViewModel.CanOpenRepositoryBrowser(serverOnly));

        var full = new Infobase { Repository = new RepositorySettings { Server = "tcp://srv", RepositoryName = "main" } };
        Assert.True(MainViewModel.CanOpenRepositoryBrowser(full));
    }

    // ===================== Fakes =====================

    /// <summary>Fake-сервис хранилища: фиксирует вызовы, возвращает заданные результаты.</summary>
    private sealed class FakeRepositoryStorageService : IRepositoryStorageService
    {
        public List<(Infobase infobase, int? nBegin, int? nEnd)> HistoryCalls { get; } = new();
        public List<(Infobase infobase, int? version, string platformVersion)> LoadObjectsCalls { get; } = new();

        public IReadOnlyList<RepositoryVersion>? HistoryResult { get; set; }
        public IReadOnlyList<RepositoryObjectInfo>? ObjectsResult { get; set; }
        public Exception? HistoryException { get; set; }
        public Exception? ObjectsException { get; set; }

        public Task<IReadOnlyList<RepositoryVersion>> GetHistoryAsync(Infobase infobase, int? nBegin = null, int? nEnd = null,
            IProgress<string>? progress = null, CancellationToken cancellationToken = default)
        {
            HistoryCalls.Add((infobase, nBegin, nEnd));
            if (HistoryException is not null)
                throw HistoryException;
            return Task.FromResult(HistoryResult ?? Array.Empty<RepositoryVersion>());
        }

        public Task<IReadOnlyList<RepositoryObjectInfo>> LoadVersionObjectsAsync(Infobase infobase, int? version,
            string platformVersion, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
        {
            LoadObjectsCalls.Add((infobase, version, platformVersion));
            if (ObjectsException is not null)
                throw ObjectsException;
            return Task.FromResult(ObjectsResult ?? Array.Empty<RepositoryObjectInfo>());
        }

        public Task<string> DumpVersionToCfAsync(Infobase infobase, int? version, string cfPath,
            IProgress<string>? progress = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Этап 3");

        public Task LockAsync(Infobase infobase, string? objectsXmlPath = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Этап 4");

        public Task UnlockAsync(Infobase infobase, string? objectsXmlPath = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Этап 4");
    }

    /// <summary>Запись диалогов для тестов: ничего не показывает, подтверждение — настраиваемое.</summary>
    private sealed class RecordingDialogs : IDialogService
    {
        public bool ConfirmResult { get; set; } = true;

        public List<(string message, string title)> Confirms { get; } = new();
        public List<(string message, string title)> Warnings { get; } = new();

        public void ShowInfo(string message, string title = "") { }
        public void ShowError(string message, string title = "") { }
        public void ShowWarning(string message, string title = "") => Warnings.Add((message, title));
        public bool Confirm(string message, string title = "")
        {
            Confirms.Add((message, title));
            return ConfirmResult;
        }
        public string? OpenFileDialog(string title = "", string filter = "", string? initialDirectory = null) => null;
        public string? SaveFileDialog(string title = "", string defaultFileName = "", string filter = "", string? initialDirectory = null) => null;
        public string? OpenFolderDialog(string title = "", string? initialDirectory = null) => null;
    }
}