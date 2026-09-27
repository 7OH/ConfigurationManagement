using Configuration_Management.Services;
using Xunit;

namespace ConfigurationManagement.Tests;

/// <summary>
/// Тесты разбора командной строки процесса 1С для инспектора процессов
/// (<see cref="ProcessCommandLineParser"/>): режим запуска, пользователь (/N)
/// и краткая строка подключения (/F, /S).
/// </summary>
public sealed class ProcessInspectorParsingTests
{
    [Theory]
    [InlineData(@"""C:\Program Files\1cv8\1cv8c.exe"" ENTERPRISE /F C:\Bases\ib", OneCProcessLaunchMode.Enterprise)]
    [InlineData(@"""C:\Program Files\1cv8\1cv8c.exe"" /F C:\Bases\ib /N Иван", OneCProcessLaunchMode.Enterprise)]
    [InlineData(@"""C:\1cv8\1cv8.exe"" DESIGNER /F C:\Bases\ib", OneCProcessLaunchMode.Configurator)]
    [InlineData(@"""C:\1cv8\1cv8.exe"" CONFIG /S server1\trade", OneCProcessLaunchMode.Configurator)]
    [InlineData(@"""C:\1cv8\1cv8c.exe"" ENTERPRISE /Execute C:\Ext.epf /F C:\Bases\ib", OneCProcessLaunchMode.Service)]
    [InlineData(@"""C:\1cv8\1cv8c.exe"" ENTERPRISE /C ""ВыполнитьКоманду"" /F C:\Bases\ib", OneCProcessLaunchMode.Service)]
    [InlineData(@"""C:\1cv8\1cv8.exe"" DESIGNER /C ""Загрузить"" /F C:\Bases\ib", OneCProcessLaunchMode.Configurator)] // приоритет конфигуратора
    [InlineData("", OneCProcessLaunchMode.Enterprise)]
    [InlineData(null, OneCProcessLaunchMode.Enterprise)]
    public void DetectMode_ReturnsExpectedMode(string? commandLine, OneCProcessLaunchMode expected)
    {
        Assert.Equal(expected, ProcessCommandLineParser.DetectMode(commandLine));
    }

    [Theory]
    [InlineData(@"""1cv8c.exe"" ENTERPRISE /F C:\Bases\ib /N Иван", "Иван")]
    [InlineData(@"""1cv8c.exe"" ENTERPRISE /F C:\Bases\ib /N""Иван""", "Иван")]
    [InlineData(@"""1cv8c.exe"" ENTERPRISE /F C:\Bases\ib /N Иван /P 123", "Иван")]
    [InlineData(@"""1cv8c.exe"" ENTERPRISE /F C:\Bases\ib /N Иван Иванович /DisableStartupMessages", "Иван Иванович")]
    [InlineData(@"""1cv8c.exe"" ENTERPRISE /F C:\Bases\ib", null)]
    [InlineData(null, null)]
    public void ExtractUser_ReturnsUserOrNull(string? commandLine, string? expected)
    {
        Assert.Equal(expected, ProcessCommandLineParser.ExtractUser(commandLine));
    }

    [Theory]
    [InlineData(@"""1cv8c.exe"" ENTERPRISE /F ""C:\Bases\Моя база"" /N Иван", "/F C:\\Bases\\Моя база")]
    [InlineData(@"""1cv8c.exe"" ENTERPRISE /FC:\Bases\ib", "/F C:\\Bases\\ib")]
    [InlineData(@"""1cv8c.exe"" ENTERPRISE /S ""server1\trade"" /N Иван", "/S server1\\trade")]
    [InlineData(@"""1cv8c.exe"" ENTERPRISE /Sserver1\trade", "/S server1\\trade")]
    [InlineData(@"""1cv8c.exe"" ENTERPRISE /N Иван", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void ExtractConnectionString_ReturnsShortConnection(string? commandLine, string? expected)
    {
        Assert.Equal(expected, ProcessCommandLineParser.ExtractConnectionString(commandLine));
    }

    [Fact]
    public void SlashOutKey_IsNotExtractedAsConnection()
    {
        // /Out — не /F и не /S: без ключей подключения строка подключения не извлекается.
        Assert.Null(ProcessCommandLineParser.ExtractConnectionString(
            @"""1cv8.exe"" DESIGNER /Out C:\log.txt /DisableStartupMessages"));
    }

    [Fact]
    public void UserValue_StopsBeforeNextKey()
    {
        var user = ProcessCommandLineParser.ExtractUser(
            @"""1cv8.exe"" DESIGNER /F C:\Bases\ib /N Иван /DisableStartupMessages");
        Assert.Equal("Иван", user);
    }
}