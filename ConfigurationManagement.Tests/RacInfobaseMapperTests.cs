using Configuration_Management.Models;
using Configuration_Management.Services;
using Xunit;

namespace ConfigurationManagement.Tests;

/// <summary>
/// Тесты чистого маппинга информационной базы кластера 1С в базу списка приложения
/// (<see cref="RacInfobaseMapper"/>): правила построения клиент-серверного подключения
/// (хост/порт кластера, Ref из имени ИБ, Prompt) и дедупликация по строке подключения.
/// </summary>
public sealed class RacInfobaseMapperTests
{
    private static RacInfobaseSummary Source(
        string name = "Бухгалтерия",
        string descr = "Основная база",
        string dbms = "MSSQLServer",
        string dbServer = "sql-01",
        string dbName = "buho",
        string dbUser = "sa",
        string locale = "ru",
        int securityLevel = 0,
        bool licensed = false)
    {
        return new RacInfobaseSummary
        {
            InfobaseId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"),
            Name = name,
            Descr = descr,
            Dbms = dbms,
            DbServer = dbServer,
            DbName = dbName,
            DbUser = dbUser,
            Locale = locale,
            SecurityLevel = securityLevel,
            Licensed = licensed
        };
    }

    // ---------- ToInfobase: маппинг полей ----------

    [Fact]
    public void ToInfobase_MapsFields()
    {
        var source = Source();
        var ib = RacInfobaseMapper.ToInfobase(source, "srv:1545", 2541, null, "Рабочий кластер");

        Assert.Equal("Бухгалтерия", ib.Name);
        Assert.Equal("Рабочий кластер", ib.Group);
        Assert.Equal(string.Empty, ib.Id); // GUID кластера не переносится

        var connection = ib.Connection;
        Assert.NotNull(connection);
        Assert.Equal(ConnectionType.ClientServer, connection.Type);
        Assert.Equal("srv", connection.Server);       // host-часть адреса, порт RAS убран
        Assert.Equal(2541, connection.Port);          // порт кластера, не ragent/RAS
        Assert.Equal("Бухгалтерия", connection.DatabaseName); // Ref = имя ИБ в кластере
        Assert.Equal(AuthenticationMode.Prompt, connection.AuthenticationMode);
        Assert.False(connection.BlockScheduledJobs);
        Assert.False(connection.ForbidSpeechRecognition);
    }

    [Fact]
    public void ToInfobase_PrefersClusterHostName_OverAddress()
    {
        // RAS установлен отдельно от кластера: хост для строки подключения — из cluster info.
        var ib = RacInfobaseMapper.ToInfobase(Source(), "ras-host:1545", 2541, "cluster-host", "Группа");

        Assert.Equal("cluster-host", ib.Connection.Server);
        Assert.Equal(2541, ib.Connection.Port);
    }

    [Fact]
    public void ToInfobase_FallsBackToAddressHost_WithoutRasPort()
    {
        // clusterHostName пуст — берём host-часть введённого адреса, порт RAS (1545) НЕ переносим.
        var withEmptyHost = RacInfobaseMapper.ToInfobase(Source(), "srv:1545", 1541, "", "Группа");
        var withNullHost = RacInfobaseMapper.ToInfobase(Source(), "srv:1545", 1541, null, "Группа");
        var withoutPort = RacInfobaseMapper.ToInfobase(Source(), "srv", 1541, null, "Группа");

        Assert.Equal("srv", withEmptyHost.Connection.Server);
        Assert.Equal("srv", withNullHost.Connection.Server);
        Assert.Equal("srv", withoutPort.Connection.Server);
    }

    [Fact]
    public void ToInfobase_Ipv6Address_KeepsBrackets()
    {
        // IPv6 в квадратных скобках: хост сохраняется с ними, порт RAS не переносится.
        var ib = RacInfobaseMapper.ToInfobase(Source(), "[2001:db8::1]:1545", 1541, null, "Группа");

        Assert.Equal("[2001:db8::1]", ib.Connection.Server);
        Assert.Equal("Srvr=\"[2001:db8::1]\";Ref=\"Бухгалтерия\"", RacInfobaseMapper.ConnectionKey(ib));
    }

    // ---------- ConnectionKey: нормализация порта 1541 ----------

    [Fact]
    public void ConnectionKey_OmitsDefaultPort1541()
    {
        // Порт 1541 — стандартный: GetServerWithPort в строку подключения его не выводит.
        var ib = RacInfobaseMapper.ToInfobase(Source(), "srv", 1541, null, "Группа");

        Assert.Equal("Srvr=\"srv\";Ref=\"Бухгалтерия\"", RacInfobaseMapper.ConnectionKey(ib));
    }

    [Fact]
    public void ConnectionKey_KeepsNonStandardPort()
    {
        // Нестандартный порт кластера (≠ 1541) в строке подключения сохраняется.
        var ib = RacInfobaseMapper.ToInfobase(Source(), "srv", 2541, null, "Группа");

        Assert.Equal("Srvr=\"srv:2541\";Ref=\"Бухгалтерия\"", RacInfobaseMapper.ConnectionKey(ib));
    }

    // ---------- IsDuplicate: дедупликация по строке подключения ----------

    [Fact]
    public void IsDuplicate_TrueForSameConnection_IgnoringHostAndNameCase()
    {
        var existing = RacInfobaseMapper.ToInfobase(Source(name: "БУХГАЛТЕРИЯ"), "SRV:1540", 1541, null, "Группа");
        var candidate = RacInfobaseMapper.ToInfobase(Source(), "srv", 1541, null, "Группа");

        Assert.True(RacInfobaseMapper.IsDuplicate(new[] { existing }, candidate));
    }

    [Fact]
    public void IsDuplicate_TrueForDefaultAndExplicitPort1541()
    {
        // База с явным портом 1541 и без него (Port=0 → порт по умолчанию) — один ключ.
        var existing = new Infobase
        {
            Name = "Бухгалтерия",
            Connection = new ConnectionSettings { Server = "srv", Port = 1541, DatabaseName = "Бухгалтерия" }
        };
        var candidate = new Infobase
        {
            Name = "Бухгалтерия",
            Connection = new ConnectionSettings { Server = "srv", Port = 0, DatabaseName = "Бухгалтерия" }
        };

        Assert.True(RacInfobaseMapper.IsDuplicate(new[] { existing }, candidate));
    }

    [Fact]
    public void IsDuplicate_FalseForSameNameOnDifferentClusterPorts()
    {
        // Одноимённые базы разных кластеров (разные порты подключения) — не дубликаты.
        var cluster1 = RacInfobaseMapper.ToInfobase(Source(), "srv", 1541, null, "Кластер 1");
        var cluster2 = RacInfobaseMapper.ToInfobase(Source(), "srv", 2541, null, "Кластер 2");

        Assert.False(RacInfobaseMapper.IsDuplicate(new[] { cluster1 }, cluster2));
    }

    [Fact]
    public void IsDuplicate_DifferentNames_NotDuplicates()
    {
        var existing = RacInfobaseMapper.ToInfobase(Source(name: "Зарплата"), "srv", 1541, null, "Группа");
        var candidate = RacInfobaseMapper.ToInfobase(Source(), "srv", 1541, null, "Группа");

        Assert.False(RacInfobaseMapper.IsDuplicate(new[] { existing }, candidate));
    }

    [Fact]
    public void IsDuplicate_SafeForEmptyList_AndNullConnection()
    {
        var candidate = RacInfobaseMapper.ToInfobase(Source(), "srv", 1541, null, "Группа");

        Assert.False(RacInfobaseMapper.IsDuplicate(Array.Empty<Infobase>(), candidate));

        // У базы без настроек подключения ключ — пустая строка: исключений нет.
        var withoutConnection = new Infobase { Name = "Пустая" };
        Assert.False(RacInfobaseMapper.IsDuplicate(new[] { withoutConnection }, candidate));
    }
}