using Configuration_Management.Models;
using Configuration_Management.Services;
using Xunit;

namespace ConfigurationManagement.Tests;

/// <summary>
/// Тесты сопоставления командной строки процесса 1С с информационной базой
/// (индикатор «база сейчас запущена», <see cref="RunningInfobaseMatcher"/>).
/// </summary>
public sealed class RunningInfobaseMatcherTests
{
    private static Infobase FileBase(string path) => new()
    {
        Id = "f1",
        Name = "Файловая",
        Connection = new ConnectionSettings { Type = ConnectionType.File, FilePath = path }
    };

    private static Infobase ServerBase(string server, string dbName) => new()
    {
        Id = "s1",
        Name = "Серверная",
        Connection = new ConnectionSettings
        {
            Type = ConnectionType.ClientServer,
            Server = server,
            DatabaseName = dbName
        }
    };

    [Theory]
    [InlineData(@"""C:\Program Files\1cv8\1cv8c.exe"" ENTERPRISE /F ""C:\Bases\Моя база"" /N Иван", true)]
    [InlineData(@"""C:\Program Files\1cv8\1cv8.exe"" DESIGNER /F""C:\Bases\Моя база"" /N Иван", true)]
    [InlineData(@"""C:\Program Files\1cv8\1cv8c.exe"" ENTERPRISE /FC:\Bases\Моя база", true)]
    [InlineData(@"""C:\1cv8\1cv8c.exe"" ENTERPRISE /F ""c:\bases\МОЯ БАЗА\ """, true)] // регистр и хвост-разделитель
    [InlineData(@"""C:\1cv8\1cv8c.exe"" ENTERPRISE /S server1\trade /N Иван", false)] // другая база
    [InlineData(@"""C:\1cv8\1cv8c.exe"" ENTERPRISE", false)]                          // без ключа /F
    [InlineData("", false)]
    public void FileBase_MatchesBySlashF(string commandLine, bool expected)
    {
        Assert.Equal(expected, RunningInfobaseMatcher.MatchesCommandLine(FileBase(@"C:\Bases\Моя база"), commandLine));
    }

    [Theory]
    [InlineData(@"""C:\Program Files\1cv8\1cv8c.exe"" ENTERPRISE /S ""server1\trade"" /N Иван", true)]
    [InlineData(@"""C:\Program Files\1cv8\1cv8.exe"" DESIGNER /Sserver1\trade", true)]
    [InlineData(@"""C:\1cv8\1cv8c.exe"" ENTERPRISE /F ""C:\Bases\ib""", false)]
    [InlineData(@"""C:\1cv8\1cv8c.exe"" ENTERPRISE /S server2\trade", false)] // другой сервер
    [InlineData(@"""C:\1cv8\1cv8c.exe"" ENTERPRISE /S server1\other", false)] // другая база на том же сервере
    public void ServerBase_MatchesBySlashS(string commandLine, bool expected)
    {
        Assert.Equal(expected, RunningInfobaseMatcher.MatchesCommandLine(ServerBase("server1", "trade"), commandLine));
    }

    [Theory]
    [InlineData(@"""1cv8c.exe"" ENTERPRISE /S ""server1\trade"" /N Иван", true)]
    [InlineData(@"""1cv8c.exe"" ENTERPRISE /S server1\TRADE", true)]                 // регистр имени базы
    [InlineData(@"""1cv8c.exe"" ENTERPRISE /S ""server1\База с пробелами""", true)]  // кавычки и пробелы в имени
    [InlineData(@"""1cv8c.exe"" ENTERPRISE /S ""server2\trade""", false)]            // другой сервер
    [InlineData(@"""1cv8c.exe"" ENTERPRISE /S ""server1\other""", false)]            // другая база
    [InlineData(@"""1cv8c.exe"" ENTERPRISE", false)]                                 // нет ключа /S
    public void ServerBase_WithPortInAddress_MatchesSlashS(string commandLine, bool expected)
    {
        // issue #342: у базы в списке адрес сервера с портом («server1:1541»), а командная
        // строка процесса содержит «/S server1\БД» без порта — раньше это не сопоставлялось.
        Assert.Equal(expected, RunningInfobaseMatcher.MatchesCommandLine(
            ServerBase("server1:1541", commandLine.Contains("База с пробелами") ? "База с пробелами" : "trade"),
            commandLine));
    }

    [Fact]
    public void WebBase_NeverMatches()
    {
        var web = new Infobase
        {
            Connection = new ConnectionSettings { Type = ConnectionType.WebServer, WebUrl = "http://srv/base" }
        };

        Assert.False(RunningInfobaseMatcher.MatchesCommandLine(web, @"""1cv8c.exe"" ENTERPRISE /WS http://srv/base"));
    }

    [Fact]
    public void NullCommandLine_DoesNotThrow()
    {
        Assert.False(RunningInfobaseMatcher.MatchesCommandLine(FileBase(@"C:\Bases\ib"), null));
    }

    [Fact]
    public void SlashOutKey_IsNotConfusedWithSlashS()
    {
        // /Out — не /S: ключ должен быть отдельным токеном.
        Assert.False(RunningInfobaseMatcher.MatchesCommandLine(
            ServerBase("server1", "trade"),
            @"""1cv8.exe"" DESIGNER /Out C:\log.txt /S server1\trade-mismatch"));
    }

    [Fact]
    public void PathsEqual_IgnoresCaseAndTrailingSeparators()
    {
        Assert.True(RunningInfobaseMatcher.PathsEqual(@"C:\Bases\ib\", @"c:\Bases\IB"));
        Assert.False(RunningInfobaseMatcher.PathsEqual(@"C:\Bases\ib", @"C:\Bases\ib2"));
        Assert.False(RunningInfobaseMatcher.PathsEqual(null, @"C:\x"));
    }
}
