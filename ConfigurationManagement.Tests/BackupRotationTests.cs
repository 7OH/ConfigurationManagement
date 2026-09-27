using Configuration_Management.Services;
using Xunit;

namespace ConfigurationManagement.Tests;

/// <summary>
/// Тесты чистой логики ротации резервных копий (<see cref="BackupRotation.Apply"/>):
/// лимит количества, возрастной порог, фильтр префикса и комбинация правил.
/// </summary>
public sealed class BackupRotationTests : IDisposable
{
    private readonly string _root;

    public BackupRotationTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "cm_rotation_tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch { /* временный каталог — не критично */ }
    }

    private string WriteFile(string name, DateTime? lastWriteUtc = null)
    {
        var path = Path.Combine(_root, name);
        File.WriteAllText(path, name);
        if (lastWriteUtc.HasValue)
            File.SetLastWriteTimeUtc(path, lastWriteUtc.Value);
        return path;
    }

    [Fact]
    public void Apply_MissingDirectory_ReturnsEmpty()
    {
        var deleted = BackupRotation.Apply(
            Path.Combine(_root, "no_such_dir"), "Base", ".dt", 2, 0);

        Assert.Empty(deleted);
    }

    [Fact]
    public void Apply_KeepCount_KeepsNewestCopies()
    {
        var now = DateTime.UtcNow;
        WriteFile("Base_20260101.dt", now.AddDays(-10));
        WriteFile("Base_20260102.dt", now.AddDays(-9));
        WriteFile("Base_20260103.dt", now.AddDays(-8));

        var deleted = BackupRotation.Apply(_root, "Base", ".dt", keepCount: 2, deleteOlderThanDays: 0);

        Assert.Single(deleted);
        Assert.Equal("Base_20260101.dt", Path.GetFileName(deleted[0]));
        Assert.True(File.Exists(Path.Combine(_root, "Base_20260102.dt")));
        Assert.True(File.Exists(Path.Combine(_root, "Base_20260103.dt")));
    }

    [Fact]
    public void Apply_KeepCountZero_DeletesNothing()
    {
        WriteFile("Base_1.dt", DateTime.UtcNow.AddDays(-5));
        WriteFile("Base_2.dt", DateTime.UtcNow.AddDays(-4));

        var deleted = BackupRotation.Apply(_root, "Base", ".dt", keepCount: 0, deleteOlderThanDays: 0);

        Assert.Empty(deleted);
        Assert.Equal(2, Directory.EnumerateFiles(_root, "*.dt").Count());
    }

    [Fact]
    public void Apply_Age_DeletesOlderThanDays()
    {
        var now = DateTime.UtcNow;
        WriteFile("Base_old.dt", now.AddDays(-40));
        WriteFile("Base_new.dt", now.AddDays(-1));

        var deleted = BackupRotation.Apply(_root, "Base", ".dt", keepCount: 0, deleteOlderThanDays: 30);

        Assert.Single(deleted);
        Assert.Equal("Base_old.dt", Path.GetFileName(deleted[0]));
        Assert.True(File.Exists(Path.Combine(_root, "Base_new.dt")));
    }

    [Fact]
    public void Apply_AgeZero_DeletesNothing()
    {
        WriteFile("Base_old.dt", DateTime.UtcNow.AddDays(-400));

        var deleted = BackupRotation.Apply(_root, "Base", ".dt", keepCount: 0, deleteOlderThanDays: 0);

        Assert.Empty(deleted);
    }

    [Fact]
    public void Apply_KeepAndAge_CombinedRules()
    {
        var now = DateTime.UtcNow;
        // Старая и за пределами лимита — удаляется по возрасту.
        WriteFile("Base_01.dt", now.AddDays(-50));
        // Старая, но в пределах лимита — всё равно удаляется по возрасту.
        WriteFile("Base_02.dt", now.AddDays(-40));
        WriteFile("Base_03.dt", now.AddDays(-2));
        WriteFile("Base_04.dt", now.AddDays(-1));

        var deleted = BackupRotation.Apply(_root, "Base", ".dt", keepCount: 2, deleteOlderThanDays: 30);

        Assert.Equal(2, deleted.Count);
        Assert.Contains(deleted.Select(Path.GetFileName), n => n == "Base_01.dt");
        Assert.Contains(deleted.Select(Path.GetFileName), n => n == "Base_02.dt");
        Assert.True(File.Exists(Path.Combine(_root, "Base_03.dt")));
        Assert.True(File.Exists(Path.Combine(_root, "Base_04.dt")));
    }

    [Fact]
    public void Apply_Prefix_FiltersFilesByName()
    {
        WriteFile("Base_1.dt", DateTime.UtcNow.AddDays(-5));
        WriteFile("Base_2.dt", DateTime.UtcNow.AddDays(-4));
        WriteFile("Other_1.dt", DateTime.UtcNow.AddDays(-5));
        WriteFile("Base_2.cf", DateTime.UtcNow.AddDays(-5));

        var deleted = BackupRotation.Apply(_root, "Base", ".dt", keepCount: 1, deleteOlderThanDays: 0);

        Assert.Single(deleted);
        Assert.Equal("Base_1.dt", Path.GetFileName(deleted[0]));
        // Чужие файлы и чужие расширения не трогаем.
        Assert.True(File.Exists(Path.Combine(_root, "Other_1.dt")));
        Assert.True(File.Exists(Path.Combine(_root, "Base_2.cf")));
    }

    [Fact]
    public void Apply_EmptyPrefix_MatchesAllExtensions()
    {
        WriteFile("A.dt", DateTime.UtcNow.AddDays(-5));
        WriteFile("B.dt", DateTime.UtcNow.AddDays(-4));

        var deleted = BackupRotation.Apply(_root, "", ".dt", keepCount: 1, deleteOlderThanDays: 0);

        Assert.Single(deleted);
        Assert.Equal("A.dt", Path.GetFileName(deleted[0]));
    }

    [Fact]
    public void Apply_ExtensionWithoutDot_Normalizes()
    {
        WriteFile("Base_1.dt", DateTime.UtcNow.AddDays(-5));
        WriteFile("Base_2.dt", DateTime.UtcNow.AddDays(-4));

        var deleted = BackupRotation.Apply(_root, "Base", "dt", keepCount: 1, deleteOlderThanDays: 0);

        Assert.Single(deleted);
        Assert.Equal("Base_1.dt", Path.GetFileName(deleted[0]));
    }

    [Fact]
    public void Apply_SortsByLastWriteDesc_WhenFileTimesDifferFromNames()
    {
        var now = DateTime.UtcNow;
        // Имя «новое», но по времени записи — самое старое: ротация ориентируется
        // на LastWriteTime, а не на имя.
        WriteFile("Base_latest_name.dt", now.AddDays(-10));
        WriteFile("Base_01.dt", now.AddDays(-1));
        WriteFile("Base_02.dt", now.AddHours(-1));

        var deleted = BackupRotation.Apply(_root, "Base", ".dt", keepCount: 2, deleteOlderThanDays: 0);

        Assert.Single(deleted);
        Assert.Equal("Base_latest_name.dt", Path.GetFileName(deleted[0]));
    }
}