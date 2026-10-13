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
    public void Merge_InstalledIsNewest_NoUpdateFlag_LowerLineHidden()
    {
        // Issue #361: версии ниже установленной линии не показываются.
        var matches = PlatformUpdateMatcher.Merge(
            new[] { "8.3.27.2214" },
            Releases("8.3.27.2214", "8.3.26.1890"));

        Assert.Single(matches); // только установленная; 8.3.26.1890 — ниже линии, скрыта
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
        // Issue #361: показывается только максимум линии 8.3.27 (не все её версии).
        var matches = PlatformUpdateMatcher.Merge(
            new[] { "8.3.26.1890" },
            Releases("8.3.27.2214", "8.3.27.1688"));

        Assert.Equal(2, matches.Count); // установленная + максимум линии 8.3.27
        var installed = matches.Single(m => m.Version == "8.3.26.1890");
        Assert.True(installed.IsInstalled);
        Assert.True(installed.HasUpdate);
        Assert.Equal("8.3.27.2214", installed.AvailableVersion);

        var availableOnly = matches.Where(m => m.Version != "8.3.26.1890").ToList();
        Assert.All(availableOnly, m => Assert.False(m.IsInstalled));
        Assert.All(availableOnly, m => Assert.False(m.HasUpdate));
        Assert.All(availableOnly, m => Assert.Null(m.AvailableVersion));
        Assert.Equal("8.3.27.2214", availableOnly.Single().Version); // только максимум линии
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

    // --- Merge: логика issue #361 (закрепления баз, линии, фильтр) ---

    [Fact]
    public void Merge_FourSegmentBasePin_DisablesUpdateCheck()
    {
        // База закреплена за 8.3.26.11540 (4 сегмента) — обновление не проверяется,
        // даже если в каталоге есть 8.3.27.x.
        var bases = new[] { NewBase("8.3.26.11540") };
        var matches = PlatformUpdateMatcher.Merge(
            new[] { "8.3.26.1656" },
            Releases("8.3.27.2214", "8.3.26.1890"),
            bases);

        var installed = matches.Single(m => m.Version == "8.3.26.1656");
        Assert.True(installed.IsInstalled);
        Assert.False(installed.HasUpdate); // закрепление 4 сегмента → проверка отключена
        Assert.Null(installed.AvailableVersion);
    }

    [Fact]
    public void Merge_ThreeSegmentBasePin_OnlyNewerLineCountsAsUpdate()
    {
        // База указывает 8.3.26 (3 сегмента): 8.3.26.1890 (та же линия) — НЕ обновление;
        // 8.3.27.2214 (новая линия) — обновление.
        var bases = new[] { NewBase("8.3.26") };
        var matches = PlatformUpdateMatcher.Merge(
            new[] { "8.3.26.1656" },
            Releases("8.3.27.2214", "8.3.26.1890"),
            bases);

        var installed = matches.Single(m => m.Version == "8.3.26.1656");
        Assert.True(installed.IsInstalled);
        Assert.True(installed.HasUpdate);
        Assert.Equal("8.3.27.2214", installed.AvailableVersion); // цель — новая линия
    }

    [Fact]
    public void Merge_ThreeSegmentBasePin_NoNewerLine_NoUpdate()
    {
        // База 8.3.26 (3 сегмента), каталог содержит только 8.3.26.x — обновления нет
        // (сборки в той же линии не считаются обновлением по правилу 3 сегментов).
        var bases = new[] { NewBase("8.3.26") };
        var matches = PlatformUpdateMatcher.Merge(
            new[] { "8.3.26.1656" },
            Releases("8.3.26.1890", "8.3.26.1656"),
            bases);

        var installed = matches.Single(m => m.Version == "8.3.26.1656");
        Assert.True(installed.IsInstalled);
        Assert.False(installed.HasUpdate); // та же линия 8.3.26 — не обновление
        Assert.Null(installed.AvailableVersion);
    }

    [Fact]
    public void Merge_NoBase_FullComparison_WithinLineCountsAsUpdate()
    {
        // Без баз — прежнее поведение: новая сборка в той же линии (8.3.26.1890 > 8.3.26.1656)
        // считается обновлением.
        var matches = PlatformUpdateMatcher.Merge(
            new[] { "8.3.26.1656" },
            Releases("8.3.26.1890", "8.3.26.1656"));

        var installed = matches.Single(m => m.Version == "8.3.26.1656");
        Assert.True(installed.IsInstalled);
        Assert.True(installed.HasUpdate);
        Assert.Equal("8.3.26.1890", installed.AvailableVersion);
    }

    [Fact]
    public void Merge_HidesMaximalPerLine_NotAllHistoricalVersions()
    {
        // Issue #361: из линии 8.3.27 показывается только её максимум, не все версии.
        var matches = PlatformUpdateMatcher.Merge(
            new[] { "8.3.25.1000" },
            Releases("8.3.27.2214", "8.3.27.1688", "8.3.26.1890", "8.3.25.1000"));

        // Установленная 8.3.25.1000 + максимумы линий 8.3.26 и 8.3.27 (не все версии).
        Assert.Equal(3, matches.Count);
        Assert.Contains(matches, m => m.Version == "8.3.25.1000" && m.IsInstalled);
        Assert.Contains(matches, m => m.Version == "8.3.26.1890" && !m.IsInstalled);
        Assert.Contains(matches, m => m.Version == "8.3.27.2214" && !m.IsInstalled);
        Assert.DoesNotContain(matches, m => m.Version == "8.3.27.1688"); // не максимум линии
    }

    [Fact]
    public void Merge_HidesLinesBelowAllInstalled()
    {
        // Issue #361: версии ниже установленной линии не показываются.
        var matches = PlatformUpdateMatcher.Merge(
            new[] { "8.3.27.1688" },
            Releases("8.3.27.2214", "8.3.26.1890", "8.3.25.1000"));

        Assert.DoesNotContain(matches, m => m.Version == "8.3.26.1890");
        Assert.DoesNotContain(matches, m => m.Version == "8.3.25.1000");
        Assert.Contains(matches, m => m.Version == "8.3.27.1688" && m.IsInstalled);
        Assert.Contains(matches, m => m.Version == "8.3.27.2214" && !m.IsInstalled);
    }

    [Fact]
    public void Merge_SameLineMaxInstalled_NoDuplicateAvailableRow()
    {
        // Если максимум линии уже установлен — отдельная строка «Доступна» не создаётся.
        var matches = PlatformUpdateMatcher.Merge(
            new[] { "8.3.27.2214" },
            Releases("8.3.27.2214", "8.3.27.1688"));

        Assert.Single(matches);
        Assert.True(matches[0].IsInstalled);
        Assert.Equal("8.3.27.2214", matches[0].Version);
    }

    // --- Merge: issue #361 (0.3.12.11) — установленная версия и нормализация пинов ---

    [Fact]
    public void Merge_InstalledVersionEqualsAvailable_NoUpdateFlag()
    {
        // Установленная версия равна доступной (точный дубль каталога) — «Установлена».
        var matches = PlatformUpdateMatcher.Merge(
            new[] { "8.3.26.1656" },
            Releases("8.3.26.1656"));

        var installed = matches.Single(m => m.Version == "8.3.26.1656");
        Assert.True(installed.IsInstalled);
        Assert.False(installed.HasUpdate);
        Assert.Null(installed.AvailableVersion);
    }

    [Fact]
    public void Merge_InstalledIsLineMax_NoNewerLines_NoUpdateFlag()
    {
        // Установленная — максимум своей линии без закрепления; каталог не содержит
        // новее ни в этой линии, ни в других — HasUpdate=false («Установлена»).
        var matches = PlatformUpdateMatcher.Merge(
            new[] { "8.3.26.1656" },
            Releases("8.3.26.1656", "8.3.25.900"));

        var installed = matches.Single(m => m.Version == "8.3.26.1656" && m.IsInstalled);
        Assert.False(installed.HasUpdate);
        Assert.Null(installed.AvailableVersion);
    }

    [Fact]
    public void Merge_NewerCatalogVersionAlreadyInstalled_NoFalseUpdateForLowerInstalled()
    {
        // Дефект A (0.3.12.11): кандидат newest не берётся из уже установленных версий —
        // 8.3.27.2214 есть и в каталоге, и среди установленных, поэтому для 8.3.26.1656
        // ложного «Есть обновление» на неё нет.
        var matches = PlatformUpdateMatcher.Merge(
            new[] { "8.3.26.1656", "8.3.27.2214" },
            Releases("8.3.27.2214"));

        var lower = matches.Single(m => m.Version == "8.3.26.1656");
        Assert.True(lower.IsInstalled);
        Assert.False(lower.HasUpdate);
        Assert.Null(lower.AvailableVersion);

        var newest = matches.Single(m => m.Version == "8.3.27.2214");
        Assert.True(newest.IsInstalled);
        Assert.False(newest.HasUpdate);
    }

    [Fact]
    public void Merge_FourSegmentPinWithArchSuffix_DisablesUpdateCheck()
    {
        // Дефект B (0.3.12.11): пин «8.3.26.1656 (64)» нормализуется до 4 сегментов
        // → режим Disabled → «Установлена», хотя в каталоге есть 8.3.27.x.
        var bases = new[] { NewBase("8.3.26.1656 (64)") };
        var matches = PlatformUpdateMatcher.Merge(
            new[] { "8.3.26.1656" },
            Releases("8.3.27.2214", "8.3.26.1890"),
            bases);

        var installed = matches.Single(m => m.Version == "8.3.26.1656");
        Assert.True(installed.IsInstalled);
        Assert.False(installed.HasUpdate);
        Assert.Null(installed.AvailableVersion);
    }

    [Fact]
    public void Merge_ThreeSegmentPinWithArchSuffix_OnlyNewerLineCountsAsUpdate()
    {
        // Дефект B (0.3.12.11): пин «8.3.26 (64)» нормализуется до «8.3.26» → режим
        // ThreePart: сборка 8.3.26.1890 той же линии — не обновление, 8.3.27.2214 — обновление.
        var bases = new[] { NewBase("8.3.26 (64)") };
        var matches = PlatformUpdateMatcher.Merge(
            new[] { "8.3.26.1656" },
            Releases("8.3.27.2214", "8.3.26.1890"),
            bases);

        var installed = matches.Single(m => m.Version == "8.3.26.1656");
        Assert.True(installed.IsInstalled);
        Assert.True(installed.HasUpdate);
        Assert.Equal("8.3.27.2214", installed.AvailableVersion);
    }

    // --- Merge: issue #361 (0.3.12.27) — цель обновления относительно МАКСИМАЛЬНОЙ
    // установленной линии («искать только версии больше той максимальной, что сейчас
    // есть на компе по 3-м цифрам») ---

    [Fact]
    public void Merge_ThreePart_MaxInstalledLineHigher_NoUpdateForLowerLines()
    {
        // Сценарий из debug-лога 7OH: установлены 8.3.26.1656 и 8.5.4.1878 (максимальная
        // линия 8.5.4), база закреплена за 8.3.26 (3 сегмента), каталог содержит только
        // 8.3.x. Обновления нет: в каталоге нет версий с первыми 3 сегментами > 8.5.4.
        var bases = new[] { NewBase("8.3.26") };
        var matches = PlatformUpdateMatcher.Merge(
            new[] { "8.3.26.1656", "8.5.4.1878" },
            Releases("8.3.27.2214", "8.3.26.1890"),
            bases);

        var lower = matches.Single(m => m.Version == "8.3.26.1656");
        Assert.True(lower.IsInstalled);
        Assert.False(lower.HasUpdate); // 8.3.27.2214 < максимальной установленной линии 8.5.4
        Assert.Null(lower.AvailableVersion);

        var max = matches.Single(m => m.Version == "8.5.4.1878");
        Assert.True(max.IsInstalled);
        Assert.False(max.HasUpdate);
        Assert.Null(max.AvailableVersion);
    }

    [Fact]
    public void Merge_ThreePart_MaxInstalledLineHigher_UpdateToNewerThanMax()
    {
        // Если в каталоге появилась версия с первыми 3 сегментами строго больше
        // максимальной установленной линии (8.5.4) — она становится обновлением
        // для всех установленных версий ниже неё.
        var bases = new[] { NewBase("8.3.26") };
        var matches = PlatformUpdateMatcher.Merge(
            new[] { "8.3.26.1656", "8.5.4.1878" },
            Releases("8.3.27.2214", "8.6.0.100"),
            bases);

        var lower = matches.Single(m => m.Version == "8.3.26.1656");
        Assert.True(lower.HasUpdate);
        Assert.Equal("8.6.0.100", lower.AvailableVersion);

        var max = matches.Single(m => m.Version == "8.5.4.1878");
        Assert.True(max.HasUpdate);
        Assert.Equal("8.6.0.100", max.AvailableVersion);
    }

    [Fact]
    public void Merge_ThreePart_NewerBuildOfInstalledMaxLine_NotUpdate()
    {
        // Установлены 8.3.26.1656 и 8.3.27.2214 (максимальная линия 8.3.27), базы
        // закреплены за 8.3.26 и 8.3.27 (3 сегмента). В каталоге есть 8.3.27.2325
        // (та же линия 8.3.27, не установлена) — она НЕ обновление: её первые 3 сегмента
        // не больше максимальной установленной линии 8.3.27.
        var bases = new[] { NewBase("8.3.26"), NewBase("8.3.27") };
        var matches = PlatformUpdateMatcher.Merge(
            new[] { "8.3.26.1656", "8.3.27.2214" },
            Releases("8.3.27.2325"),
            bases);

        var lower = matches.Single(m => m.Version == "8.3.26.1656");
        Assert.False(lower.HasUpdate);
        Assert.Null(lower.AvailableVersion);

        var max = matches.Single(m => m.Version == "8.3.27.2214");
        Assert.False(max.HasUpdate);
        Assert.Null(max.AvailableVersion);
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

    // --- Merge + тестовые сборки (issue #364) ---

    [Fact]
    public void Merge_TestReleases_HiddenByDefault()
    {
        // issue #364: без опции «Учитывать тестовые» предрелизные сборки не показываются
        // и не участвуют в расчёте обновления.
        var matches = PlatformUpdateMatcher.Merge(
            new[] { "8.3.27.1688" },
            Mixed(("8.3.27.2214", false), ("8.3.28.100", true)));

        Assert.Equal(new[] { "8.3.27.2214", "8.3.27.1688" }, matches.Select(m => m.Version));
        Assert.All(matches, m => Assert.NotEqual("8.3.28.100", m.Version));
    }

    [Fact]
    public void Merge_IncludeTestVersions_AddsTestRowsWithoutUpdateFlag()
    {
        // issue #364: при включённой опции тестовая сборка появляется строкой «Доступна»,
        // но НИКОГДА не помечается «Есть обновление».
        var matches = PlatformUpdateMatcher.Merge(
            new[] { "8.3.27.1688" },
            Mixed(("8.3.27.2214", false), ("8.3.28.100", true)),
            includeTestVersions: true);

        Assert.Equal(new[] { "8.3.28.100", "8.3.27.2214", "8.3.27.1688" }, matches.Select(m => m.Version));

        var test = matches.Single(m => m.Version == "8.3.28.100");
        Assert.False(test.IsInstalled);
        Assert.False(test.HasUpdate);
        Assert.Null(test.AvailableVersion);

        var installed = matches.Single(m => m.Version == "8.3.27.1688");
        Assert.True(installed.IsInstalled);
        Assert.True(installed.HasUpdate);
        Assert.Equal("8.3.27.2214", installed.AvailableVersion);
    }

    [Fact]
    public void Merge_IncludeTestVersions_MaxOfLineStaysRelease()
    {
        // issue #364: тестовая сборка новее релизной в той же линии — целью обновления
        // остаётся релизный максимум линии (8.3.27.2214), а не 8.3.27.2500.
        var matches = PlatformUpdateMatcher.Merge(
            new[] { "8.3.27.1688" },
            Mixed(("8.3.27.2500", true), ("8.3.27.2214", false)),
            includeTestVersions: true);

        var installed = matches.Single(m => m.Version == "8.3.27.1688");
        Assert.True(installed.HasUpdate);
        Assert.Equal("8.3.27.2214", installed.AvailableVersion);
        Assert.False(matches.Single(m => m.Version == "8.3.27.2500").HasUpdate);
    }

    [Fact]
    public void Merge_InstalledTestVersion_NotDuplicatedAsTestRow()
    {
        // issue #364: установленная тестовая сборка показывается одной строкой
        // «Установлена» (без дубля «Доступна»).
        var matches = PlatformUpdateMatcher.Merge(
            new[] { "8.3.28.100" },
            Mixed(("8.3.28.100", true), ("8.3.27.2214", false)),
            includeTestVersions: true);

        var installed = matches.Single(m => m.Version == "8.3.28.100");
        Assert.True(installed.IsInstalled);
        Assert.Single(matches, m => m.Version == "8.3.28.100");
    }

    [Fact]
    public void Merge_MinInstalledLine_ComparesNumerically()
    {
        // issue #361: минимальная установленная линия считается ЧИСЛОВЫМ сравнением.
        // Установлены 8.3.9 и 8.3.27 — линия 8.3.26 выше минимальной (8.3.9) и должна
        // показываться (раньше строковый Min давал «8.3.27» и ошибочно скрывал 8.3.26).
        var matches = PlatformUpdateMatcher.Merge(
            new[] { "8.3.9", "8.3.27.2214" },
            Releases("8.3.27.2214", "8.3.26.1890", "8.3.8.100"));

        Assert.Contains(matches, m => m.Version == "8.3.26.1890"); // линия 8.3.26 видна
        Assert.DoesNotContain(matches, m => m.Version == "8.3.8.100"); // линия 8.3.8 ниже 8.3.9 — скрыта
    }

    /// <summary>Создаёт релизы с явным признаком тестовой сборки (issue #364).</summary>
    private static List<PlatformRelease> Mixed(params (string Version, bool IsTest)[] items)
        => items.Select(t => new PlatformRelease
        {
            Version = t.Version,
            VersionFilesUrl = $"/version_files?nick=Platform83&ver={t.Version}",
            IsTest = t.IsTest,
        }).ToList();

    /// <summary>Создаёт базу с указанной версией платформы.</summary>
    private static Infobase NewBase(string? platformVersion)
        => new() { Id = "id", Name = "База", PlatformVersion = platformVersion ?? string.Empty };
}