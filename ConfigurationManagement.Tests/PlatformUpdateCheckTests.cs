using Configuration_Management.Models;
using Configuration_Management.Services;
using Xunit;

namespace ConfigurationManagement.Tests;

/// <summary>
/// Юнит-тесты чистой логики задания по расписанию «Проверить наличие обновления платформы»
/// (issue #366): разбор/сопоставление выражения проверки и сравнение установленных версий
/// с доступными в каталоге. Без UI, сети и файловой системы.
/// </summary>
public sealed class PlatformUpdateCheckTests
{
    // ---- Разбор выражения ----

    [Theory]
    [InlineData("8.5.*")]
    [InlineData("8.3.27.*")]
    [InlineData("8.3")]
    [InlineData("8")]
    [InlineData("*")]
    [InlineData(" 8.3.* ")]
    public void ValidExpression_IsValid(string expression)
    {
        Assert.True(PlatformUpdateCheckLogic.IsValidExpression(expression));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("8..3")]
    [InlineData("8.3.")]
    [InlineData(".8.3")]
    [InlineData("8.x.*")]
    [InlineData("8.3.-1")]
    [InlineData("8.3.*.1.2.3.4.5")]
    public void InvalidExpression_IsNotValid(string? expression)
    {
        Assert.False(PlatformUpdateCheckLogic.IsValidExpression(expression));
    }

    // ---- Сопоставление версии с выражением ----

    [Theory]
    [InlineData("8.5.2.1966", "8.5.*", true)]
    [InlineData("8.5.2.1966", "8.3.27.*", false)]
    [InlineData("8.3.27.2214", "8.3.27.*", true)]
    [InlineData("8.3.27.2214", "8.3.*", true)]
    [InlineData("8.3.27.2214", "8.3", true)]
    [InlineData("8.3.27", "8.3.27.*", true)]
    [InlineData("8.3.27.2214", "8.*", true)]
    [InlineData("8.3.27.2214", "*", true)]
    [InlineData("8.4.0.1", "8.3.*", false)]
    [InlineData("8.3.27.2214 (64)", "8.3.27.*", false)]
    public void MatchesExpression_ComparesSegments(string version, string expression, bool expected)
    {
        Assert.Equal(expected, PlatformUpdateCheckLogic.MatchesExpression(version, expression));
    }

    // ---- Сравнение установленных и доступных версий ----

    [Fact]
    public void Check_NoInstalled_AllMatchingVersionsAvailable()
    {
        var releases = new[]
        {
            new PlatformRelease { Version = "8.3.27.2214" },
            new PlatformRelease { Version = "8.5.2.1966" },
            new PlatformRelease { Version = "8.2.19.1234" }
        };

        var result = PlatformUpdateCheckLogic.Check(
            installedVersions: Array.Empty<string>(),
            availableReleases: releases,
            expression: "8.3.*",
            includeTestVersions: false);

        Assert.Null(result.MaxInstalledVersion);
        Assert.True(result.HasUpdate);
        Assert.Equal(new[] { "8.3.27.2214" }, result.AvailableVersions);
    }

    [Fact]
    public void Check_NewerVersionAvailable_Reported()
    {
        var releases = new[]
        {
            new PlatformRelease { Version = "8.3.27.2214" },
            new PlatformRelease { Version = "8.3.28.1340" }
        };

        var result = PlatformUpdateCheckLogic.Check(
            new[] { "8.3.27.1688" },
            releases,
            "8.3.*",
            includeTestVersions: false);

        Assert.Equal("8.3.27.1688", result.MaxInstalledVersion);
        Assert.True(result.HasUpdate);
        Assert.Equal(new[] { "8.3.28.1340", "8.3.27.2214" }, result.AvailableVersions);
    }

    [Fact]
    public void Check_InstalledIsLatest_NoUpdate()
    {
        var releases = new[]
        {
            new PlatformRelease { Version = "8.3.27.1688" },
            new PlatformRelease { Version = "8.3.26.1456" }
        };

        var result = PlatformUpdateCheckLogic.Check(
            new[] { "8.3.27.1688" },
            releases,
            "8.3.*",
            includeTestVersions: false);

        Assert.Equal("8.3.27.1688", result.MaxInstalledVersion);
        Assert.False(result.HasUpdate);
    }

    [Fact]
    public void Check_OtherLineIgnored_ExpressionFilters()
    {
        var releases = new[] { new PlatformRelease { Version = "8.5.2.1966" } };

        var result = PlatformUpdateCheckLogic.Check(
            new[] { "8.3.27.1688" },
            releases,
            "8.3.*",
            includeTestVersions: false);

        Assert.False(result.HasUpdate);
    }

    [Fact]
    public void Check_TestVersions_FilteredByOption()
    {
        var releases = new[]
        {
            new PlatformRelease { Version = "8.3.28.1400", IsTest = true },
            new PlatformRelease { Version = "8.3.27.2214", IsTest = false }
        };

        var withoutTests = PlatformUpdateCheckLogic.Check(
            new[] { "8.3.27.1688" }, releases, "8.3.*", includeTestVersions: false);
        var withTests = PlatformUpdateCheckLogic.Check(
            new[] { "8.3.27.1688" }, releases, "8.3.*", includeTestVersions: true);

        Assert.Equal(new[] { "8.3.27.2214" }, withoutTests.AvailableVersions);
        Assert.Equal(new[] { "8.3.28.1400", "8.3.27.2214" }, withTests.AvailableVersions);
    }

    [Fact]
    public void Check_MaxInstalledIsMaxMatching_IgnoredOtherLines()
    {
        // Установлена 8.5.x — по выражению 8.3.* установок нет, поэтому все версии
        // каталога 8.3.x считаются доступными.
        var releases = new[] { new PlatformRelease { Version = "8.3.27.2214" } };

        var result = PlatformUpdateCheckLogic.Check(
            new[] { "8.5.2.1966" },
            releases,
            "8.3.*",
            includeTestVersions: false);

        Assert.Null(result.MaxInstalledVersion);
        Assert.Equal(new[] { "8.3.27.2214" }, result.AvailableVersions);
    }

    // ---- Сводный список версий ----

    [Fact]
    public void FormatVersions_LimitsToListAndEllipsis()
    {
        Assert.Equal("", PlatformUpdateCheckLogic.FormatVersions(Array.Empty<string>()));
        Assert.Equal("1, 2", PlatformUpdateCheckLogic.FormatVersions(new[] { "1", "2" }));
        Assert.Equal(
            "1, 2, 3, 4, 5… (+2)",
            PlatformUpdateCheckLogic.FormatVersions(new[] { "1", "2", "3", "4", "5", "6", "7" }));
    }
}
