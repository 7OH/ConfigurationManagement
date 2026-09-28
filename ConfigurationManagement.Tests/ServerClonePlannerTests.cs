using Configuration_Management.Models;
using Configuration_Management.Services;
using Xunit;

namespace ConfigurationManagement.Tests;

/// <summary>
/// Тесты чистой логики клонирования клиент-серверной ИБ
/// (<see cref="ServerClonePlanner"/>): имя/Ref клона, строка подключения,
/// валидация запроса, этапы операции. Без реальной платформы 1С.
/// </summary>
public sealed class ServerClonePlannerTests
{
    // ======================= ProposeCloneName =======================

    [Theory]
    [InlineData("Бухгалтерия", "Бухгалтерия — Копия")]
    [InlineData("", "Копия")]
    [InlineData("  Зарплата  ", "Зарплата — Копия")]
    public void ProposeCloneName_AppendsSuffix(string source, string expected)
    {
        Assert.Equal(expected, ServerClonePlanner.ProposeCloneName(source));
    }

    // ======================= SuggestRefName =======================

    [Fact]
    public void SuggestRefName_AppendsCopySuffix()
    {
        Assert.Equal("Бух_база_copy", ServerClonePlanner.SuggestRefName("Бух_база", Array.Empty<string>()));
    }

    [Fact]
    public void SuggestRefName_UniquifiesOnCollision()
    {
        var existing = new[] { "Бух_база_copy", "БУХ_БАЗА_COPY2" };

        var result = ServerClonePlanner.SuggestRefName("Бух_база", existing);

        Assert.Equal("Бух_база_copy3", result);
    }

    [Fact]
    public void SuggestRefName_CaseInsensitive()
    {
        var existing = new[] { "Base_copy" };

        Assert.Equal("base_copy2", ServerClonePlanner.SuggestRefName("base", existing));
    }

    [Fact]
    public void SuggestRefName_EmptySource_ReturnsClone()
    {
        Assert.Equal("Clone", ServerClonePlanner.SuggestRefName("  ", Array.Empty<string>()));
        Assert.Equal("Clone2", ServerClonePlanner.SuggestRefName("  ", new[] { "Clone" }));
    }

    // ======================= BuildConnectionString =======================

    [Fact]
    public void BuildConnectionString_FullSet()
    {
        var request = new ServerCloneRequest
        {
            Server = "srv-1c",
            DatabaseName = "base_copy",
            Dbms = "PostgreSQL",
            DbServer = "pg-host",
            DbName = "base_copy_db",
            DbUser = "1c_user",
            DbPassword = "secret",
            CreateSqlDatabase = true,
            BlockScheduledJobs = true
        };

        var cs = ServerClonePlanner.BuildConnectionString(request);

        Assert.Equal(
            "Srvr=\"srv-1c\";Ref=\"base_copy\";DBMS=\"PostgreSQL\";DBSrvr=\"pg-host\";" +
            "DB=\"base_copy_db\";DBUID=\"1c_user\";DBPwd=\"secret\";CrSQLDB=\"Y\";SchJobDn=\"Y\"",
            cs);
    }

    [Fact]
    public void BuildConnectionString_Minimal()
    {
        var request = new ServerCloneRequest
        {
            Server = "srv-1c",
            DatabaseName = "base_copy"
        };

        Assert.Equal("Srvr=\"srv-1c\";Ref=\"base_copy\"", ServerClonePlanner.BuildConnectionString(request));
    }

    [Fact]
    public void BuildConnectionString_EscapesQuoteByDoubling()
    {
        var request = new ServerCloneRequest
        {
            Server = "srv\"1c",
            DatabaseName = "base"
        };

        Assert.Equal("Srvr=\"srv\"\"1c\";Ref=\"base\"", ServerClonePlanner.BuildConnectionString(request));
    }

    // ======================= BuildSourceSrvr =======================

    [Fact]
    public void BuildSourceSrvr_ServerWithPortAndRef()
    {
        Assert.Equal("host:2541\\Бух_база", ServerClonePlanner.BuildSourceSrvr("host:2541", "Бух_база"));
    }

    [Fact]
    public void BuildSourceSrvr_MissingParts()
    {
        Assert.Equal("host", ServerClonePlanner.BuildSourceSrvr("host", ""));
        Assert.Equal("base", ServerClonePlanner.BuildSourceSrvr("", "base"));
        Assert.Equal("", ServerClonePlanner.BuildSourceSrvr("", ""));
    }

    // ======================= Validate =======================

    [Fact]
    public void Validate_EmptyName_ReturnsEnterName()
    {
        Assert.Equal(ServerCloneValidationError.EnterName,
            ServerClonePlanner.Validate(new ServerCloneRequest { CloneName = " " }));
        Assert.Equal(ServerCloneValidationError.EnterName, ServerClonePlanner.Validate(null));
    }

    [Fact]
    public void Validate_EmptyPlatform_ReturnsNoPlatform()
    {
        var request = new ServerCloneRequest { CloneName = "Копия", PlatformVersion = "" };

        Assert.Equal(ServerCloneValidationError.NoPlatform, ServerClonePlanner.Validate(request));
    }

    [Fact]
    public void Validate_EmptyServerOrRef_ReturnsEnterServerAndRef()
    {
        var request = new ServerCloneRequest
        {
            CloneName = "Копия",
            PlatformVersion = "8.3.24",
            Server = "",
            DatabaseName = "base_copy"
        };
        Assert.Equal(ServerCloneValidationError.EnterServerAndRef, ServerClonePlanner.Validate(request));

        request.Server = "host";
        request.DatabaseName = "";
        Assert.Equal(ServerCloneValidationError.EnterServerAndRef, ServerClonePlanner.Validate(request));
    }

    [Fact]
    public void Validate_CreateSqlDatabaseWithoutDbmsDetails_ReturnsMissingDbmsDetails()
    {
        var request = new ServerCloneRequest
        {
            CloneName = "Копия",
            PlatformVersion = "8.3.24",
            Server = "host",
            DatabaseName = "base_copy",
            CreateSqlDatabase = true,
            Dbms = "",
            DbServer = ""
        };
        Assert.Equal(ServerCloneValidationError.MissingDbmsDetails, ServerClonePlanner.Validate(request));

        request.Dbms = "PostgreSQL";
        request.DbServer = "pg-host";
        Assert.Equal(ServerCloneValidationError.None, ServerClonePlanner.Validate(request));
    }

    [Fact]
    public void Validate_ValidRequest_ReturnsNone()
    {
        var request = new ServerCloneRequest
        {
            CloneName = "Копия",
            PlatformVersion = "8.3.24",
            Server = "host",
            DatabaseName = "base_copy"
        };

        Assert.Equal(ServerCloneValidationError.None, ServerClonePlanner.Validate(request));
    }

    // ======================= DescribeSteps =======================

    [Fact]
    public void DescribeSteps_FullCopy_ThreeStages()
    {
        var request = new ServerCloneRequest { Mode = ServerCloneMode.FullCopy };

        var steps = ServerClonePlanner.DescribeSteps(request);

        Assert.Equal(new[] { "CloneServer.StageDump", "CloneServer.StageCreate", "CloneServer.StageRestore" }, steps);
    }

    [Fact]
    public void DescribeSteps_ConfigurationOnly_TwoStages()
    {
        var request = new ServerCloneRequest { Mode = ServerCloneMode.ConfigurationOnly };

        var steps = ServerClonePlanner.DescribeSteps(request);

        Assert.Equal(new[] { "CloneServer.StageDump", "CloneServer.StageCreate" }, steps);
    }
}