using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Configuration_Management.Models;
using Configuration_Management.Services.EventLog;
using Microsoft.Data.Sqlite;
using Xunit;

namespace ConfigurationManagement.Tests;

/// <summary>
/// Тесты чтения журнала регистрации в формате SQLite (.lgd): маппинг полей
/// eventLog и справочников, постраничная выборка, толерантность к отсутствию
/// справочников/колонок и к повреждённым файлам.
/// </summary>
public sealed class EventLogSqliteReaderTests
{
    private static readonly DateTime SampleTime = new(2026, 9, 29, 12, 30, 15, DateTimeKind.Local);

    [Fact]
    public void CanRead_SqliteLocation_True()
    {
        using var fixture = Fixture.Create(withLookups: true);
        var reader = new SqliteLgdReader();
        Assert.True(reader.CanRead(fixture.Location));
    }

    [Fact]
    public void CanRead_OtherFormat_False()
    {
        var location = new JournalLocation
        {
            LogDir = "dir",
            Format = LgdFormat.Sequential,
            MainFile = "C:\\1Cv8.lgf"
        };
        Assert.False(new SqliteLgdReader().CanRead(location));
    }

    [Fact]
    public void CountRows_ReturnsAllRows()
    {
        using var fixture = Fixture.Create(withLookups: true);
        var reader = new SqliteLgdReader();
        Assert.Equal(3, reader.CountRows(fixture.Location));
    }

    [Fact]
    public void CountRows_EmptyDatabase_ReturnsZero()
    {
        using var fixture = Fixture.CreateEmpty();
        var reader = new SqliteLgdReader();
        Assert.Equal(0, reader.CountRows(fixture.Location));
    }

    [Fact]
    public void CountRows_MissingEventTable_ReturnsZero()
    {
        using var fixture = Fixture.CreateWithoutEventTable();
        var reader = new SqliteLgdReader();
        Assert.Equal(0, reader.CountRows(fixture.Location));
    }

    [Fact]
    public void ReadPage_MapsAllFields_FromTablesAndLookups()
    {
        using var fixture = Fixture.Create(withLookups: true);
        var reader = new SqliteLgdReader();
        var entry = reader.ReadPage(fixture.Location, 0, 10).First();

        Assert.Equal(SampleTime, entry.Timestamp);
        Assert.Equal(EventLogTransactionStatus.Committed, entry.TransactionStatus);
        Assert.Equal("Иванов", entry.User);
        Assert.Equal("WS-01", entry.Computer);
        Assert.Equal("1CV8", entry.Application);
        Assert.Equal(5, entry.EventCode);
        Assert.Equal("$Data.Update", entry.EventName);
        Assert.Equal(EventLogSeverity.Error, entry.Severity);
        Assert.Equal("Проведение документа", entry.Comment);
        Assert.Equal("Справочник", entry.MetadataType);
        Assert.Equal("Контрагенты", entry.MetadataName);
        Assert.Equal("{\"d\":1}", entry.Data);
        Assert.Equal(fixture.LgdPath, entry.SourceFile);
        Assert.Equal(1, entry.RowId);
    }

    [Fact]
    public void ReadPage_OrdersByRowId()
    {
        using var fixture = Fixture.Create(withLookups: true);
        var reader = new SqliteLgdReader();
        var rows = reader.ReadPage(fixture.Location, 0, 100).ToList();

        Assert.Equal(3, rows.Count);
        Assert.Equal([1, 2, 3], rows.Select(r => r.RowId).ToArray());
    }

    [Fact]
    public void ReadPage_SkipTake_ReturnsWindow()
    {
        using var fixture = Fixture.Create(withLookups: true); // 3 записи
        var reader = new SqliteLgdReader();

        var page = reader.ReadPage(fixture.Location, 1, 2).ToList();
        Assert.Equal([2, 3], page.Select(r => r.RowId).ToArray());

        var beyond = reader.ReadPage(fixture.Location, 10, 5).ToList();
        Assert.Empty(beyond);

        var empty = reader.ReadPage(fixture.Location, 0, 0).ToList();
        Assert.Empty(empty);
    }

    [Fact]
    public void ReadPage_WithoutLookupTables_FallsBackToRawCodes()
    {
        using var fixture = Fixture.Create(withLookups: false);
        var reader = new SqliteLgdReader();
        var entry = reader.ReadPage(fixture.Location, 0, 10).First();

        Assert.Equal("#1", entry.User);
        Assert.Equal("#1", entry.Computer);
        Assert.Equal("#1", entry.Application);
        Assert.Equal("#5", entry.EventName);
        Assert.Equal(string.Empty, entry.MetadataName);
    }

    [Fact]
    public void ReadPage_MissingOptionalColumns_Tolerated()
    {
        using var fixture = Fixture.CreateReducedSchema();
        var reader = new SqliteLgdReader();
        var entry = reader.ReadPage(fixture.Location, 0, 10).Single();

        // Нет колонок transactionStatus/data/dataPresentation — поля по умолчанию;
        // важность присутствует (severityCode = 2 → ошибка).
        Assert.Equal(EventLogTransactionStatus.None, entry.TransactionStatus);
        Assert.Equal(string.Empty, entry.Data);
        Assert.Equal(SampleTime, entry.Timestamp);
        Assert.Equal("Иванов", entry.User);
        Assert.Equal(EventLogSeverity.Error, entry.Severity);
    }

    [Fact]
    public void ReadPage_ReportsProgress()
    {
        using var fixture = Fixture.Create(withLookups: true); // 3 записи
        var reader = new SqliteLgdReader();
        var progress = new SyncProgress();
        var rows = reader.ReadPage(fixture.Location, 0, 3, progress).ToList();

        // Progress<T> асинхронно постит в SynchronizationContext — для детерминизма
        // используем синхронную реализацию IProgress.
        Assert.Equal(3, rows.Count);
        Assert.Equal(3, progress.Values.Count);
        Assert.Contains(progress.Values, v => v > 0);
        Assert.Contains(progress.Values, v => Math.Abs(v - 1.0) < 1e-9);
    }

    [Fact]
    public void CountRows_GarbageFile_ThrowsInvalidData()
    {
        using var fixture = Fixture.CreateGarbageFile();
        var reader = new SqliteLgdReader();
        Assert.Throws<InvalidDataException>(() => reader.CountRows(fixture.Location));
    }

    [Fact]
    public void ReadPage_NotSqliteLocation_Throws()
    {
        var location = new JournalLocation
        {
            LogDir = "dir",
            Format = LgdFormat.Sequential,
            MainFile = "C:\\1Cv8.lgf"
        };
        var reader = new SqliteLgdReader();
        Assert.Throws<InvalidOperationException>(() => reader.ReadPage(location, 0, 10).ToList());
    }

    [Fact]
    public void ReadPage_MissingFile_Throws()
    {
        var location = new JournalLocation
        {
            LogDir = "dir",
            Format = LgdFormat.Sqlite,
            MainFile = Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.lgd")
        };
        var reader = new SqliteLgdReader();
        Assert.Throws<InvalidOperationException>(() => reader.ReadPage(location, 0, 10).ToList());
    }

    [Fact]
    public void FromPlatformDate_RoundTripsKnownValue()
    {
        // Значение в единицах платформы = Ticks / 1000 (интервалы по 100 мкс).
        var value = SampleTime.Ticks / 1000L;
        var converted = SqliteLgdReader.FromPlatformDate(value);
        Assert.Equal(SampleTime, converted);
    }

    [Fact]
    public void FromPlatformDate_InvalidValues_ReturnsMinValue()
    {
        Assert.Equal(DateTime.MinValue, SqliteLgdReader.FromPlatformDate(0));
        Assert.Equal(DateTime.MinValue, SqliteLgdReader.FromPlatformDate(-5));
        Assert.Equal(DateTime.MinValue, SqliteLgdReader.FromPlatformDate(long.MaxValue));
    }

    /// <summary>Синхронная реализация прогресса для детерминированных тестов.</summary>
    private sealed class SyncProgress : IProgress<double>
    {
        public List<double> Values { get; } = new();

        public void Report(double value) => Values.Add(value);
    }

    /// <summary>Расположение temp-базы + путь к ней; удаляется при Dispose.</summary>
    private sealed class Fixture : IDisposable
    {
        public JournalLocation Location { get; }
        public string LgdPath { get; }
        private readonly string _dir;

        private Fixture(string dir, string path)
        {
            _dir = dir;
            LgdPath = path;
            Location = new JournalLocation
            {
                LogDir = dir,
                Format = LgdFormat.Sqlite,
                MainFile = path,
                DataFiles = new[] { path }
            };
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_dir))
                    Directory.Delete(_dir, recursive: true);
            }
            catch (IOException)
            {
                // Окна могут держать файл; тест не должен падать из-за очистки.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        public static Fixture Create(bool withLookups)
        {
            var (dir, path) = CreateTempDir();
            using var conn = OpenCreate(path);
            Exec(conn, """
                CREATE TABLE eventLog (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    date INTEGER NOT NULL,
                    transactionStatus INTEGER NOT NULL DEFAULT 0,
                    userCode INTEGER, computerCode INTEGER, appCode INTEGER,
                    eventCode INTEGER, severityCode INTEGER,
                    comment TEXT, metadataCode INTEGER, data TEXT, dataPresentation TEXT
                );
                """);
            InsertSamples(conn);

            if (withLookups)
            {
                Exec(conn, "CREATE TABLE userCodes (code INTEGER PRIMARY KEY, name TEXT);");
                Exec(conn, "CREATE TABLE computerCodes (code INTEGER PRIMARY KEY, name TEXT);");
                Exec(conn, "CREATE TABLE appCodes (code INTEGER PRIMARY KEY, name TEXT);");
                Exec(conn, "CREATE TABLE eventCodes (code INTEGER PRIMARY KEY, name TEXT);");
                Exec(conn, "CREATE TABLE metadataCodes (code INTEGER PRIMARY KEY, name TEXT, type TEXT);");
                Exec(conn, "INSERT INTO userCodes VALUES (1, 'Иванов'), (2, 'Петров'), (3, 'Сидоров');");
                Exec(conn, "INSERT INTO computerCodes VALUES (1, 'WS-01'), (2, 'WS-02'), (3, 'WS-03');");
                Exec(conn, "INSERT INTO appCodes VALUES (1, '1CV8'), (2, 'WebClient');");
                Exec(conn, "INSERT INTO eventCodes VALUES (5, '$Data.Update'), (10, '$Session.Authentication');");
                Exec(conn, "INSERT INTO metadataCodes VALUES (2, 'Контрагенты', 'Справочник');");
            }

            return new Fixture(dir, path);
        }

        public static Fixture CreateEmpty()
        {
            var (dir, path) = CreateTempDir();
            using var conn = OpenCreate(path);
            Exec(conn, "CREATE TABLE eventLog (id INTEGER PRIMARY KEY AUTOINCREMENT, date INTEGER NOT NULL);");
            return new Fixture(dir, path);
        }

        public static Fixture CreateWithoutEventTable()
        {
            var (dir, path) = CreateTempDir();
            using var conn = OpenCreate(path);
            Exec(conn, "CREATE TABLE otherTable (x INTEGER);");
            return new Fixture(dir, path);
        }

        /// <summary>Минимальная схема: без транзакций, данных и даты презентации.</summary>
        public static Fixture CreateReducedSchema()
        {
            var (dir, path) = CreateTempDir();
            using var conn = OpenCreate(path);
            Exec(conn, """
                CREATE TABLE eventLog (
                    date INTEGER NOT NULL,
                    userCode INTEGER,
                    computerCode INTEGER,
                    appCode INTEGER,
                    eventCode INTEGER,
                    severityCode INTEGER,
                    comment TEXT,
                    metadataCode INTEGER
                );
                """);
            Exec(conn, "CREATE TABLE userCodes (code INTEGER PRIMARY KEY, name TEXT);");
            Exec(conn, "CREATE TABLE eventCodes (code INTEGER PRIMARY KEY, name TEXT);");
            Exec(conn, "CREATE TABLE metadataCodes (code INTEGER PRIMARY KEY, name TEXT, type TEXT);");
            Exec(conn, "INSERT INTO userCodes VALUES (1, 'Иванов');");
            Exec(conn, "INSERT INTO eventCodes VALUES (5, '$Data.Update');");
            Exec(conn, "INSERT INTO metadataCodes VALUES (2, 'Контрагенты', 'Справочник');");

            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO eventLog (date, userCode, computerCode, appCode, eventCode, severityCode, comment, metadataCode)
                VALUES (@date, 1, 1, 1, 5, 2, 'Комментарий', 2);
                """;
            cmd.Parameters.AddWithValue("@date", PlatformDate(SampleTime));
            cmd.ExecuteNonQuery();

            return new Fixture(dir, path);
        }

        public static Fixture CreateGarbageFile()
        {
            var (dir, path) = CreateTempDir();
            File.WriteAllBytes(path, new byte[] { 0x47, 0x41, 0x52, 0x42, 0x41, 0x47, 0x45 }); // не SQLite
            return new Fixture(dir, path);
        }

        private static void InsertSamples(SqliteConnection conn)
        {
            long d1 = PlatformDate(SampleTime);
            long d2 = PlatformDate(SampleTime.AddMinutes(1));
            long d3 = PlatformDate(SampleTime.AddMinutes(2));

            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO eventLog (date, transactionStatus, userCode, computerCode, appCode, eventCode, severityCode, comment, metadataCode, data, dataPresentation)
                VALUES
                    (@d1, 2, 1, 1, 1, 5, 2, 'Проведение документа', 2, '{"d":1}', 'Документ.Продажа'),
                    (@d2, 0, 2, 2, 1, 10, 0, 'Вход в систему', NULL, NULL, NULL),
                    (@d3, 3, 3, 3, 2, 5, 1, 'Откат транзакции', NULL, NULL, NULL);
                """;
            cmd.Parameters.AddWithValue("@d1", d1);
            cmd.Parameters.AddWithValue("@d2", d2);
            cmd.Parameters.AddWithValue("@d3", d3);
            cmd.ExecuteNonQuery();
        }

        private static (string Dir, string Path) CreateTempDir()
        {
            var dir = Path.Combine(Path.GetTempPath(), $"lgd-test-{Guid.NewGuid():N}");
            Directory.CreateDirectory(dir);
            return (dir, Path.Combine(dir, "1Cv8.lgd"));
        }

        private static SqliteConnection OpenCreate(string path)
        {
            var conn = new SqliteConnection($"Data Source={path};Mode=ReadWriteCreate");
            conn.Open();
            return conn;
        }

        private static void Exec(SqliteConnection conn, string sql)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            cmd.ExecuteNonQuery();
        }

        private static long PlatformDate(DateTime dt) => dt.Ticks / 1000L;
    }
}