using Configuration_Management.Services;
using Xunit;

namespace ConfigurationManagement.Tests;

/// <summary>
/// Аргументы пакетных операций конфигуратора (issue #365): ветка
/// /DumpConfigToFiles отсутствовала в Windows-RunDesignerBatch — «Обозреватель
/// метаданных» всегда получал «Операция отклонена: пустой или недопустимый путь».
/// </summary>
public class DesignerBatchArgumentsTests
{
    [Fact]
    public void DumpConfigToFiles_ReturnsKeyWithQuotedPath()
    {
        Assert.Equal(
            "/DumpConfigToFiles\"C:\\temp\\cm_dump\\dump\"",
            OneCLauncher.DumpConfigToFilesArgument(@"C:\temp\cm_dump\dump"));
    }

    [Fact]
    public void DumpConfigToFiles_PathWithSpaces_IsAllowed()
    {
        Assert.Equal(
            "/DumpConfigToFiles\"C:\\temp\\dump dir\\cfg\"",
            OneCLauncher.DumpConfigToFilesArgument(@"C:\temp\dump dir\cfg"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("C:\\bad\"quotes\\dump")]
    public void DumpConfigToFiles_UnsafeOrEmpty_ReturnsEmpty(string? path)
    {
        Assert.Equal("", OneCLauncher.DumpConfigToFilesArgument(path));
    }
}