using Configuration_Management.Services;
using Xunit;

namespace ConfigurationManagement.Tests;

/// <summary>
/// Тесты имени сохранения одиночного дистрибутива обновления (issue #352, 0.3.12.6).
/// </summary>
public sealed class UpdateDownloadNamingTests
{
    private static string Identity(string s) => s;

    [Fact]
    public void BuildDownloadFileName_WithChoice_UsesOriginalStemAndExtension()
    {
        var name = UpdateDownloadNaming.BuildDownloadFileName(
            "УТ", "11.5.26.118", "Trade_11_5_26_118_updsetup.cf", Identity);

        Assert.Equal("УТ_11.5.26.118_Trade_11_5_26_118_updsetup.cf", name);
    }

    [Fact]
    public void BuildDownloadFileName_FullDistribution_DiffersFromUpdateDistribution()
    {
        var full = UpdateDownloadNaming.BuildDownloadFileName(
            "УТ", "11.5.26.118", "Trade_11_5_26_118.rar", Identity);
        var upd = UpdateDownloadNaming.BuildDownloadFileName(
            "УТ", "11.5.26.118", "Trade_11_5_26_118_updsetup.cf", Identity);

        Assert.NotEqual(full, upd);
    }

    [Fact]
    public void BuildDownloadFileName_NoChoice_FallsBackToZip()
    {
        var name = UpdateDownloadNaming.BuildDownloadFileName("УТ", "11.5.26.118", null, Identity);

        Assert.Equal("УТ_11.5.26.118.zip", name);
    }

    [Fact]
    public void BuildDownloadFileName_EmptyVersion_UsesUpdatePlaceholder()
    {
        var name = UpdateDownloadNaming.BuildDownloadFileName("УТ", "", "file.cf", Identity);

        Assert.Equal("УТ_update_file.cf", name);
    }
}
