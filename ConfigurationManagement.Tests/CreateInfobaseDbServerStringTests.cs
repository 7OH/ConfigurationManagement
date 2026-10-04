using System.Threading;
using System.Threading.Tasks;
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
    [InlineData("localhost", "localhost:1541", true)]   // порт не задан у одной стороны → равны
    [InlineData("localhost:1541", "localhost", true)]   // симметрично
    [InlineData("SRV", "srv", true)]                    // регистр не мешает
    [InlineData("server1", "server2", false)]
    [InlineData("127.0.0.1", "localhost", false)]       // разные хосты
    public void SameServer_WithPorts_EqualWhenNoPortSpecified(string a, string b, bool expected)
    {
        // issue #305: эвристика предупреждения о версии сравнивает серверы из списка баз
        // и поля окна; база может быть задана с портом, поле — без него.
        Assert.Equal(expected, CreateInfobaseService.SameServer(a, b));
    }

    [Theory]
    [InlineData("localhost:1541", "localhost:1545")]    // разные порты — разные кластеры
    [InlineData("srv1c:1541", "srv1c:2541")]
    public void SameServer_WithDifferentPorts_NotEqual(string a, string b)
    {
        // Порт явно задан у обеих сторон и отличается — серверы НЕ равны
        // (на одном хосте могут работать несколько кластеров 1С, issue #305).
        Assert.False(CreateInfobaseService.SameServer(a, b));
    }

    [Fact]
    public void SameServer_WithSamePort_Equal()
    {
        Assert.True(CreateInfobaseService.SameServer("localhost:1541", "localhost:1541"));
        Assert.True(CreateInfobaseService.SameServer("SRV1C:1541", "srv1c:1541"));
    }

    [Fact]
    public void SameServer_NonNumericSuffix_TreatsWholeAsServer()
    {
        // «server:prod» не разбирается как сервер+порт — адрес сравнивается целиком.
        Assert.True(CreateInfobaseService.SameServer("srv1c:prod", "srv1c:prod"));
        Assert.False(CreateInfobaseService.SameServer("srv1c:prod", "srv1c"));
    }

    [Fact]
    public void SameServer_EmptyServer_ReturnsFalse()
    {
        Assert.False(CreateInfobaseService.SameServer(string.Empty, "localhost"));
        Assert.False(CreateInfobaseService.SameServer(null, "localhost"));
        Assert.False(CreateInfobaseService.SameServer("", ""));
    }

    // ============ SameServer с ЯВНЫМИ портами (порт в поле ConnectionSettings.Port) ============
    // У существующей базы порт лежит в отдельном поле (по умолчанию 1541), а не в строке
    // сервера — сравнение только строк не различило бы кластеры localhost:1541/1545 (issue #305).

    [Fact]
    public void SameServer_ExplicitPorts_EqualWhenHostAndPortMatch()
    {
        Assert.True(CreateInfobaseService.SameServer("localhost", 1541, "localhost", 1541));
        Assert.True(CreateInfobaseService.SameServer("SRV", 1541, "srv", 1541));
    }

    [Fact]
    public void SameServer_ExplicitDifferentPorts_NotEqual()
    {
        // Ключевой кейс issue #305: порты заданы в отдельном поле у ОБЕИХ сторон
        // и различаются — это разные кластеры на одном хосте.
        Assert.False(CreateInfobaseService.SameServer("localhost", 1541, "localhost", 1545));
    }

    [Fact]
    public void SameServer_ExplicitPorts_NoPortOnOneSide_EqualFallback()
    {
        // Порт не задан хотя бы у одной стороны — серверы считаются равными (fallback):
        // «localhost» в поле окна совпадает с базой на localhost:1541.
        Assert.True(CreateInfobaseService.SameServer("localhost", 0, "localhost", 1541));
        Assert.True(CreateInfobaseService.SameServer("localhost", 1541, "localhost", 0));
    }

    [Fact]
    public void SameServer_ExplicitPorts_DifferentHosts_NotEqual()
    {
        Assert.False(CreateInfobaseService.SameServer("srv1", 1541, "srv2", 1541));
    }

    // ============ BuildBaseServerAddress — адрес найденной базы для предупреждения ============

    [Fact]
    public void BuildBaseServerAddress_UsesPortField_WhenSet()
    {
        var conn = new ConnectionSettings { Server = "localhost", Port = 1545 };

        Assert.Equal("localhost:1545", CreateInfobaseService.BuildBaseServerAddress(conn));
    }

    [Fact]
    public void BuildBaseServerAddress_NoDoublePort_WhenPortInStringAndField()
    {
        // Порт может храниться и в строке сервера, и в отдельном поле — задвоения быть не должно.
        var conn = new ConnectionSettings { Server = "localhost:1541", Port = 1541 };

        Assert.Equal("localhost:1541", CreateInfobaseService.BuildBaseServerAddress(conn));
    }

    [Fact]
    public void BuildBaseServerAddress_DefaultPortField_UsedForAddress()
    {
        // Значение по умолчанию 1541 (пользователь порт не вводил) — именно его и увидит
        // пользователь в предупреждении; текст сообщения объясняет, что адрес взят из списка баз.
        var conn = new ConnectionSettings { Server = "localhost" };

        Assert.Equal("localhost:1541", CreateInfobaseService.BuildBaseServerAddress(conn));
    }

    [Fact]
    public void BuildBaseServerAddress_EmptyServer_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, CreateInfobaseService.BuildBaseServerAddress(null));
        Assert.Equal(string.Empty, CreateInfobaseService.BuildBaseServerAddress(new ConnectionSettings()));
    }

    // ============ Сохранение «Сервера СУБД» и типа при создании/закрытии окна (issue #305) ============

    [Fact]
    public void SaveLastDbServer_PersistsForNextOpen()
    {
        var repo = new FakeRepository();
        var service = new CreateInfobaseService(repo);

        service.SaveLastDbServer("localhost", "5433", isClientServer: true);

        Assert.Equal("localhost", repo.Settings.LastCreateDbServer);
        Assert.Equal("5433", repo.Settings.LastCreateDbPort);
        Assert.Equal("ClientServer", repo.Settings.LastCreateDbType);
    }

    [Fact]
    public void SaveLastDbServer_ClientServer_SavesTypeAndServer()
    {
        var repo = new FakeRepository();
        var service = new CreateInfobaseService(repo);

        service.SaveLastDbServer("dbhost", "5432", isClientServer: true);

        Assert.Equal("dbhost", repo.Settings.LastCreateDbServer);
        Assert.Equal("5432", repo.Settings.LastCreateDbPort);
        Assert.Equal("ClientServer", repo.Settings.LastCreateDbType);
    }

    [Fact]
    public void SaveLastDbServer_FileMode_SavesTypeOnly()
    {
        // Файловый режим не должен затирать ранее сохранённый сервер СУБД:
        // он относится только к клиент-серверному созданию (issue #305).
        var repo = new FakeRepository();
        repo.Settings.LastCreateDbServer = "dbhost";
        repo.Settings.LastCreateDbPort = "5432";
        var service = new CreateInfobaseService(repo);

        service.SaveLastDbServer("", "", isClientServer: false);

        Assert.Equal("File", repo.Settings.LastCreateDbType);
        Assert.Equal("dbhost", repo.Settings.LastCreateDbServer);
        Assert.Equal("5432", repo.Settings.LastCreateDbPort);
    }

    [Fact]
    public void SaveLastDbServer_EmptyClientServer_SavesTypeOnly()
    {
        // Клиент-серверный режим с пустым сервером: тип сохраняется, сервер остаётся как был.
        var repo = new FakeRepository();
        repo.Settings.LastCreateDbServer = "oldhost";
        var service = new CreateInfobaseService(repo);

        service.SaveLastDbServer("", "", isClientServer: true);

        Assert.Equal("ClientServer", repo.Settings.LastCreateDbType);
        Assert.Equal(string.Empty, repo.Settings.LastCreateDbServer);
        Assert.Equal(string.Empty, repo.Settings.LastCreateDbPort);
    }

    [Fact]
    public void SaveLastDbServer_InvalidRepo_DoesNotThrow()
    {
        // Ошибка сохранения не должна прерывать создание ИБ / закрытие окна (issue #305).
        var service = new CreateInfobaseService(new ThrowingRepository());

        var ex = Record.Exception(() =>
            service.SaveLastDbServer("localhost", "5433", isClientServer: true));

        Assert.Null(ex);
    }

    // ============ AppSettings round-trip (issue #305) ============

    [Fact]
    public void AppSettings_RoundTrip_PreservesLastCreateDbFields()
    {
        // Моделирует сценарий #305: запись «мутацией» загруженного объекта сохраняет
        // поля внешних писателей (последний сервер СУБД/порт/тип) через сериализацию.
        var settings = new AppSettings
        {
            Theme = "Dark",
            LastCreateDbServer = "dbhost",
            LastCreateDbPort = "5432",
            LastCreateDbType = "ClientServer"
        };

        var json = System.Text.Json.JsonSerializer.Serialize(settings);
        var loaded = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(json)!;

        Assert.Equal("dbhost", loaded.LastCreateDbServer);
        Assert.Equal("5432", loaded.LastCreateDbPort);
        Assert.Equal("ClientServer", loaded.LastCreateDbType);
        Assert.Equal("Dark", loaded.Theme);
    }

    [Fact]
    public void AppSettings_LastCreateDbType_DefaultsToFile()
    {
        // Обратная совместимость: у новых/старых файлов настроек без поля тип = "File".
        var settings = new AppSettings();

        Assert.Equal("File", settings.LastCreateDbType);

        var json = System.Text.Json.JsonSerializer.Serialize(new AppSettings());
        var loaded = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(json)!;

        Assert.Equal("File", loaded.LastCreateDbType);
    }

    /// <summary>Репозиторий в памяти: настройки живут в объекте (образец PlatformDownloadTests).</summary>
    private sealed class FakeRepository : IInfobaseRepository
    {
        public AppSettings Settings { get; set; } = new();

        public List<Infobase> Load() => new();

        public void Save(List<Infobase> infobases)
        {
        }

        public Task SaveAsync(List<Infobase> infobases, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public List<Group> LoadGroups() => new();

        public void SaveGroups(List<Group> groups)
        {
        }

        public Task SaveGroupsAsync(List<Group> groups, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public AppSettings LoadSettings() => Settings;

        public void SaveSettings(AppSettings settings) => Settings = settings;

        public Task SaveSettingsAsync(AppSettings settings, CancellationToken cancellationToken = default)
        {
            Settings = settings;
            return Task.CompletedTask;
        }
    }

    /// <summary>Репозиторий, у которого сохранение настроек всегда бросает исключение.</summary>
    private sealed class ThrowingRepository : IInfobaseRepository
    {
        public AppSettings Settings { get; set; } = new();

        public List<Infobase> Load() => new();

        public void Save(List<Infobase> infobases)
        {
        }

        public Task SaveAsync(List<Infobase> infobases, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public List<Group> LoadGroups() => new();

        public void SaveGroups(List<Group> groups)
        {
        }

        public Task SaveGroupsAsync(List<Group> groups, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public AppSettings LoadSettings() => Settings;

        public void SaveSettings(AppSettings settings)
            => throw new InvalidOperationException("save failed");

        public Task SaveSettingsAsync(AppSettings settings, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("save failed");
    }
}