using Configuration_Management.Services;
using Xunit;

namespace ConfigurationManagement.Tests;

/// <summary>
/// Тесты персистентного списка путей удалённых пользователем пустых групп
/// (issue #327): JSON-файл deleted_groups.json рядом с настройками; Add идемпотентен,
/// Clear удаляет файл (группы снова начинают импортироваться).
/// </summary>
public sealed class DeletedGroupPathsStoreTests
{
    [Fact]
    public void Add_ThenLoad_ReturnsAddedPath()
    {
        using var tmp = TempDir();
        var store = new DeletedGroupPathsStore(directoryOverride: tmp.Path);

        store.Add("Parent / Old");

        Assert.Equal(new[] { "Parent / Old" }, store.Load());
        Assert.True(File.Exists(store.FilePath));
    }

    [Fact]
    public void Add_SamePathTwice_KeepsSingleEntry()
    {
        using var tmp = TempDir();
        var store = new DeletedGroupPathsStore(directoryOverride: tmp.Path);

        store.Add("Учёт / Бухгалтерия");
        store.Add("учёт / бухгалтерия"); // регистронезависимо — дубль

        var loaded = store.Load();
        Assert.Single(loaded);
        Assert.Equal("Учёт / Бухгалтерия", loaded[0]);
    }

    [Fact]
    public void Clear_RemovesFileAndList()
    {
        using var tmp = TempDir();
        var store = new DeletedGroupPathsStore(directoryOverride: tmp.Path);

        store.Add("Old");
        store.Clear();

        Assert.False(File.Exists(store.FilePath));
        Assert.Empty(store.Load());
    }

    [Fact]
    public void Load_WhenNoFile_ReturnsEmpty()
    {
        using var tmp = TempDir();
        var store = new DeletedGroupPathsStore(directoryOverride: tmp.Path);

        Assert.Empty(store.Load());
    }

    private static TempDirectory TempDir() => new();

    private sealed class TempDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"cm_dgp_{Guid.NewGuid():N}");

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); } catch { /* не критично */ }
        }
    }
}