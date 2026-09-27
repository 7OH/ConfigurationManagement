using Configuration_Management.Services;
using Xunit;

namespace ConfigurationManagement.Tests;

/// <summary>
/// Тесты определения файловой ИБ по перетащенному пути (0.3.9.92):
/// файл 1Cv8.1CD → родительский каталог; каталог с 1Cv8.1CD → сам каталог;
/// каталог без 1Cv8.1CD → невалидно; сравнение путей-дубликатов без учёта
/// регистра и хвостовых разделителей.
/// </summary>
public sealed class DroppedBaseDetectorTests : IDisposable
{
    private readonly string _tempRoot;

    public DroppedBaseDetectorTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"cm-dnd-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempRoot))
                Directory.Delete(_tempRoot, recursive: true);
        }
        catch (IOException) { /* тестовая уборка: каталог может быть занят */ }
        catch (UnauthorizedAccessException) { /* то же */ }
    }

    private string CreateBaseDirectory(string name)
    {
        var dir = Path.Combine(_tempRoot, name);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, DroppedBaseDetector.BaseDataFileName), string.Empty);
        return dir;
    }

    [Fact]
    public void ResolveBasePath_File1Cv8_1Cd_ReturnsParentDirectory()
    {
        var baseDir = CreateBaseDirectory("Base");
        var file = Path.Combine(baseDir, DroppedBaseDetector.BaseDataFileName);

        var result = DroppedBaseDetector.ResolveBasePath(file);

        Assert.NotNull(result);
        Assert.Equal(baseDir, result);
    }

    [Fact]
    public void ResolveBasePath_DirectoryWith1Cv8_1Cd_ReturnsItself()
    {
        var baseDir = CreateBaseDirectory("Base");

        var result = DroppedBaseDetector.ResolveBasePath(baseDir);

        Assert.NotNull(result);
        Assert.Equal(baseDir, result);
    }

    [Fact]
    public void ResolveBasePath_DirectoryWithout1Cv8_1Cd_ReturnsNull()
    {
        var emptyDir = Path.Combine(_tempRoot, "Empty");
        Directory.CreateDirectory(emptyDir);

        Assert.Null(DroppedBaseDetector.ResolveBasePath(emptyDir));
    }

    [Fact]
    public void ResolveBasePath_FileWithOtherName_ReturnsNull()
    {
        var path = Path.Combine(_tempRoot, "notes.txt");
        File.WriteAllText(path, "hello");

        Assert.Null(DroppedBaseDetector.ResolveBasePath(path));
    }

    [Fact]
    public void ResolveBasePath_NonExistentPath_ReturnsNull()
    {
        Assert.Null(DroppedBaseDetector.ResolveBasePath(Path.Combine(_tempRoot, "missing")));
        Assert.Null(DroppedBaseDetector.ResolveBasePath(Path.Combine(_tempRoot, "missing", "1Cv8.1CD")));
    }

    [Fact]
    public void ResolveBasePath_NullOrWhitespace_ReturnsNull()
    {
        Assert.Null(DroppedBaseDetector.ResolveBasePath(null));
        Assert.Null(DroppedBaseDetector.ResolveBasePath(string.Empty));
        Assert.Null(DroppedBaseDetector.ResolveBasePath("   "));
    }

    [Fact]
    public void ResolveBasePaths_MixedInput_ReturnsOnlyValid()
    {
        var baseA = CreateBaseDirectory("A");
        var baseB = CreateBaseDirectory("B");
        var emptyDir = Path.Combine(_tempRoot, "Empty");
        Directory.CreateDirectory(emptyDir);
        var file = Path.Combine(baseA, DroppedBaseDetector.BaseDataFileName);

        var result = DroppedBaseDetector.ResolveBasePaths(new[]
        {
            baseA,                 // каталог с 1Cv8.1CD → принимается
            file,                  // сам файл → родитель
            emptyDir,              // каталог без 1Cv8.1CD → отбрасывается
            baseB,                 // каталог с 1Cv8.1CD → принимается
            Path.Combine(_tempRoot, "nope") // несуществующий → отбрасывается
        });

        Assert.Equal(3, result.Count);
        Assert.Equal(baseA, result[0]);
        Assert.Equal(baseA, result[1]); // файл сводится к своему каталогу
        Assert.Equal(baseB, result[2]);
    }

    [Fact]
    public void ResolveBasePaths_Null_ReturnsEmpty()
    {
        Assert.Empty(DroppedBaseDetector.ResolveBasePaths(null));
        Assert.Empty(DroppedBaseDetector.ResolveBasePaths(Array.Empty<string>()));
    }

    [Fact]
    public void HasAnyBasePath_TrueWhenAtLeastOneValid()
    {
        var baseDir = CreateBaseDirectory("Base");

        Assert.True(DroppedBaseDetector.HasAnyBasePath(new[] { Path.Combine(_tempRoot, "junk"), baseDir }));
        Assert.False(DroppedBaseDetector.HasAnyBasePath(new[] { Path.Combine(_tempRoot, "junk") }));
        Assert.False(DroppedBaseDetector.HasAnyBasePath(null));
    }

    [Fact]
    public void AreSameBasePath_IgnoresCaseAndTrailingSeparators()
    {
        // Windows-стиль: регистр диска/пути и хвостовой разделитель.
        Assert.True(DroppedBaseDetector.AreSameBasePath(@"C:\Bases\Бухгалтерия", @"c:\bases\бухгалтерия\"));
        // Unix-стиль.
        Assert.True(DroppedBaseDetector.AreSameBasePath("/home/user/1c/base", "/home/user/1c/base/"));
        // Разные пути — разные базы.
        Assert.False(DroppedBaseDetector.AreSameBasePath(@"C:\Bases\A", @"C:\Bases\B"));
        // Null и пустая строка эквивалентны.
        Assert.True(DroppedBaseDetector.AreSameBasePath(null, string.Empty));
    }

    [Fact]
    public void NormalizePathForCompare_DoesNotCollapseRoot()
    {
        Assert.Equal(@"C:\", DroppedBaseDetector.NormalizePathForCompare(@"C:\"));
        Assert.Equal("/", DroppedBaseDetector.NormalizePathForCompare("/"));
        Assert.Equal(@"C:\Bases\A", DroppedBaseDetector.NormalizePathForCompare(@"C:\Bases\A\\"));
        Assert.Equal("base", DroppedBaseDetector.NormalizePathForCompare("  base/  "));
    }
}