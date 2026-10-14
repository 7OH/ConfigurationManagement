using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace ConfigurationManagement.Tests;

/// <summary>
/// Полнота наборов иконок тем (0.3.12.59): словари IconsMaterial/MacOS/WindowsXp/Glass
/// (.xaml и .axaml) сгенерированы tools/icongen и должны покрывать КАЖДЫЙ ключ
/// геометрии базового набора (Icons.xaml / Icons.axaml) и — в WPF — каждый
/// используемый в разметке и коде вид значка materialDesign:PackIcon (Pack.&lt;Kind&gt;).
/// Иначе тема молча показывала бы Fluent-иконки вперемешку со своими.
/// Тесты читают файлы проекта (как SettingsWindowXamlResourcesTests) и не требуют UI.
/// </summary>
public sealed class IconSetCoverageTests
{
    private static readonly string[] GeneratedSets = { "Material", "MacOS", "WindowsXp", "Glass" };

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "Configuration Management", "Themes")) &&
                Directory.Exists(Path.Combine(dir.FullName, "ConfigurationManagement.Tests")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("Не найден корень репозитория");
    }

    private static string Themes(string file) =>
        File.ReadAllText(Path.Combine(RepoRoot(), "Configuration Management", "Themes", file));

    private static Dictionary<string, string> Geometries(string file)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Match m in Regex.Matches(Themes(file),
                     "<(?:Stream)?Geometry x:Key=\"([^\"]+)\">([^<]+)</(?:Stream)?Geometry>"))
            result[m.Groups[1].Value] = m.Groups[2].Value.Trim();
        return result;
    }

    private static IEnumerable<string> BaseKeys(string file) =>
        Geometries(file).Keys.Where(k => k.StartsWith("Icon", StringComparison.Ordinal));

    private static IReadOnlyCollection<string> UsedPackKinds()
    {
        var kinds = new SortedSet<string>(StringComparer.Ordinal);
        var root = Path.Combine(RepoRoot(), "Configuration Management");
        foreach (var file in Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories))
        {
            if (file.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar) ||
                file.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar))
                continue;
            if (file.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase))
            {
                foreach (Match m in Regex.Matches(File.ReadAllText(file),
                             "PackIcon[^>]*?\\sKind=\"([A-Za-z0-9]+)\"", RegexOptions.Singleline))
                    kinds.Add(m.Groups[1].Value);
            }
            else if (file.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            {
                foreach (Match m in Regex.Matches(File.ReadAllText(file), "PackIconKind\\.([A-Za-z0-9]+)"))
                    kinds.Add(m.Groups[1].Value);
            }
        }
        return kinds;
    }

    public static IEnumerable<object[]> SetsAndPlatforms() =>
        from set in GeneratedSets
        from ext in new[] { "xaml", "axaml" }
        select new object[] { set, ext };

    [Theory]
    [MemberData(nameof(SetsAndPlatforms))]
    public void EveryBaseIconKey_HasVariantInSet(string set, string ext)
    {
        var dict = Geometries($"Icons{set}.{ext}");
        var missing = BaseKeys($"Icons.{ext}")
            .Where(k => !dict.ContainsKey(k + "." + set))
            .ToList();
        Assert.True(missing.Count == 0,
            $"В Icons{set}.{ext} нет вариантов для: {string.Join(", ", missing)}");
    }

    [Theory]
    [InlineData("Material")]
    [InlineData("MacOS")]
    [InlineData("WindowsXp")]
    [InlineData("Glass")]
    [InlineData("Outline")]
    public void EveryUsedPackIconKind_HasVariantInSet(string set)
    {
        var dict = Geometries($"Icons{set}.xaml");
        var missing = UsedPackKinds().Where(k => !dict.ContainsKey($"Pack.{k}.{set}")).ToList();
        Assert.True(missing.Count == 0,
            $"В Icons{set}.xaml нет значков PackIcon: {string.Join(", ", missing)}");
    }

    [Theory]
    [MemberData(nameof(SetsAndPlatforms))]
    public void WindowGlyphs_ArePresentInSet(string set, string ext)
    {
        var dict = Geometries($"Icons{set}.{ext}");
        foreach (var glyph in new[] { "GlyphWindowMinimize", "GlyphWindowMaximize", "GlyphWindowRestore", "GlyphWindowClose" })
            Assert.True(dict.ContainsKey(glyph + "." + set), $"Icons{set}.{ext}: нет глифа {glyph}.{set}");
    }

    [Fact]
    public void GlassSet_HasTranslucentBackLayers()
    {
        // Duotone-набор Glass: у иконок есть фоновый слой *.Back.Glass.
        var dict = Geometries("IconsGlass.xaml");
        Assert.Contains("IconSettings.Back.Glass", dict.Keys);
        Assert.Contains("Pack.Cog.Back.Glass", dict.Keys);
        Assert.True(dict.Keys.Count(k => k.Contains(".Back.Glass", StringComparison.Ordinal)) > 100);
    }

    [Theory]
    [MemberData(nameof(SetsAndPlatforms))]
    public void GeneratedPaths_AreWellFormed(string set, string ext)
    {
        // Путь — мини-язык геометрии WPF/Avalonia: необязательный префикс правила
        // заливки «F1», затем команды и числа. Мусор (теги, буквы вне команд) ломал
        // бы загрузку словаря целиком.
        var valid = new Regex(@"^(F[01]\s+)?[Mm][\s\dMmLlHhVvCcSsQqTtAaZz.,eE+\-]*$");
        foreach (var (key, data) in Geometries($"Icons{set}.{ext}"))
            Assert.True(valid.IsMatch(data), $"Icons{set}.{ext}: некорректный путь у {key}");
    }
}
