using Configuration_Management.Models;
using Configuration_Management.Services;
using Configuration_Management.ViewModels;
using Xunit;

namespace ConfigurationManagement.Tests;

/// <summary>
/// Тесты встроенного монитора серверов 1С — ViewModel (этап 2, 0.3.9.124):
/// дефолты подключения, подключение с fake-клиентом rac (кластеры) и загрузка
/// данных кластера во вкладки. Действия и автообновление — этап 3 (0.3.9.125).
/// </summary>
public sealed class ServerMonitorViewModelTests
{
    [Fact]
    public void Defaults_PortIs1540_AddressLocalhost_CommandsExist()
    {
        var vm = new ServerMonitorViewModel(new FakeRacClient(), new StubDialogs());

        Assert.Equal("localhost", vm.ServerAddress);
        Assert.Equal(IRacClient.DefaultPort, vm.ServerPort);
        Assert.Equal(1540, vm.ServerPort);
        Assert.Equal(string.Empty, vm.UserName);
        Assert.Equal(string.Empty, vm.Password);
        Assert.False(vm.HasConnected);
        Assert.False(vm.IsBusy);
        Assert.NotNull(vm.ConnectCommand);
        Assert.NotNull(vm.RefreshCommand);
        Assert.Empty(vm.Processes);
        Assert.Empty(vm.Sessions);
        Assert.Empty(vm.Connections);
        Assert.Empty(vm.Locks);
        Assert.Empty(vm.Clusters);
        Assert.Empty(vm.ClusterRows);
    }

    [Fact]
    public async Task ConnectAsync_WithClusters_SetsConnected_AndSelectsFirst()
    {
        var vm = new ServerMonitorViewModel(new FakeRacClient(), new StubDialogs());

        await vm.ConnectAsync();

        Assert.True(vm.HasConnected);
        Assert.Equal(2, vm.Clusters.Count);
        Assert.Equal(2, vm.ClusterRows.Count);
        Assert.Equal(FakeRacClient.FirstClusterId, vm.SelectedClusterId);
        Assert.NotEmpty(vm.StatusText);
        Assert.Empty(vm.ErrorMessage);
    }

    [Fact]
    public async Task ConnectAsync_OnClientFailure_ReportsError_WithoutConnected()
    {
        var vm = new ServerMonitorViewModel(new FakeRacClient(throwOnClusters: true), new StubDialogs());

        await vm.ConnectAsync();

        Assert.False(vm.HasConnected);
        Assert.NotEmpty(vm.ErrorMessage);
        Assert.NotEmpty(vm.StatusText);
    }

    [Fact]
    public async Task LoadClusterDataAsync_FillsTabs_AndClusterInfo()
    {
        var vm = new ServerMonitorViewModel(new FakeRacClient(), new StubDialogs());

        await vm.LoadClusterDataAsync(FakeRacClient.FirstClusterId);

        Assert.Single(vm.Processes);
        Assert.Single(vm.Sessions);
        Assert.Single(vm.Connections);
        Assert.Single(vm.Locks);
        Assert.NotNull(vm.ClusterInfo);
        Assert.NotEmpty(vm.ClusterInfoText);
        Assert.Contains("name:", vm.ClusterInfoText);
    }

    [Fact]
    public void Refresh_WithoutConnection_DoesNothing()
    {
        var vm = new ServerMonitorViewModel(new FakeRacClient(), new StubDialogs());
        vm.Refresh();
        Assert.Empty(vm.Processes);
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public void Password_Settable_InMemory()
    {
        var vm = new ServerMonitorViewModel(new FakeRacClient(), new StubDialogs());
        vm.Password = "secret";
        Assert.Equal("secret", vm.Password);
    }

    // ===================== Fakes =====================

    /// <summary>Fake-клиент rac для тестов: два кластера, по одной строке данных.</summary>
    private sealed class FakeRacClient : IRacClient
    {
        public static readonly System.Guid FirstClusterId = System.Guid.Parse("11111111-1111-1111-1111-111111111111");
        private static readonly System.Guid SecondClusterId = System.Guid.Parse("22222222-2222-2222-2222-222222222222");

        private readonly bool _throwOnClusters;

        public FakeRacClient(bool throwOnClusters = false) => _throwOnClusters = throwOnClusters;

        public Task<IReadOnlyList<RacCluster>> GetClustersAsync(
            RacConnectionParams parameters, CancellationToken cancellationToken = default)
        {
            if (_throwOnClusters)
                throw new RacClientException("rac not found");
            return Task.FromResult<IReadOnlyList<RacCluster>>(new[]
            {
                new RacCluster { Id = FirstClusterId, Name = "Главный кластер", Port = 1541 },
                new RacCluster { Id = SecondClusterId, Name = "Второй", Port = 1542 }
            });
        }

        public Task<RacClusterInfo?> GetClusterInfoAsync(
            RacConnectionParams parameters, Guid clusterId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<RacClusterInfo?>(new RacClusterInfo
            {
                Name = "Главный кластер",
                HostName = "srv1",
                Port = 1541,
                Properties = new Dictionary<string, string>
                {
                    ["name"] = "Главный кластер",
                    ["hostName"] = "srv1",
                    ["port"] = "1541"
                }
            });
        }

        public Task<IReadOnlyList<RacProcessInfo>> GetProcessesAsync(
            RacConnectionParams parameters, Guid clusterId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<RacProcessInfo>>(new[]
            {
                new RacProcessInfo
                {
                    Id = System.Guid.NewGuid(),
                    Host = "srv1",
                    Pid = 1234,
                    Port = 1560,
                    StartedAt = new DateTime(2026, 9, 28, 10, 0, 0),
                    MemorySize = 512 * 1024 * 1024,
                    Threads = 8,
                    Cpu = 12.5,
                    Running = true,
                    Infobases = 3
                }
            });
        }

        public Task<IReadOnlyList<RacSessionInfo>> GetSessionsAsync(
            RacConnectionParams parameters, Guid clusterId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<RacSessionInfo>>(new[]
            {
                new RacSessionInfo
                {
                    Id = System.Guid.NewGuid(),
                    User = "Иванов",
                    Host = "client1",
                    AppId = "1CV8",
                    StartedAt = new DateTime(2026, 9, 28, 9, 30, 0),
                    LastActiveAt = new DateTime(2026, 9, 28, 10, 5, 0),
                    State = "active",
                    Memory = 256 * 1024 * 1024,
                    DurationAll = 3600_000
                }
            });
        }

        public Task<IReadOnlyList<RacConnectionInfo>> GetConnectionsAsync(
            RacConnectionParams parameters, Guid clusterId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<RacConnectionInfo>>(new[]
            {
                new RacConnectionInfo
                {
                    Id = System.Guid.NewGuid(),
                    SessionId = System.Guid.NewGuid(),
                    Connector = "1CV8",
                    Host = "client1",
                    Port = 52000,
                    EstablishedAt = new DateTime(2026, 9, 28, 9, 30, 0),
                    Duration = 1800_000
                }
            });
        }

        public Task<IReadOnlyList<RacLockInfo>> GetLocksAsync(
            RacConnectionParams parameters, Guid clusterId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<RacLockInfo>>(new[]
            {
                new RacLockInfo
                {
                    Id = System.Guid.NewGuid(),
                    SessionId = System.Guid.NewGuid(),
                    ConnectionId = System.Guid.NewGuid(),
                    TransactionId = System.Guid.NewGuid(),
                    Waiting = true,
                    Blocking = false,
                    Object = "Справочник.Номенклатура"
                }
            });
        }

        public Task<bool> TerminateSessionAsync(
            RacConnectionParams parameters, Guid clusterId, Guid sessionId,
            CancellationToken cancellationToken = default) =>
            throw new NotImplementedException("Этап 3 (0.3.9.125).");

        public Task<bool> DisconnectConnectionAsync(
            RacConnectionParams parameters, Guid clusterId, Guid connectionId,
            CancellationToken cancellationToken = default) =>
            throw new NotImplementedException("Этап 3 (0.3.9.125).");
    }

    /// <summary>Заглушка диалогов: методы записывают вызовы, ничего не показывая.</summary>
    private sealed class StubDialogs : IDialogService
    {
        public void ShowInfo(string message, string title = "") { }
        public void ShowWarning(string message, string title = "") { }
        public void ShowError(string message, string title = "") { }
        public bool Confirm(string message, string title = "") => true;
        public string? OpenFileDialog(string title = "", string filter = "", string? initialDirectory = null) => null;
        public string? SaveFileDialog(string title = "", string defaultFileName = "", string filter = "", string? initialDirectory = null) => null;
        public string? OpenFolderDialog(string title = "", string? initialDirectory = null) => null;
    }
}