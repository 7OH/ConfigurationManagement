using Configuration_Management.Services;
using Xunit;

namespace ConfigurationManagement.Tests;

/// <summary>
/// Тесты чистой логики дублирования файловой ИБ (<see cref="InfobaseCloneHelper"/>):
/// предлагаемое имя, уникальный целевой каталог, рекурсивное копирование.
/// </summary>
public sealed class InfobaseCloneHelperTests : IDisposable
{
    private readonly string _root;

    public InfobaseCloneHelperTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "cm_clone_tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch { /* временный каталог — не критично */ }
    }

    [Theory]
    [InlineData("Бухгалтерия", "Бухгалтерия — Копия")]
    [InlineData("", "Копия")]
    [InlineData("  Зарплата  ", "Зарплата — Копия")]
    public void ProposeCloneName_AppendsSuffix(string source, string expected)
    {
        Assert.Equal(expected, InfobaseCloneHelper.ProposeCloneName(source));
    }

    [Fact]
    public void BuildTargetDirectory_PlacesNextToSource()
    {
        var source = Path.Combine(_root, "Base1");
        Directory.CreateDirectory(source);

        var target = InfobaseCloneHelper.BuildTargetDirectory(source, "Base1 — Копия");

        Assert.NotNull(target);
        Assert.Equal(Path.Combine(_root, "Base1 — Копия"), target);
    }

    [Fact]
    public void BuildTargetDirectory_UniquifiesOnCollision()
    {
        var source = Path.Combine(_root, "Base1");
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(Path.Combine(_root, "Copy"));
        Directory.CreateDirectory(Path.Combine(_root, "Copy 2"));

        var target = InfobaseCloneHelper.BuildTargetDirectory(source, "Copy");

        Assert.NotNull(target);
        Assert.Equal(Path.Combine(_root, "Copy 3"), target);
    }

    [Fact]
    public void BuildTargetDirectory_SanitizesInvalidChars()
    {
        var source = Path.Combine(_root, "Base1");
        Directory.CreateDirectory(source);

        var target = InfobaseCloneHelper.BuildTargetDirectory(source, "a<b>c");

        Assert.NotNull(target);
        Assert.EndsWith("a_b_c", target);
    }

    [Fact]
    public void BuildTargetDirectory_MissingSource_ReturnsNull()
    {
        Assert.Null(InfobaseCloneHelper.BuildTargetDirectory(
            Path.Combine(_root, "no_such_dir"), "Copy"));
    }

    [Fact]
    public void CopyDirectory_CopiesRecursively()
    {
        var source = Path.Combine(_root, "src");
        var nested = Path.Combine(source, "sub");
        Directory.CreateDirectory(nested);
        WriteFile(Path.Combine(source, "1cd.1CD"), "hello");
        WriteFile(Path.Combine(nested, "log.txt"), "world");

        var target = Path.Combine(_root, "dst");
        var ok = InfobaseCloneHelper.CopyDirectory(source, target);

        Assert.True(ok);
        Assert.Equal("hello", File.ReadAllText(Path.Combine(target, "1cd.1CD")));
        Assert.Equal("world", File.ReadAllText(Path.Combine(target, "sub", "log.txt")));
    }

    [Fact]
    public void CopyDirectory_ExistingTarget_ReturnsFalse()
    {
        var source = Path.Combine(_root, "src2");
        Directory.CreateDirectory(source);
        var target = Path.Combine(_root, "dst2");
        Directory.CreateDirectory(target);

        Assert.False(InfobaseCloneHelper.CopyDirectory(source, target));
    }

    [Fact]
    public void GetDirectorySize_SumsFiles()
    {
        var dir = Path.Combine(_root, "size");
        Directory.CreateDirectory(dir);
        WriteFile(Path.Combine(dir, "a.bin"), "12345");
        WriteFile(Path.Combine(dir, "b.bin"), "12");

        Assert.Equal(7, InfobaseCloneHelper.GetDirectorySize(dir));
    }

    private static void WriteFile(string path, string content) =>
        File.WriteAllText(path, content);
}
