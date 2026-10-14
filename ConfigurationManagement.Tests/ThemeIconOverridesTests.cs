using System.Collections.Generic;
using System.Linq;
using Configuration_Management.Themes;
using Xunit;

namespace ConfigurationManagement.Tests;

/// <summary>
/// Живое переключение набора иконок (0.3.12.60): чистая логика переопределений базовых
/// ключей (<see cref="ThemeFeatureMap.IconOverrides"/>), ключей значков PackIcon
/// (<see cref="ThemeFeatureMap.PackIconKey"/>), «блика» Windows XP и состояния
/// <see cref="IconSetState"/>.
/// </summary>
public sealed class ThemeIconOverridesTests
{
    private static readonly HashSet<string> Available = new()
    {
        "IconAdd.Glass", "IconAdd.Back.Glass", "IconFolder.Glass",
        "IconAdd.Material", "GlyphWindowClose.Material"
    };

    [Fact]
    public void Fluent_HasNoOverrides()
    {
        var result = ThemeFeatureMap.IconOverrides(IconSet.Fluent, new[] { "IconAdd", "IconFolder" }, Available.Contains);
        Assert.Empty(result);
    }

    [Fact]
    public void Glass_OverridesKeyAndBackLayer()
    {
        var result = ThemeFeatureMap.IconOverrides(IconSet.Glass,
            new[] { "IconAdd", "IconFolder", "IconMissing" }, Available.Contains);
        var map = result.ToDictionary(p => p.Key, p => p.Value);
        Assert.Equal("IconAdd.Glass", map["IconAdd"]);
        Assert.Equal("IconAdd.Back.Glass", map["IconAdd.Back"]);
        Assert.Equal("IconFolder.Glass", map["IconFolder"]);
        Assert.False(map.ContainsKey("IconFolder.Back"));
        Assert.False(map.ContainsKey("IconMissing"));
    }

    [Fact]
    public void Material_OverridesWindowGlyphs()
    {
        var result = ThemeFeatureMap.IconOverrides(IconSet.Material,
            new[] { "IconAdd", "GlyphWindowClose" }, Available.Contains);
        Assert.Contains(result, p => p.Key == "GlyphWindowClose" && p.Value == "GlyphWindowClose.Material");
    }

    [Theory]
    [InlineData("Cog", IconSet.Material, false, "Pack.Cog.Material")]
    [InlineData("Cog", IconSet.Glass, true, "Pack.Cog.Back.Glass")]
    [InlineData("Folder", IconSet.WindowsXp, false, "Pack.Folder.WindowsXp")]
    [InlineData("Folder", IconSet.Outline, false, "Pack.Folder.Outline")]
    public void PackIconKey_UsesSetSuffix(string kind, IconSet set, bool back, string expected)
        => Assert.Equal(expected, ThemeFeatureMap.PackIconKey(kind, set, back));

    [Fact]
    public void PackIconKey_FluentUsesNativeGeometry()
        => Assert.Null(ThemeFeatureMap.PackIconKey("Cog", IconSet.Fluent));

    [Fact]
    public void Gloss_OnlyForWindowsXp()
    {
        Assert.NotNull(ThemeFeatureMap.IconGloss(IconSet.WindowsXp));
        foreach (var set in new[] { IconSet.Fluent, IconSet.Outline, IconSet.Material, IconSet.MacOS, IconSet.Glass })
            Assert.Null(ThemeFeatureMap.IconGloss(set));
    }

    [Fact]
    public void IconSetState_RaisesChangeOnlyWhenSetChanges()
    {
        var state = IconSetState.Instance;
        var original = state.Current;
        try
        {
            state.Current = IconSet.Fluent;
            var raised = 0;
            void Handler(object? s, System.ComponentModel.PropertyChangedEventArgs e) => raised++;
            state.PropertyChanged += Handler;
            state.Current = IconSet.Glass;
            state.Current = IconSet.Glass;
            state.Current = IconSet.MacOS;
            state.PropertyChanged -= Handler;
            Assert.Equal(2, raised);
        }
        finally
        {
            state.Current = original;
        }
    }
}
