using Configuration_Management.Models;
using Configuration_Management.Services;
using Xunit;

namespace ConfigurationManagement.Tests;

/// <summary>
/// Тесты чистой подстановки параметров сценариев запуска скриптов
/// (<see cref="ScriptParameterResolver"/>, issue #308): %name%, вложенные свойства
/// подключения через точку, дата в пользовательском формате, поведение
/// неизвестного ключа и построение примера командной строки.
/// </summary>
public sealed class ScriptParameterResolverTests
{
    private static readonly DateTime FixedNow = new(2026, 9, 28, 14, 5, 7);

    private static Infobase MakeInfobase()
    {
        var ib = new Infobase { Name = "Бухгалтерия" };
        ib.Connection.Type = ConnectionType.ClientServer;
        ib.Connection.Server = "srv-1c";
        ib.Connection.DatabaseName = "acc_main";
        ib.Connection.Port = 1541;
        return ib;
    }

    private static Dictionary<string, string> Map(Infobase? ib = null) =>
        ScriptParameterResolver.BuildValueMap(ib);

    // ------------------- %name% и вложенные свойства подключения -------------------

    [Fact]
    public void Resolve_Name_SubstitutesBaseName()
    {
        var map = Map(MakeInfobase());

        var result = ScriptParameterResolver.Resolve("База: %name%", map, FixedNow);

        Assert.Equal("База: Бухгалтерия", result);
    }

    [Fact]
    public void Resolve_ConnectionServer_SubstitutesServer()
    {
        var map = Map(MakeInfobase());

        var result = ScriptParameterResolver.Resolve("/S:%connection.server%", map, FixedNow);

        Assert.Equal("/S:srv-1c", result);
    }

    [Fact]
    public void Resolve_NestedConnectionKeys_SubstituteAll()
    {
        var map = Map(MakeInfobase());

        var result = ScriptParameterResolver.Resolve(
            "%name%|%connection.server%|%connection.database%|%connection.filePath%",
            map, FixedNow);

        Assert.Equal("Бухгалтерия|srv-1c|acc_main|", result);
    }

    [Fact]
    public void BuildValueMap_FileBase_HasFilePath()
    {
        var ib = new Infobase { Name = "Файловая" };
        ib.Connection.Type = ConnectionType.File;
        ib.Connection.FilePath = @"D:\bases\file_base";

        var map = Map(ib);

        Assert.Equal(@"D:\bases\file_base", map["connection.filePath"]);
        Assert.Equal("Файловая", map["name"]);
    }

    [Fact]
    public void BuildValueMap_NullInfobase_ReturnsEmptyMap()
    {
        var map = Map(null);

        Assert.Empty(map);
    }

    // ------------------- Дата -------------------

    [Fact]
    public void Resolve_Date_DefaultFormat()
    {
        var map = Map(MakeInfobase());

        var result = ScriptParameterResolver.Resolve("%date%", map, FixedNow);

        Assert.Equal("2026-09-28", result);
    }

    [Fact]
    public void Resolve_DateCustomFormat_UsesGivenFormat()
    {
        var map = Map(MakeInfobase());

        var result = ScriptParameterResolver.Resolve("%date:yyyyMMdd_HHmm%", map, FixedNow);

        Assert.Equal("20260928_1405", result);
    }

    [Fact]
    public void Resolve_DateInvalidFormat_FallsBackToDefault()
    {
        var map = Map(MakeInfobase());

        // Некорректный формат .NET не должен ронять подстановку.
        var result = ScriptParameterResolver.Resolve("%date:[%", map, FixedNow);

        Assert.Equal("2026-09-28", result);
    }

    // ------------------- Неизвестный ключ -------------------

    [Fact]
    public void Resolve_UnknownKey_LeavesTokenAsIs()
    {
        var map = Map(MakeInfobase());

        var result = ScriptParameterResolver.Resolve("%unknown.key%", map, FixedNow);

        Assert.Equal("%unknown.key%", result);
    }

    [Fact]
    public void Resolve_UnknownKey_WhenNotLeaveReplacesWithEmpty()
    {
        var map = Map(MakeInfobase());

        var result = ScriptParameterResolver.Resolve("a-%unknown.key%-b", map, FixedNow, leaveUnknown: false);

        Assert.Equal("a--b", result);
    }

    [Fact]
    public void Resolve_NoTokens_ReturnsTemplateAsIs()
    {
        var map = Map(MakeInfobase());

        var result = ScriptParameterResolver.Resolve("просто текст", map, FixedNow);

        Assert.Equal("просто текст", result);
    }

    // ------------------- Командная строка -------------------

    [Fact]
    public void BuildCommandLine_PathWithSpaces_QuotesPath()
    {
        var scenario = new ScriptScenario
        {
            FilePath = @"C:\Program Files\Tools\report.bat",
            Parameters = new List<string> { "/S:%connection.server%", "base=%name%" }
        };
        var map = Map(MakeInfobase());

        var result = ScriptParameterResolver.BuildCommandLine(scenario, map, FixedNow);

        Assert.Equal("\"C:\\Program Files\\Tools\\report.bat\" /S:srv-1c base=Бухгалтерия", result);
    }

    [Fact]
    public void BuildCommandLine_NoSpaceInPath_KeepsPathUnquoted()
    {
        var scenario = new ScriptScenario
        {
            FilePath = "/opt/scripts/report.sh",
            Parameters = new List<string> { "--db", "%connection.database%" }
        };
        var map = Map(MakeInfobase());

        var result = ScriptParameterResolver.BuildCommandLine(scenario, map, FixedNow);

        Assert.Equal("/opt/scripts/report.sh --db acc_main", result);
    }

    [Fact]
    public void BuildCommandLine_EmptyParameters_ReturnsPathOnly()
    {
        var scenario = new ScriptScenario { FilePath = "tool.exe" };
        var map = Map(MakeInfobase());

        var result = ScriptParameterResolver.BuildCommandLine(scenario, map, FixedNow);

        Assert.Equal("tool.exe", result);
    }
}