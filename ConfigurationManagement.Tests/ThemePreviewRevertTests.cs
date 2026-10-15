using Configuration_Management.Themes;
using Xunit;

namespace ConfigurationManagement.Tests;

/// <summary>
/// Тесты режима «предпросмотр → Применить/Отмена» выбора полноценной темы (issue #378):
/// <see cref="ThemePreviewState"/> — чистое состояние, которое использует
/// <see cref="Configuration_Management.ViewModels.MainViewModel"/> обеих платформ.
/// Предпросмотр не сохраняется до «Применить»; «Отмена» возвращает прежнюю тему.
/// </summary>
public sealed class ThemePreviewRevertTests
{
    [Fact]
    public void PreviewTheme_DoesNotSaveUntilCommit()
    {
        var state = new ThemePreviewState();
        state.Begin("light", "light");

        state.Preview("dark");

        // Предпросмотр активен, но сохранённый FullTheme не изменился.
        Assert.True(state.HasPreview);
        Assert.Equal("dark", state.PreviewedThemeId);
        Assert.Equal("light", state.SavedFullThemeId);

        // Только Commit фиксирует выбор.
        Assert.Equal("dark", state.Commit());
        Assert.Equal("dark", state.SavedFullThemeId);
        Assert.False(state.HasPreview);
    }

    [Fact]
    public void RevertTheme_ReturnsToPreviousSelection()
    {
        var state = new ThemePreviewState();
        state.Begin("light", "light");
        state.Preview("dark");

        // Отмена возвращает тему, которая была до входа в галерею.
        Assert.Equal("light", state.Revert());
        Assert.False(state.HasPreview);
        Assert.Equal("light", state.SavedFullThemeId);
    }

    [Fact]
    public void CommitTheme_PersistsAndApplies()
    {
        var state = new ThemePreviewState();
        state.Begin("light", "light");
        state.Preview("blue");

        Assert.Equal("blue", state.Commit());
        Assert.Equal("blue", state.SavedFullThemeId);
        Assert.False(state.HasPreview);
    }

    [Fact]
    public void Begin_ResetsPreviousPreview()
    {
        var state = new ThemePreviewState();
        state.Begin("light", "light");
        state.Preview("dark");

        // Повторный вход в галерею сбрасывает незавершённый предпросмотр.
        state.Begin("dark", "light");

        Assert.False(state.HasPreview);
        Assert.Equal("dark", state.ThemeIdBeforePreview);
        Assert.Equal("light", state.SavedFullThemeId);
    }

    [Fact]
    public void Commit_WithoutPreview_ReturnsNull()
    {
        var state = new ThemePreviewState();
        state.Begin("light", "light");

        Assert.Null(state.Commit());
        Assert.Equal("light", state.SavedFullThemeId);
    }

    [Fact]
    public void Revert_WithoutPreview_ReturnsThemeBeforePreview()
    {
        // Отмена без предпросмотра возвращает тему до входа в галерею (не null):
        // повторное применение безопасно и ничего не меняет.
        var state = new ThemePreviewState();
        state.Begin("light", "light");

        Assert.Equal("light", state.Revert());
        Assert.False(state.HasPreview);
    }

    [Fact]
    public void Preview_NormalizesEmptyIdToLight()
    {
        var state = new ThemePreviewState();
        state.Begin("light", "light");

        Assert.Equal("light", state.Preview(""));
        Assert.Equal("light", state.PreviewedThemeId);
    }

    [Fact]
    public void Begin_EmptyCurrentThemeFallsBackToLight()
    {
        var state = new ThemePreviewState();
        state.Begin("", "");

        Assert.Equal("light", state.ThemeIdBeforePreview);
        Assert.Equal("light", state.SavedFullThemeId);
    }
}