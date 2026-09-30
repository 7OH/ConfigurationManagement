using System.Collections.Generic;
using System.Linq;
using Configuration_Management.Models;
using Configuration_Management.Services;
using Xunit;

namespace ConfigurationManagement.Tests;

/// <summary>
/// Тесты чистых хелперов сопоставления версий платформы 1С
/// (<see cref="PlatformUpdateMatcher"/>): объединение установленных и доступных
/// версий в список строк окна (признак «есть обновление», сортировка числовыми
/// сегментами) и подсчёт совместимых с версией информационных баз.
/// </summary>
public sealed class PlatformUpdateMatcherTests
{
    // --- Merge ---

    [Fact]
    public void Merge_InstalledIsNewest_NoUpdateFlagAndBothRowsPresent()
    {
        var matches = PlatformUpdateMatcher.Merge(
            new[] { "8.3.27.2214" },
            Releases("8.3.27.2214", "8.3.26.1890"));

        Assert.Equal(2, matches.Count);
        var installed = matches.Single(m => m.Version == "8.3.27.2214");
        Assert.True(installed.IsInstalled);
        Assert.False(installed.HasUpdate);
        Assert.Null(installed.AvailableVersion);
    }

    [Fact]
    public void Merge_InstalledHasNewerAvailable_SetsHasUpdateAndAvailableVersion()
    {
        var matches = PlatformUpdateMatcher.Merge(
            new[] { "8.3.27.1688" },
            Releases("8.3.27.2214", "8.3.27.1688", "8.3.26.1890"));

        var installed = matches.Single(m => m.Version == "8.3.27.1688");
        Assert.True(installed.IsInstalled);
        Assert.True(installed.HasUpdate);
        Assert.Equal("8.3.27.2214", installed.AvailableVersion);
    }

    [Fact]
    public void Merge_InstalledNotInCatalog_KeepsInstalledRowWithUpdateFlag()
    {
        // Установленная 8.3.26.1890 отсутствует в каталоге, но для неё есть более новые
        // доступные версии — строка остаётся установленной с признаком обновления.
        var matches = PlatformUpdateMatcher.Merge(
            new[] { "8.3.26.1890" },
            Releases("8.3.27.2214", "8.3.27.1688"));

        Assert.Equal(3, matches.Count);
        var installed = matches.Single(m => m.Version == "8.3.26.1890");
        Assert.True(installed.IsInstalled);
        Assert.True(installed.HasUpdate);
        Assert.Equal("8.3.27.2214", installed.AvailableVersion);

        var availableOnly = matches.Where(m => m.Version != "8.3.26.1890").ToList();
        Assert.All(availableOnly, m => Assert.False(m.IsInstalled));
        Assert.All(availableOnly, m => Assert.False(m.HasUpdate));
        Assert.All(availableOnly, m => Assert.Null(m.AvailableVersion));
    }

    [Fact]
    public void Merge_SortsDescendingByNumericSegments()
    {
        var matches = PlatformUpdateMatcher.Merge(
            new[] { "8.3.9" },
            Releases("8.3.10", "8.3.27.2214", "8.3.9"));

        Assert.Equal(new[] { "8.3.27.2214", "8.3.10", "8.3.9" },
            matches.Select(m => m.Version));
    }

    [Fact]
    public void Merge_DuplicatesInstalled_AreDeduplicated()
    {
        var matches = PlatformUpdateMatcher.Merge(
            new[] { "8.3.27.2214", "8.3.27.2214", "8.3.9" },
            Releases("8.3.27.2214"));

        Assert.Equal(2, matches.Count);
        Assert.Single(matches, m => m.Version == "8.3.27.2214");
    }

    [Fact]
    public void Merge_NullArguments_ReturnsEmptyList()
    {
        Assert.Empty(PlatformUpdateMatcher.Merge(null!, null!));
        Assert.Empty(PlatformUpdateMatcher.Merge(new List<string>(), null!));
    }

    // --- CountCompatibleBases ---

    [Fact]
    public void CountCompatibleBases_ExactFourSegments_CountsSameLineBases()
    {
        // Точное 4-сегментное совпадение + префикс 3 сегментов: обе базы линии 8.3.27
        // считаются совместимыми с «8.3.27.2214», база линии 8.3.26 — нет.
        var bases = new[]
        {
            NewBase("8.3.27.2214"),
            NewBase("8.3.27.1688"),
            NewBase("8.3.26.1890"),
        };

        Assert.Equal(2, PlatformUpdateMatcher.CountCompatibleBases("8.3.27.2214", bases));
    }

    [Fact]
    public void CountCompatibleBases_PrefixThreeSegments_CountsWholeLine()
    {
        var bases = new[]
        {
            NewBase("8.3.27.2214"),
            NewBase("8.3.27.1688"),
            NewBase("8.3.27"),
            NewBase("8.3.26.1890"),
        };

        Assert.Equal(3, PlatformUpdateMatcher.CountCompatibleBases("8.3.27.2214", bases));
    }

    [Fact]
    public void CountCompatibleBases_CaseInsensitive_IgnoresCase()
    {
        // Сегменты с латинским маркером линии: префикс «V8.3.27» должен совпадать
        // с базовой версией «v8.3.27…» независимо от регистра.
        var bases = new[]
        {
            NewBase("v8.3.27.2214"),
            NewBase("v8.3.27.1688"),
            NewBase("v8.3.26.1890"),
        };

        Assert.Equal(2, PlatformUpdateMatcher.CountCompatibleBases("V8.3.27.2214", bases));
    }

    [Fact]
    public void CountCompatibleBases_WrongLine_NotCounted()
    {
        var bases = new[]
        {
            NewBase("8.3.2"),
            NewBase("8.5.1.100"),
            NewBase("8.3.10"),
        };

        // «8.3.2» не начинается с префикса «8.3.27», «8.5.1.100» — другая линия.
        Assert.Equal(0, PlatformUpdateMatcher.CountCompatibleBases("8.3.27.2214", bases));
    }

    [Fact]
    public void CountCompatibleBases_EmptyPlatformVersion_NotCounted()
    {
        var bases = new[]
        {
            NewBase("8.3.27.2214"),
            NewBase(string.Empty),
            NewBase("   "),
            NewBase(null),
        };

        Assert.Equal(1, PlatformUpdateMatcher.CountCompatibleBases("8.3.27.2214", bases));
    }

    [Fact]
    public void CountCompatibleBases_ShortSelectedVersion_UsesAvailableSegments()
    {
        var bases = new[]
        {
            NewBase("8.3.27.2214"),
            NewBase("8.3.10"),
            NewBase("8.2.14.540"),
        };

        Assert.Equal(2, PlatformUpdateMatcher.CountCompatibleBases("8.3", bases));
    }

    /// <summary>Создаёт релизы по списку версий.</summary>
    private static List<PlatformRelease> Releases(params string[] versions)
        => versions.Select(v => new PlatformRelease
        {
            Version = v,
            VersionFilesUrl = $"/version_files?nick=Platform83&ver={v}",
        }).ToList();

    /// <summary>Создаёт базу с указанной версией платформы.</summary>
    private static Infobase NewBase(string? platformVersion)
        => new() { Id = "id", Name = "База", PlatformVersion = platformVersion ?? string.Empty };
}