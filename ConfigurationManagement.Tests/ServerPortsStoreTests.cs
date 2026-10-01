using Configuration_Management.Services;
using Xunit;

namespace ConfigurationManagement.Tests;

/// <summary>
/// Тесты хранилища портов серверов 1С в разрезе сервера (issue #335): JSON-файл
/// server_ports.json рядом с настройками; запись «сервер → порт(ы)» для кластера,
/// rac/монитора и хранилища; ключи нормализуются (без пробелов, регистронезависимо);
/// битый файл не роняет загрузку.
/// </summary>
public sealed class ServerPortsStoreTests
{
    [Fact]
    public void Save_ThenLoad_ReturnsSavedPorts()
    {
        using var tmp = TempDir();
        var store = new ServerPortsStore(directoryOverride: tmp.Path);

        store.Save("srv1c", new ServerPortsSettings(1541, 1540, 1542));

        var loaded = store.Load();
        Assert.True(loaded.TryGetValue("srv1c", out var ports));
        Assert.Equal(1541, ports!.Cluster);
        Assert.Equal(1540, ports.Agent);
        Assert.Equal(1542, ports.Repository);
        Assert.True(File.Exists(store.FilePath));
    }

    [Fact]
    public void Save_SameServerTwice_UpdatesEntry()
    {
        using var tmp = TempDir();
        var store = new ServerPortsStore(directoryOverride: tmp.Path);

        store.Save("srv1c", new ServerPortsSettings(1541, 0, 0));
        store.Save("srv1c", new ServerPortsSettings(1599, 0, 1542));

        var loaded = store.Load();
        Assert.Single(loaded);
        Assert.Equal(1599, loaded["srv1c"].Cluster);
        Assert.Equal(1542, loaded["srv1c"].Repository);
    }

    [Fact]
    public void Save_TwoServers_BothStoredIndependently()
    {
        using var tmp = TempDir();
        var store = new ServerPortsStore(directoryOverride: tmp.Path);

        store.Save("srv-a", new ServerPortsSettings(1541, 0, 0));
        store.Save("srv-b", new ServerPortsSettings(1580, 1540, 1542));

        var loaded = store.Load();
        Assert.Equal(2, loaded.Count);
        Assert.Equal(1541, loaded["srv-a"].Cluster);
        Assert.Equal(1580, loaded["srv-b"].Cluster);
    }

    [Fact]
    public void Save_ServerKeyIsCaseInsensitiveAndTrimmed()
    {
        using var tmp = TempDir();
        var store = new ServerPortsStore(directoryOverride: tmp.Path);

        store.Save("  SRV1C ", new ServerPortsSettings(1541, 0, 0));

        var loaded = store.Load();
        Assert.True(loaded.TryGetValue("srv1c", out _));
        Assert.Single(loaded);
    }

    [Fact]
    public void Load_WhenNoFile_ReturnsEmpty()
    {
        using var tmp = TempDir();
        var store = new ServerPortsStore(directoryOverride: tmp.Path);

        Assert.Empty(store.Load());
    }

    [Fact]
    public void Load_CorruptFile_ReturnsEmptyWithoutThrowing()
    {
        using var tmp = TempDir();
        Directory.CreateDirectory(tmp.Path);
        var store = new ServerPortsStore(directoryOverride: tmp.Path);
        File.WriteAllText(store.FilePath, "{ not valid json ");

        Assert.Empty(store.Load());
    }

    [Fact]
    public void Save_NullOrEmptyServer_IsIgnored()
    {
        using var tmp = TempDir();
        var store = new ServerPortsStore(directoryOverride: tmp.Path);

        store.Save("   ", new ServerPortsSettings(1541, 0, 0));

        Assert.Empty(store.Load());
    }

    [Fact]
    public void Save_OutOfRangePorts_AreSanitizedToZero()
    {
        using var tmp = TempDir();
        var store = new ServerPortsStore(directoryOverride: tmp.Path);

        store.Save("srv", new ServerPortsSettings(70000, -1, 1542));

        var ports = store.Load()["srv"];
        Assert.Equal(0, ports.Cluster);
        Assert.Equal(0, ports.Agent);
        Assert.Equal(1542, ports.Repository);
    }

    private static TempDirectory TempDir() => new();

    private sealed class TempDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"cm_sps_{Guid.NewGuid():N}");

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); } catch { /* не критично */ }
        }
    }
}