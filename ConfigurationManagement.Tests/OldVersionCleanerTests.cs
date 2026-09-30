using System;
using System.Collections.Generic;
using System.Linq;
using Configuration_Management.Models;
using Configuration_Management.Services;
using Xunit;

namespace ConfigurationManagement.Tests;

/// <summary>
/// Тесты чистого отбора кандидатов на удаление старых версий платформы 1С
/// (этап 0.3.9.215, <see cref="OldVersionCleaner"/>): новейшая версия исключается,
/// версии, на которые ссылаются базы репозитория (точное и префиксное совпадение),
/// исключаются; версии запущенных процессов исключаются по пути bin; пустой вход
/// даёт пустой список; результат отсортирован по убыванию.
/// </summary>
public sealed class OldVersionCleanerTests
{
    private static PlatformVersionInfo Installed(string display, string? path = null)
        => new() { Display = display, Path = path ?? string.Empty };

    private static Infobase Base(string id, string platformVersion)
        => new() { Id = id, Name = $"База {id}", PlatformVersion = platformVersion };

    // ---------- Новейшая исключена ----------

    [Fact]
    public void SelectCandidates_ExcludesNewestVersion()
    {
        var installed = new[]
        {
            Installed("8.3.27.2214"),
            Installed("8.3.27.1688"),
            Installed("8.3.26.1890"),
        };

        var result = OldVersionCleaner.SelectCandidates(installed, Array.Empty<Infobase>(), Array.Empty<string>());

        Assert.Equal(new[] { "8.3.27.1688", "8.3.26.1890" }, result.Select(v => v.Display).ToArray());
    }

    [Fact]
    public void SelectCandidates_ExcludesAllArchitecturesOfNewestVersion()
    {
        // 64- и 32-битные установки одного номера — обе новейшие (численное сравнение равно 0).
        var installed = new[]
        {
            Installed("8.3.27.2214 (64)"),
            Installed("8.3.27.2214 (32)"),
            Installed("8.3.27.1688 (64)"),
        };

        var result = OldVersionCleaner.SelectCandidates(installed, Array.Empty<Infobase>(), Array.Empty<string>());

        Assert.Single(result);
        Assert.Equal("8.3.27.1688 (64)", result[0].Display);
    }

    // ---------- Используемые базами исключены ----------

    [Fact]
    public void SelectCandidates_ExcludesVersionReferencedByBase_ExactMatch()
    {
        var installed = new[]
        {
            Installed("8.3.27.2214"),
            Installed("8.3.27.1688"),
            Installed("8.3.26.1890"),
        };
        var bases = new List<Infobase> { Base("1", "8.3.27.1688") };

        var result = OldVersionCleaner.SelectCandidates(installed, bases, Array.Empty<string>());

        Assert.Equal(new[] { "8.3.26.1890" }, result.Select(v => v.Display).ToArray());
    }

    [Fact]
    public void SelectCandidates_ExcludesVersionReferencedByBase_WithArchitectureSuffix()
    {
        // База ссылается на «8.3.27.1688 (64)» — та же версия численно.
        var installed = new[] { Installed("8.3.27.2214"), Installed("8.3.27.1688") };
        var bases = new List<Infobase> { Base("1", "8.3.27.1688 (64)") };

        var result = OldVersionCleaner.SelectCandidates(installed, bases, Array.Empty<string>());

        Assert.Empty(result);
    }

    [Fact]
    public void SelectCandidates_ExcludesWholeFamily_WhenBaseUsesPartialPrefix()
    {
        // База с частичной версией «8.3.26» охватывает все сборки семейства:
        // обе старые 8.3.26.* исключены, новейшая 8.3.27.2214 — по новизне.
        var installed = new[]
        {
            Installed("8.3.27.2214"),
            Installed("8.3.26.1890"),
            Installed("8.3.26.1644"),
        };
        var bases = new List<Infobase> { Base("1", "8.3.26") };

        var result = OldVersionCleaner.SelectCandidates(installed, bases, Array.Empty<string>());

        Assert.Empty(result);
    }

    [Fact]
    public void SelectCandidates_EmptyBaseVersion_DoesNotExcludeAnything()
    {
        var installed = new[] { Installed("8.3.27.2214"), Installed("8.3.27.1688") };
        var bases = new List<Infobase> { Base("1", "") };

        var result = OldVersionCleaner.SelectCandidates(installed, bases, Array.Empty<string>());

        Assert.Single(result);
        Assert.Equal("8.3.27.1688", result[0].Display);
    }

    // ---------- Запущенный процесс исключён ----------

    [Fact]
    public void SelectCandidates_ExcludesVersionRunningFromItsBinDirectory()
    {
        const string runningDir = @"C:\Program Files\1cv8\8.3.27.1688";
        var installed = new[]
        {
            Installed("8.3.27.2214", @"C:\Program Files\1cv8\8.3.27.2214"),
            Installed("8.3.27.1688", runningDir),
            Installed("8.3.26.1890", @"C:\Program Files\1cv8\8.3.26.1890"),
        };
        var runningBinPaths = new List<string>
        {
            System.IO.Path.Combine(runningDir, "bin", "1cv8c.exe"),
        };

        var result = OldVersionCleaner.SelectCandidates(installed, Array.Empty<Infobase>(), runningBinPaths);

        // 8.3.27.1688 запущена — исключена; осталась только 8.3.26.1890.
        Assert.Equal(new[] { "8.3.26.1890" }, result.Select(v => v.Display).ToArray());
    }

    [Fact]
    public void SelectCandidates_PathWithSimilarPrefix_IsNotTreatedAsRunning()
    {
        // «8.3.27.16882» — другой каталог, его процесс не должен исключать «8.3.27.1688».
        var installed = new[]
        {
            Installed("8.3.27.2214", @"C:\Program Files\1cv8\8.3.27.2214"),
            Installed("8.3.27.1688", @"C:\Program Files\1cv8\8.3.27.1688"),
        };
        var runningBinPaths = new List<string>
        {
            @"C:\Program Files\1cv8\8.3.27.16882\bin\1cv8c.exe",
        };

        var result = OldVersionCleaner.SelectCandidates(installed, Array.Empty<Infobase>(), runningBinPaths);

        Assert.Single(result);
        Assert.Equal("8.3.27.1688", result[0].Display);
    }

    // ---------- Пустой вход / сортировка ----------

    [Fact]
    public void SelectCandidates_EmptyInstalled_ReturnsEmpty()
    {
        var result = OldVersionCleaner.SelectCandidates(
            Array.Empty<PlatformVersionInfo>(), Array.Empty<Infobase>(), Array.Empty<string>());

        Assert.Empty(result);

        Assert.Empty(OldVersionCleaner.SelectCandidates(
            null!, new List<Infobase> { Base("1", "8.3.27.1688") }, Array.Empty<string>()));
    }

    [Fact]
    public void SelectCandidates_ResultIsSortedByVersionDescending()
    {
        var installed = new[]
        {
            Installed("8.3.27.2214"),
            Installed("8.3.10.2012"),
            Installed("8.3.9.2577"),
            Installed("8.3.27.1688"),
        };

        var result = OldVersionCleaner.SelectCandidates(installed, Array.Empty<Infobase>(), Array.Empty<string>());

        Assert.Equal(new[] { "8.3.27.1688", "8.3.10.2012", "8.3.9.2577" }, result.Select(v => v.Display).ToArray());
    }

    // ---------- Вспомогательные хелперы ----------

    [Fact]
    public void CleanVersion_StripsArchitectureSuffix()
    {
        Assert.Equal("8.3.27.2214", OldVersionCleaner.CleanVersion("8.3.27.2214 (64)"));
        Assert.Equal("8.3.27.2214", OldVersionCleaner.CleanVersion("8.3.27.2214 (32)"));
        Assert.Equal("8.3.27.2214", OldVersionCleaner.CleanVersion("8.3.27.2214"));
        Assert.Equal("", OldVersionCleaner.CleanVersion(null));
    }

    [Fact]
    public void ExtractExecutablePath_TakesFirstTokenRespectingQuotes()
    {
        Assert.Equal(
            @"C:\Program Files\1cv8\8.3.27.1688\bin\1cv8c.exe",
            OldVersionCleaner.ExtractExecutablePath(
                "\"C:\\Program Files\\1cv8\\8.3.27.1688\\bin\\1cv8c.exe\" /F \"C:\\base\""));

        Assert.Equal(
            "/opt/1cv8/8.3.27.1688/bin/1cv8c",
            OldVersionCleaner.ExtractExecutablePath("/opt/1cv8/8.3.27.1688/bin/1cv8c /F /home/user/base"));

        Assert.Null(OldVersionCleaner.ExtractExecutablePath(null));
        Assert.Null(OldVersionCleaner.ExtractExecutablePath("   "));
        Assert.Null(OldVersionCleaner.ExtractExecutablePath("\"не закрытая кавычка"));
    }
}