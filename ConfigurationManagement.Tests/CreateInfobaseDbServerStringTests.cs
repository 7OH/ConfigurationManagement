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
}