using Configuration_Management.Models;
using Configuration_Management.Services;
using Xunit;

namespace ConfigurationManagement.Tests;

/// <summary>
/// Тесты чистого парсера вывода rac (<see cref="RacOutputParser"/>): таблицы
/// (ParseTable), «ключ: значение» (ParseInfo) и типизированные парсеры
/// кластеров/процессов/сеансов/соединений/блокировок/информации о кластере.
/// Образцы вывода соответствуют документированному на ИТС формату rac:
/// list-команды — таблица с разделителем \t и строкой заголовка; info — «ключ: значение».
/// </summary>
public sealed class RacOutputParserTests
{
    // ---------- ParseTable ----------

    [Fact]
    public void ParseTable_SplitsRowsAndColumns()
    {
        var table = RacOutputParser.ParseTable("a\tb\tc\n1\t2\t3\n");

        Assert.Equal(2, table.Count);
        Assert.Equal(new[] { "a", "b", "c" }, table[0]);
        Assert.Equal(new[] { "1", "2", "3" }, table[1]);
    }

    [Fact]
    public void ParseTable_SkipsEmptyLines_AndHandlesCrLf()
    {
        var table = RacOutputParser.ParseTable("a\tb\r\n\r\n1\t2\r\n");

        Assert.Equal(2, table.Count);
        Assert.Equal("b", table[0][1]); // без «\r»
        Assert.Equal("2", table[1][1]);
    }

    [Fact]
    public void ParseTable_PreservesSpacesInValues()
    {
        var table = RacOutputParser.ParseTable("cluster\tname\n111-222\tЛокальный кластер\n");

        Assert.Equal("Локальный кластер", table[1][1]);
    }

    [Fact]
    public void ParseTable_EmptyInput_ReturnsEmpty()
    {
        Assert.Empty(RacOutputParser.ParseTable(string.Empty));
        Assert.Empty(RacOutputParser.ParseTable("\n\n  \n"));
    }

    // ---------- ParseInfo ----------

    [Fact]
    public void ParseInfo_ParsesKeyValue()
    {
        var info = RacOutputParser.ParseInfo("name: Локальный кластер\nport: 1541\n");

        Assert.Equal("Локальный кластер", info["name"]);
        Assert.Equal("1541", info["port"]);
    }

    [Fact]
    public void ParseInfo_TrimsKeyWithPadding()
    {
        // Реальный rac выравнивает ключи пробелами перед «:».
        var info = RacOutputParser.ParseInfo(
            "cluster        : 8b2f6f5e-6e3c-4c5a-8a9b-1c2d3e4f5a6b\n");

        Assert.Equal("8b2f6f5e-6e3c-4c5a-8a9b-1c2d3e4f5a6b", info["cluster"]);
    }

    [Fact]
    public void ParseInfo_SplitsOnFirstColon()
    {
        var info = RacOutputParser.ParseInfo("desc: значение: с двоеточием\n");

        Assert.Equal("значение: с двоеточием", info["desc"]);
    }

    [Fact]
    public void ParseInfo_SkipsLinesWithoutColon_AndEmpty()
    {
        var info = RacOutputParser.ParseInfo("no colon here\n\nname: Тест\n");

        var pair = Assert.Single(info);
        Assert.Equal("name", pair.Key);
        Assert.Equal("Тест", pair.Value);
    }

    [Fact]
    public void ParseInfo_HandlesValueWithSpacesAndCyrillic()
    {
        var info = RacOutputParser.ParseInfo("hostName : Сервер Управления 1С\n");

        Assert.Equal("Сервер Управления 1С", info["hostName"]);
    }

    // ---------- ToClusters ----------

    [Fact]
    public void ToClusters_ParsesSample()
    {
        const string output =
            "cluster\tname\tport\n" +
            "8b2f6f5e-6e3c-4c5a-8a9b-1c2d3e4f5a6b\tЛокальный кластер\t1541\n" +
            "a3f1d2b4-1111-2222-3333-444455556666\tРабочий кластер\t2541\n";

        var clusters = RacOutputParser.ToClusters(output);

        Assert.Equal(2, clusters.Count);
        Assert.Equal(Guid.Parse("8b2f6f5e-6e3c-4c5a-8a9b-1c2d3e4f5a6b"), clusters[0].Id);
        Assert.Equal("Локальный кластер", clusters[0].Name);
        Assert.Equal(1541, clusters[0].Port);
        Assert.Equal("Рабочий кластер", clusters[1].Name);
        Assert.Equal(2541, clusters[1].Port);
    }

    [Fact]
    public void ToClusters_IgnoresExtraColumns()
    {
        const string output =
            "cluster\tname\tport\textra\n" +
            "8b2f6f5e-6e3c-4c5a-8a9b-1c2d3e4f5a6b\tЛокальный кластер\t1541\tосновной\n";

        var cluster = Assert.Single(RacOutputParser.ToClusters(output));

        Assert.Equal("Локальный кластер", cluster.Name);
        Assert.Equal(1541, cluster.Port);
    }

    [Fact]
    public void ToClusters_SkipsBadUuid_AndShortRow()
    {
        const string output =
            "cluster\tname\tport\n" +
            "не-uuid\tКривой идентификатор\t1541\n" +     // кривой uuid — строка пропускается
            "8b2f6f5e-6e3c-4c5a-8a9b-1c2d3e4f5a6b\n";     // меньше полей — строка пропускается

        Assert.Empty(RacOutputParser.ToClusters(output));
    }

    [Fact]
    public void ToClusters_EmptyOutput_ReturnsEmpty()
    {
        Assert.Empty(RacOutputParser.ToClusters(string.Empty));
        Assert.Empty(RacOutputParser.ToClusters("cluster\tname\tport\n"));
    }

    // ---------- ToProcesses ----------

    [Fact]
    public void ToProcesses_ParsesSample()
    {
        const string output =
            "cluster\tprocess\thost\tpid\tport\tstarted-at\tmemory-size\tmemory-total\tmemory-available\tmemory-excess\tthreads\tcpu\tavailable-performances\trunning\tinfobases\n" +
            "8b2f6f5e-6e3c-4c5a-8a9b-1c2d3e4f5a6b\tc3d4e5f6-aaaa-bbbb-cccc-ddddeeeeffff\tserver-01\t2048\t1560\t2026-09-28T10:00:00\t104857600\t536870912\t4294967296\t0\t32\t2.5\t100\t1\t3\n";

        var process = Assert.Single(RacOutputParser.ToProcesses(output));

        Assert.Equal(Guid.Parse("c3d4e5f6-aaaa-bbbb-cccc-ddddeeeeffff"), process.Id);
        Assert.Equal("server-01", process.Host);
        Assert.Equal(2048, process.Pid);
        Assert.Equal(1560, process.Port);
        Assert.Equal(new DateTime(2026, 9, 28, 10, 0, 0), process.StartedAt);
        Assert.Equal(104857600L, process.MemorySize);
        Assert.Equal(536870912L, process.MemoryTotal);
        Assert.Equal(4294967296L, process.MemoryAvailable);
        Assert.Equal(0L, process.MemoryExcess);
        Assert.Equal(32, process.Threads);
        Assert.Equal(2.5, process.Cpu);
        Assert.Equal(100d, process.AvailablePerformances);
        Assert.True(process.Running);
        Assert.Equal(3, process.Infobases);
    }

    [Fact]
    public void ToProcesses_ParsesMinimalColumns_WithDefaults()
    {
        const string output =
            "cluster\tprocess\thost\tpid\tport\n" +
            "8b2f6f5e-6e3c-4c5a-8a9b-1c2d3e4f5a6b\tc3d4e5f6-aaaa-bbbb-cccc-ddddeeeeffff\tserver-01\t2048\t1560\n";

        var process = Assert.Single(RacOutputParser.ToProcesses(output));

        Assert.Equal(2048, process.Pid);
        Assert.Equal(0L, process.MemorySize); // отсутствующие справа колонки — default
        Assert.Equal(0d, process.Cpu);
        Assert.False(process.Running);
        Assert.Equal(0, process.Infobases);
    }

    [Fact]
    public void ToProcesses_SkipsHeader_AndShortRow()
    {
        const string output =
            "cluster\tprocess\thost\tpid\tport\n" +
            "8b2f6f5e-6e3c-4c5a-8a9b-1c2d3e4f5a6b\tc3d4e5f6-aaaa-bbbb-cccc-ddddeeeeffff\tserver-01\n";

        Assert.Empty(RacOutputParser.ToProcesses(output));
    }

    // ---------- ToSessions ----------

    [Fact]
    public void ToSessions_ParsesSample()
    {
        const string output =
            "cluster\tsession\tinfobase\tuser-name\thost\tapp-id\tstarted-at\tlast-active-at\tblocked-by-ls\tblocked-by-deadlock\tdb-proc-duration\tduration-all\tduration-current\tduration-dbms\tduration-cpu\tduration-wait\tmemory\tbytes\tposition\tread\twrite\tconnection\thibernate\tstate\n" +
            "8b2f6f5e-6e3c-4c5a-8a9b-1c2d3e4f5a6b\t11111111-2222-3333-4444-555555555555\t99999999-8888-7777-6666-555555555555\tИванов Иван\tWORKSTATION-01\t1CV8\t2026-09-28T09:30:00\t2026-09-28T09:45:12\t0\t1\t0\t1250\t350\t900\t200\t150\t104857600\t12345678\t0\t1024\t2048\tbbbbbbbb-0000-1111-2222-333344445555\t0\tactive\n";

        var session = Assert.Single(RacOutputParser.ToSessions(output));

        Assert.Equal(Guid.Parse("11111111-2222-3333-4444-555555555555"), session.Id);
        Assert.Equal(Guid.Parse("99999999-8888-7777-6666-555555555555"), session.InfobaseId);
        Assert.Equal("Иванов Иван", session.User);
        Assert.Equal("WORKSTATION-01", session.Host);
        Assert.Equal("1CV8", session.AppId);
        Assert.Equal(new DateTime(2026, 9, 28, 9, 30, 0), session.StartedAt);
        Assert.Equal(new DateTime(2026, 9, 28, 9, 45, 12), session.LastActiveAt);
        Assert.False(session.BlockedByLs);
        Assert.True(session.BlockedByDeadlock);
        Assert.Equal(0L, session.DbProcDuration);
        Assert.Equal(1250L, session.DurationAll);
        Assert.Equal(350L, session.DurationCurrent);
        Assert.Equal(900L, session.DurationDbms);
        Assert.Equal(200L, session.DurationCpu);
        Assert.Equal(150L, session.DurationWait);
        Assert.Equal(104857600L, session.Memory);
        Assert.Equal(12345678L, session.Bytes);
        Assert.Equal(0L, session.Position);
        Assert.Equal(1024L, session.Read);
        Assert.Equal(2048L, session.Write);
        Assert.Equal(Guid.Parse("bbbbbbbb-0000-1111-2222-333344445555"), session.ConnectionId);
        Assert.False(session.Hibernate);
        Assert.Equal("active", session.State);
    }

    [Fact]
    public void ToSessions_EmptyInfobase_IsNull()
    {
        const string output =
            "cluster\tsession\tinfobase\tuser-name\n" +
            "8b2f6f5e-6e3c-4c5a-8a9b-1c2d3e4f5a6b\t11111111-2222-3333-4444-555555555555\t\tСлужебный\n";

        var session = Assert.Single(RacOutputParser.ToSessions(output));

        Assert.Null(session.InfobaseId);
        Assert.Equal("Служебный", session.User);
        Assert.Equal(string.Empty, session.Host);
    }

    [Fact]
    public void ToSessions_SkipsShortRow_WithoutThrowing()
    {
        const string output =
            "cluster\tsession\n" +
            "8b2f6f5e-6e3c-4c5a-8a9b-1c2d3e4f5a6b\t11111111-2222-3333-4444-555555555555\n";

        Assert.Empty(RacOutputParser.ToSessions(output));
    }

    // ---------- ToConnections ----------

    [Fact]
    public void ToConnections_ParsesSample()
    {
        const string output =
            "cluster\tconnection\tsession\tblocked\tconnector\tprocess\thost\tport\testablished-at\tlast-connection-time\tduration\n" +
            "8b2f6f5e-6e3c-4c5a-8a9b-1c2d3e4f5a6b\t11112222-3333-4444-5555-666677778888\t11111111-2222-3333-4444-555555555555\t1\t1CV8\tc3d4e5f6-aaaa-bbbb-cccc-ddddeeeeffff\tWORKSTATION-01\t1560\t2026-09-28T09:30:00\t2026-09-28T09:45:12\t1250\n";

        var connection = Assert.Single(RacOutputParser.ToConnections(output));

        Assert.Equal(Guid.Parse("11112222-3333-4444-5555-666677778888"), connection.Id);
        Assert.Equal(Guid.Parse("11111111-2222-3333-4444-555555555555"), connection.SessionId);
        Assert.True(connection.Blocked);
        Assert.Equal("1CV8", connection.Connector);
        Assert.Equal(Guid.Parse("c3d4e5f6-aaaa-bbbb-cccc-ddddeeeeffff"), connection.ProcessId);
        Assert.Equal("WORKSTATION-01", connection.Host);
        Assert.Equal(1560, connection.Port);
        Assert.Equal(new DateTime(2026, 9, 28, 9, 30, 0), connection.EstablishedAt);
        Assert.Equal(new DateTime(2026, 9, 28, 9, 45, 12), connection.LastConnectionTime);
        Assert.Equal(1250L, connection.Duration);
    }

    // ---------- ToLocks ----------

    [Fact]
    public void ToLocks_ParsesSample()
    {
        const string output =
            "cluster\tlock\tsession\tinfobase\tconnection\ttransaction\twaiting\tblocking\tobject\n" +
            "8b2f6f5e-6e3c-4c5a-8a9b-1c2d3e4f5a6b\t11111111-aaaa-bbbb-cccc-dddddddddddd\t11111111-2222-3333-4444-555555555555\t99999999-8888-7777-6666-555555555555\t11112222-3333-4444-5555-666677778888\taaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee\t0\t1\tСправочник.Номенклатура.Ссылка\n";

        var lockInfo = Assert.Single(RacOutputParser.ToLocks(output));

        Assert.Equal(Guid.Parse("11111111-aaaa-bbbb-cccc-dddddddddddd"), lockInfo.Id);
        Assert.Equal(Guid.Parse("11111111-2222-3333-4444-555555555555"), lockInfo.SessionId);
        Assert.Equal(Guid.Parse("99999999-8888-7777-6666-555555555555"), lockInfo.InfobaseId);
        Assert.Equal(Guid.Parse("11112222-3333-4444-5555-666677778888"), lockInfo.ConnectionId);
        Assert.Equal(Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"), lockInfo.TransactionId);
        Assert.False(lockInfo.Waiting);
        Assert.True(lockInfo.Blocking);
        Assert.Equal("Справочник.Номенклатура.Ссылка", lockInfo.Object);
    }

    // ---------- ToClusterInfo ----------

    [Fact]
    public void ToClusterInfo_ParsesSample()
    {
        const string output =
            "cluster        : 8b2f6f5e-6e3c-4c5a-8a9b-1c2d3e4f5a6b\n" +
            "name           : Локальный кластер\n" +
            "hostName       : localhost\n" +
            "port           : 1541\n" +
            "expirationTimeout : 360\n" +
            "lifetimeLimit  : 1440\n" +
            "maxMemorySize  : 0\n" +
            "maxMemoryTimeLimit : 0\n" +
            "securityLevel  : 0\n" +
            "sessionIdleTimeout : 0\n" +
            "sessionMaxMemorySize : 0\n" +
            "sessionMaxTimeLimit : 0\n";

        var info = RacOutputParser.ToClusterInfo(output);

        Assert.Equal("Локальный кластер", info.Name);
        Assert.Equal("localhost", info.HostName);
        Assert.Equal(1541, info.Port);
        Assert.Equal(360L, info.ExpirationTimeout);
        Assert.Equal(1440L, info.LifetimeLimit);
        Assert.Equal(0L, info.MaxMemorySize);
        Assert.Equal(0L, info.MaxMemoryTimeLimit);
        Assert.Equal(0, info.SecurityLevel);
        Assert.Equal(0L, info.SessionIdleTimeout);
        Assert.Equal(0L, info.SessionMaxMemorySize);
        Assert.Equal(0L, info.SessionMaxTimeLimit);
        Assert.Equal("8b2f6f5e-6e3c-4c5a-8a9b-1c2d3e4f5a6b", info.Properties["cluster"]);
        Assert.Equal(12, info.Properties.Count);
    }

    [Fact]
    public void ToClusterInfo_EmptyOutput_ReturnsEmptyInfo()
    {
        var info = RacOutputParser.ToClusterInfo(string.Empty);

        Assert.Empty(info.Properties);
        Assert.Equal(string.Empty, info.Name);
        Assert.Equal(0, info.Port);
    }

    // ---------- Устойчивость к «кривым» данным ----------

    [Fact]
    public void TypedParsers_NeverThrow_OnGarbageInput()
    {
        // Строки без табуляций, пустые значения и мусор не должны ронять парсеры.
        Assert.Empty(RacOutputParser.ToClusters("garbage\n\n"));
        Assert.Empty(RacOutputParser.ToProcesses("\t\t\t\n"));
        Assert.Empty(RacOutputParser.ToSessions("111-222\t333-444\t\n"));
        Assert.Empty(RacOutputParser.ToConnections("111-222\n"));
        Assert.Empty(RacOutputParser.ToLocks(null!));

        Assert.NotNull(RacOutputParser.ToClusterInfo("только текст без двоеточия\n"));
    }
}