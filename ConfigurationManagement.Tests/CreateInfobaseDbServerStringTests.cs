using Configuration_Management.Models;
using Configuration_Management.Services;
using Xunit;

namespace ConfigurationManagement.Tests;

/// <summary>
/// Тесты сборки значения DBSrvr для команды CREATEINFOBASE (issue #305):
/// формат зависит от СУБД — PostgreSQL «host port=NNNN» (через пробел),
/// MSSQL Server «host,NNNN», остальные — просто «host». Пустой порт строку
/// не меняет. Без реальной платформы 1С.
/// </summary>
public sealed class CreateInfobaseDbServerStringTests
{
    // ======================= BuildDbServerString =======================

    [Fact]
    public void BuildDbServerString_PostgreSqlWithPort_UsesSpaceSeparatedPort()
    {
        var result = CreateInfobaseService.BuildDbServerString(
            "PostgreSQL", "localhost", "5433");

        Assert.Equal("localhost port=5433", result);
    }

    [Fact]
    public void BuildDbServerString_PostgreSqlWithoutPort_ReturnsServerOnly()
    {
        var result = CreateInfobaseService.BuildDbServerString(
            "PostgreSQL", "localhost", "");

        Assert.Equal("localhost", result);
    }

    [Fact]
    public void BuildDbServerString_PostgreSqlLowerCase_IsCaseInsensitive()
    {
        var result = CreateInfobaseService.BuildDbServerString(
            "postgresql", "dbhost", "5432");

        Assert.Equal("dbhost port=5432", result);
    }

    [Fact]
    public void BuildDbServerString_MssqlWithPort_UsesCommaSeparatedPort()
    {
        var result = CreateInfobaseService.BuildDbServerString(
            "MSSQLServer", "sqlhost", "1433");

        Assert.Equal("sqlhost,1433", result);
    }

    [Fact]
    public void BuildDbServerString_MssqlWithoutPort_ReturnsServerOnly()
    {
        var result = CreateInfobaseService.BuildDbServerString(
            "MSSQLServer", "sqlhost", null);

        Assert.Equal("sqlhost", result);
    }

    [Fact]
    public void BuildDbServerString_OtherDbms_IgnoresPort()
    {
        var result = CreateInfobaseService.BuildDbServerString(
            "OracleDatabase", "oradb", "1521");

        Assert.Equal("oradb", result);
    }

    [Fact]
    public void BuildDbServerString_EmptyPort_ReturnsServerOnly()
    {
        var result = CreateInfobaseService.BuildDbServerString(
            "PostgreSQL", "localhost", "   ");

        Assert.Equal("localhost", result);
    }

    [Fact]
    public void BuildDbServerString_EmptyServer_ReturnsEmptyString()
    {
        var result = CreateInfobaseService.BuildDbServerString(
            "PostgreSQL", "  ", "5433");

        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void BuildDbServerString_TrimsInputValues()
    {
        var result = CreateInfobaseService.BuildDbServerString(
            " PostgreSQL ", " localhost ", " 5433 ");

        Assert.Equal("localhost port=5433", result);
    }

    // ============ Сервер 1С с портом (issue #305) ============

    [Fact]
    public void Format1CServer_WithPort_ReturnsServerColonPort()
    {
        var result = CreateInfobaseService.Format1CServer("srv1c", 1541);

        Assert.Equal("srv1c:1541", result);
    }

    [Fact]
    public void Format1CServer_WithoutPort_ReturnsServerOnly()
    {
        var result = CreateInfobaseService.Format1CServer("srv1c", 0);

        Assert.Equal("srv1c", result);
    }

    [Fact]
    public void Format1CServer_EmptyServer_ReturnsEmptyString()
    {
        var result = CreateInfobaseService.Format1CServer("  ", 1541);

        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void Format1CServer_TrimsServer()
    {
        var result = CreateInfobaseService.Format1CServer(" srv1c ", 1541);

        Assert.Equal("srv1c:1541", result);
    }

    [Fact]
    public void ParseServerPort_WithPort_SplitsIntoServerAndPort()
    {
        CreateInfobaseService.ParseServerPort("srv1c:1541", out var server, out var port);

        Assert.Equal("srv1c", server);
        Assert.Equal(1541, port);
    }

    [Fact]
    public void ParseServerPort_WithoutPort_ReturnsWholeStringAsServer()
    {
        CreateInfobaseService.ParseServerPort("srv1c", out var server, out var port);

        Assert.Equal("srv1c", server);
        Assert.Equal(0, port);
    }

    [Fact]
    public void ParseServerPort_NonNumericSuffix_TreatsWholeStringAsServer()
    {
        CreateInfobaseService.ParseServerPort("srv1c:prod", out var server, out var port);

        Assert.Equal("srv1c:prod", server);
        Assert.Equal(0, port);
    }

    [Fact]
    public void ParseServerPort_OutOfRangePort_TreatsWholeStringAsServer()
    {
        CreateInfobaseService.ParseServerPort("srv1c:70000", out var server, out var port);

        Assert.Equal("srv1c:70000", server);
        Assert.Equal(0, port);
    }

    [Fact]
    public void ParseServerPort_ExtraColons_UsesLastColonAsPortSeparator()
    {
        CreateInfobaseService.ParseServerPort("srv1c:prod:1541", out var server, out var port);

        Assert.Equal("srv1c:prod", server);
        Assert.Equal(1541, port);
    }

    [Fact]
    public void ParseServerPort_TrailingColon_TreatsWholeStringAsServer()
    {
        CreateInfobaseService.ParseServerPort("srv1c:1541:", out var server, out var port);

        Assert.Equal("srv1c:1541:", server);
        Assert.Equal(0, port);
    }

    [Fact]
    public void ParseServerPort_Empty_ReturnsEmptyServerAndZeroPort()
    {
        CreateInfobaseService.ParseServerPort("   ", out var server, out var port);

        Assert.Equal(string.Empty, server);
        Assert.Equal(0, port);
    }

    // ====== Порт сервера 1С в команде CREATEINFOBASE (issue #305, 0.3.9.242) ======

    [Fact]
    public void BuildConnectionString_WithServerPort_AddsPortToSrvr()
    {
        var cs = OneCLauncher.BuildClientServerCreateConnectionString(
            "srv1c", "base", serverPort: 1541);

        Assert.Equal("Srvr=\"srv1c:1541\";Ref=\"base\"", cs);
    }

    [Fact]
    public void BuildConnectionString_WithoutServerPort_SrvrWithoutPort()
    {
        var cs = OneCLauncher.BuildClientServerCreateConnectionString(
            "srv1c", "base");

        Assert.Equal("Srvr=\"srv1c\";Ref=\"base\"", cs);
    }

    [Fact]
    public void BuildConnectionString_WithServerPortAndDbms_CombinesAll()
    {
        var cs = OneCLauncher.BuildClientServerCreateConnectionString(
            "srv1c", "base", serverPort: 1541,
            dbms: "PostgreSQL", dbServer: "localhost port=5433", dbName: "base_db",
            createSqlDatabase: true, blockScheduledJobs: true);

        Assert.Contains("Srvr=\"srv1c:1541\";Ref=\"base\"", cs);
        Assert.Contains("DBMS=\"PostgreSQL\"", cs);
        Assert.Contains("DBSrvr=\"localhost port=5433\"", cs);
        Assert.Contains("DB=\"base_db\"", cs);
        Assert.Contains("CrSQLDB=\"Y\"", cs);
        Assert.Contains("SchJobDn=\"Y\"", cs);
    }

    /// <summary>
    /// Маппинг «ввод server:port → параметры создания → строка подключения CREATEINFOBASE»
    /// (issue #305): поле окна разносится <see cref="CreateInfobaseService.ParseServerPort"/>,
    /// а порт обязан попасть в Srvr строки подключения — без этого создание базы на сервере
    /// с нестандартным портом (1541) падает (платформа стучится в порт по умолчанию 1540).
    /// </summary>
    [Theory]
    [InlineData("srv1c:1541", "srv1c", 1541)]
    [InlineData("srv1c:1540", "srv1c", 1540)]
    [InlineData("srv1c", "srv1c", 0)]
    public void ParseServerPort_MapsIntoCreateConnectionString(string field, string expectedServer, int expectedPort)
    {
        CreateInfobaseService.ParseServerPort(field, out var server, out var port);

        Assert.Equal(expectedServer, server);
        Assert.Equal(expectedPort, port);

        var cs = OneCLauncher.BuildClientServerCreateConnectionString(server, "base", serverPort: port);
        var expectedSrvr = expectedPort > 0 ? $"Srvr=\"{expectedServer}:{expectedPort}\"" : $"Srvr=\"{expectedServer}\"";
        Assert.StartsWith(expectedSrvr, cs);
    }

    // ======================= Модель запроса =======================

    [Fact]
    public void CreateInfobaseRequest_DbPort_DefaultsToNull()
    {
        var request = new CreateInfobaseRequest();

        Assert.Null(request.DbPort);
    }

    [Fact]
    public void CreateInfobaseRequest_DbPort_RoundTrips()
    {
        var request = new CreateInfobaseRequest { DbPort = "5433" };

        Assert.Equal("5433", request.DbPort);
    }

    [Fact]
    public void CreateInfobaseRequest_ServerPort_DefaultsToNull()
    {
        var request = new CreateInfobaseRequest();

        Assert.Null(request.ServerPort);
    }

    [Fact]
    public void CreateInfobaseRequest_ServerPort_RoundTrips()
    {
        var request = new CreateInfobaseRequest { ServerPort = "1541" };

        Assert.Equal("1541", request.ServerPort);
    }

    // ======================= Разрядность новой базы (issue #305) =======================

    [Theory]
    [InlineData("X64", "64-priority")]
    [InlineData("x64", "64-priority")]
    [InlineData("X86", "32-priority")]
    [InlineData("Priority", "32-priority")]
    [InlineData(null, "32-priority")]
    [InlineData("", "32-priority")]
    public void PriorityArchitectureFromDefault_MapsDefaultMode(string? mode, string expected)
    {
        // Без суффикса «(32)/(64)» в выбранной версии новая база наследует режим
        // «Разрядности по умолчанию» из настроек: X64 → 64-priority, остальное — как раньше.
        Assert.Equal(expected, CreateInfobaseService.PriorityArchitectureFromDefault(mode));
    }

    [Theory]
    [InlineData("8.3.27", "X64", "64-priority")]          // нет суффикса → приоритет по настройке (не «32»!)
    [InlineData("8.3.27", "X86", "32-priority")]
    [InlineData("8.3.27 (64)", "X86", "64")]              // явный суффикс побеждает настройку
    [InlineData("8.3.27 (32)", "X64", "32")]
    [InlineData("8.5.1.123 (x64)", "X86", "64")]
    public void ResolveStoredArchitecture_ExplicitSuffixWins_OtherwiseDefault(
        string platform, string defaultArch, string expected)
    {
        // issue #305: ParseVariant без суффикса возвращает «32» по умолчанию — прежняя
        // логика записывала бы в базу «8.3.27 [x86]» при дефолте X64. Проверяем, что
        // без суффикса берётся приоритетный режим из настроек.
        Assert.Equal(expected, CreateInfobaseService.ResolveStoredArchitecture(platform, defaultArch));
    }

    [Theory]
    [InlineData("8.3.27 (64)", "8.3.27")]
    [InlineData("8.5.1.123 (x86)", "8.5.1.123")]
    [InlineData("8.3.27", "8.3.27")]
    public void ResolveCleanPlatform_StripsOnlyArchSuffix(string platform, string expected)
    {
        Assert.Equal(expected, CreateInfobaseService.ResolveCleanPlatform(platform));
    }

    [Theory]
    [InlineData("localhost:1541", "localhost", true)]   // порт не мешает
    [InlineData("SRV", "srv", true)]                    // регистр не мешает
    [InlineData("server1", "server2", false)]
    [InlineData("127.0.0.1", "localhost", false)]       // разные хосты
    public void SameServer_IgnoresPortAndCase(string a, string b, bool expected)
    {
        // issue #305: эвристика предупреждения о версии сравнивает серверы из списка баз
        // и поля окна; база может быть задана с портом, поле — без него.
        Assert.Equal(expected, CreateInfobaseService.SameServer(a, b));
    }
}