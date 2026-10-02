using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Configuration_Management.Models;

namespace Configuration_Management.Services;

/// <summary>
/// ЧИСТЫЙ статический парсер текстового вывода утилиты rac (Remote Administration Client,
/// поставляется с платформой 1С:Предприятие). Без I/O и UI — только строки.
/// Форматы вывода rac документированы на ИТС:
/// <list type="bullet">
/// <item>list-команды (cluster/process/session/connection/lock list) — таблица: первая строка —
/// заголовок колонок, поля строк разделены табуляцией <c>\t</c> (значения могут содержать пробелы);</item>
/// <item>info-команды (cluster info и т.п.) — строки «ключ: значение».</item>
/// </list>
/// Парсинг типизированных команд — позиционный по индексам колонок; лишние колонки справа
/// игнорируются. Строки с несовпадающим числом полей или невалидным идентификатором
/// пропускаются без исключения.
/// </summary>
public static class RacOutputParser
{
    /// <summary>
    /// Разделитель для fallback-разбора таблицы без табуляций: 2+ пробела подряд.
    /// Одиночный пробел внутри значения (имя кластера, строка подключения) остаётся
    /// частью поля (issue #324).
    /// </summary>
    private static readonly System.Text.RegularExpressions.Regex MultiSpaceSeparator =
        new(@" {2,}", System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>
    /// Разбивает табличный вывод rac на строки и поля (разделитель — табуляция).
    /// Fallback: если в строке нет ни одной табуляции, поля разделяются 2+ пробелами
    /// (вывод с выравниванием пробелами в некоторых окружениях/версиях — issue #324).
    /// </summary>
    public static IReadOnlyList<IReadOnlyList<string>> ParseTable(string output)
    {
        var rows = new List<IReadOnlyList<string>>();
        if (string.IsNullOrEmpty(output))
            return rows;

        var lines = SplitLines(output);
        if (lines.Count == 0)
            return rows;

        // Вывод, выровненный пробелами (некоторые версии/окружения rac не используют
        // табуляции; issue #324): позиции начала колонок берём из строки заголовка.
        // Позиционный разбор применяем ТОЛЬКО при единообразном выравнивании — когда
        // границы колонок в заголовке совпадают с границами в первой строке данных
        // (иначе индексы из заголовка резали бы значения, шире заголовка, и мы бы
        // сломали вывод с неравномерными отступами — см. существующие образцы тестов).
        IReadOnlyList<int>? columnStarts = null;
        if (lines[0].IndexOf('\t') < 0 && lines.Count > 1)
        {
            var headerStarts = TryGetColumnStarts(lines[0]);
            // Позиционный разбор корректен, если выравнивание ЕДИНООБРАЗНО: каждая
            // граница колонки из заголовка в строках данных указывает на начало
            // непробельного блока. Внутренние пробелы значения (имя кластера) дают
            // свои «переходы», но они не совпадают с границами заголовка — их
            // наличие не отключает позиционный разбор.
            if (headerStarts is not null &&
                headerStarts.Count > 1 &&
                StartsAlignWithRows(lines, headerStarts))
                columnStarts = headerStarts;
        }

        foreach (var line in lines)
        {
            IReadOnlyList<string> fields;
            if (line.IndexOf('\t') >= 0)
            {
                fields = line.Split('\t');
            }
            else if (columnStarts is not null && columnStarts.Count > 1)
            {
                fields = SplitByColumnStarts(line, columnStarts);
            }
            else
            {
                fields = MultiSpaceSeparator.Split(line.Trim());
            }
            rows.Add(fields);
        }

        return rows;
    }

    /// <summary>Делит вывод на непустые строки, убирая «\r» (CRLF из cmd/Windows).</summary>
    private static List<string> SplitLines(string output)
    {
        var lines = new List<string>();
        foreach (var rawLine in output.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            if (!string.IsNullOrWhiteSpace(line))
                lines.Add(line);
        }
        return lines;
    }

    /// <summary>
    /// Определяет позиции начала колонок в строке заголовка: колонка начинается там,
    /// где последовательность пробелов сменяется непробельным символом (первая колонка —
    /// в начале строки). Возвращает null, если колонок меньше двух (заголовок из одного
    /// слова — позиционный разбор бессмыслен, оставляем fallback по 2+ пробелам).
    /// </summary>
    private static IReadOnlyList<int>? TryGetColumnStarts(string header)
    {
        var starts = new List<int>();
        var prevWasSpace = true;
        for (var i = 0; i < header.Length; i++)
        {
            var isSpace = header[i] == ' ' || header[i] == '\t';
            if (!isSpace && prevWasSpace)
                starts.Add(i);
            prevWasSpace = isSpace;
        }
        return starts.Count > 1 ? starts : null;
    }

    /// <summary>
    /// Проверяет единообразность выравнивания: во всех строках данных (кроме заголовка)
    /// каждая граница колонки из заголовка (кроме последней) указывает на начало
    /// непробельного блока. Если выравнивание неравномерное (границы из заголовка
    /// попадают внутрь пробельного «хвоста» или за конец строки) — позиционный разбор
    /// не применяется, остаётся fallback «2+ пробела».
    /// </summary>
    private static bool StartsAlignWithRows(List<string> lines, IReadOnlyList<int> starts)
    {
        for (var rowIndex = 1; rowIndex < lines.Count; rowIndex++)
        {
            var row = lines[rowIndex];
            for (var i = 1; i < starts.Count - 1; i++)
            {
                var pos = starts[i];
                if (pos >= row.Length || row[pos] == ' ' || row[pos] == '\t')
                    return false;
            }
        }
        return true;
    }

    /// <summary>
    /// Разбивает строку данных по фиксированным позициям начала колонок (вычисленным
    /// из заголовка). Поля обрезаются; внутренние пробелы значения (имя кластера)
    /// сохраняются — в отличие от fallback «2+ пробела», который резал такие значения.
    /// </summary>
    private static IReadOnlyList<string> SplitByColumnStarts(string line, IReadOnlyList<int> starts)
    {
        var fields = new List<string>(starts.Count);
        for (var i = 0; i < starts.Count; i++)
        {
            var from = starts[i];
            var to = i + 1 < starts.Count ? starts[i + 1] : line.Length;
            if (from >= line.Length)
            {
                fields.Add(string.Empty);
                continue;
            }
            var end = Math.Min(to, line.Length);
            fields.Add(line.Substring(from, end - from).Trim());
        }
        return fields;
    }

    /// <summary>
    /// Разбирает вывод info-команды rac в словарь «ключ → значение».
    /// Ключ и значение обрезаются; разделитель — первый символ «:».
    /// Строки без разделителя и пустые строки пропускаются.
    /// </summary>
    public static IReadOnlyDictionary<string, string> ParseInfo(string output)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (string.IsNullOrEmpty(output))
            return result;

        foreach (var rawLine in output.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            if (string.IsNullOrWhiteSpace(line))
                continue;

            var colon = line.IndexOf(':');
            if (colon <= 0)
                continue;

            var key = line.Substring(0, colon).Trim();
            if (key.Length == 0)
                continue;

            var value = line.Substring(colon + 1).Trim();
            result[key] = value;
        }

        return result;
    }

    /// <summary>
    /// Разбирает вывод «cluster list» в список кластеров.
    /// Колонки: cluster, name, port (лишние справа игнорируются).
    /// </summary>
    public static IReadOnlyList<RacCluster> ToClusters(string output)
    {
        const int minColumns = 3;
        var clusters = new List<RacCluster>();

        foreach (var row in ParseTable(output))
        {
            if (row.Count < minColumns)
                continue;

            var id = ParseGuid(row[0]);
            if (id == Guid.Empty)
                continue; // строка заголовка или мусор

            clusters.Add(new RacCluster
            {
                Id = id,
                Name = row[1].Trim(),
                Port = ParseInt(row[2])
            });
        }

        return clusters;
    }

    /// <summary>
    /// Разбирает вывод «process list» в список рабочих процессов (rphost/rmngr).
    /// Колонки: cluster, process, host, pid, port, started-at, memory-size, memory-total,
    /// memory-available, memory-excess, threads, cpu, available-performances, running, infobases.
    /// Минимальный набор для парсинга — первые 5 колонок; отсутствующие справа — default.
    /// </summary>
    public static IReadOnlyList<RacProcessInfo> ToProcesses(string output)
    {
        const int minColumns = 5;
        var processes = new List<RacProcessInfo>();

        foreach (var row in ParseTable(output))
        {
            if (row.Count < minColumns)
                continue;

            var id = ParseGuid(row[1]);
            if (id == Guid.Empty)
                continue; // строка заголовка или мусор

            processes.Add(new RacProcessInfo
            {
                Id = id,
                Host = Col(row, 2).Trim(),
                Pid = ParseInt(Col(row, 3)),
                Port = ParseInt(Col(row, 4)),
                StartedAt = ParseDateTime(Col(row, 5)),
                MemorySize = ParseLong(Col(row, 6)),
                MemoryTotal = ParseLong(Col(row, 7)),
                MemoryAvailable = ParseLong(Col(row, 8)),
                MemoryExcess = ParseLong(Col(row, 9)),
                Threads = ParseInt(Col(row, 10)),
                Cpu = ParseDouble(Col(row, 11)),
                AvailablePerformances = ParseDouble(Col(row, 12)),
                Running = ParseBool(Col(row, 13)),
                Infobases = ParseInt(Col(row, 14))
            });
        }

        return processes;
    }

    /// <summary>
    /// Разбирает вывод «session list» в список сеансов.
    /// Колонки: cluster, session, infobase, user-name, host, app-id, started-at, last-active-at,
    /// blocked-by-ls, blocked-by-deadlock, db-proc-duration, duration-all, duration-current,
    /// duration-dbms, duration-cpu, duration-wait, memory, bytes, position, read, write,
    /// connection, hibernate, state. Минимальный набор — первые 3 колонки.
    /// </summary>
    public static IReadOnlyList<RacSessionInfo> ToSessions(string output)
    {
        const int minColumns = 3;
        var sessions = new List<RacSessionInfo>();

        foreach (var row in ParseTable(output))
        {
            if (row.Count < minColumns)
                continue;

            var id = ParseGuid(row[1]);
            if (id == Guid.Empty)
                continue; // строка заголовка или мусор

            sessions.Add(new RacSessionInfo
            {
                Id = id,
                InfobaseId = ParseNullableGuid(Col(row, 2)),
                User = Col(row, 3).Trim(),
                Host = Col(row, 4).Trim(),
                AppId = Col(row, 5).Trim(),
                StartedAt = ParseDateTime(Col(row, 6)),
                LastActiveAt = ParseDateTime(Col(row, 7)),
                BlockedByLs = ParseBool(Col(row, 8)),
                BlockedByDeadlock = ParseBool(Col(row, 9)),
                DbProcDuration = ParseLong(Col(row, 10)),
                DurationAll = ParseLong(Col(row, 11)),
                DurationCurrent = ParseLong(Col(row, 12)),
                DurationDbms = ParseLong(Col(row, 13)),
                DurationCpu = ParseLong(Col(row, 14)),
                DurationWait = ParseLong(Col(row, 15)),
                Memory = ParseLong(Col(row, 16)),
                Bytes = ParseLong(Col(row, 17)),
                Position = ParseLong(Col(row, 18)),
                Read = ParseLong(Col(row, 19)),
                Write = ParseLong(Col(row, 20)),
                ConnectionId = ParseGuid(Col(row, 21)),
                Hibernate = ParseBool(Col(row, 22)),
                State = Col(row, 23).Trim()
            });
        }

        return sessions;
    }

    /// <summary>
    /// Разбирает вывод «connection list» в список соединений.
    /// Колонки: cluster, connection, session, blocked, connector, process, host, port,
    /// established-at, last-connection-time, duration. Минимальный набор — первые 5 колонок.
    /// </summary>
    public static IReadOnlyList<RacConnectionInfo> ToConnections(string output)
    {
        const int minColumns = 5;
        var connections = new List<RacConnectionInfo>();

        foreach (var row in ParseTable(output))
        {
            if (row.Count < minColumns)
                continue;

            var id = ParseGuid(row[1]);
            if (id == Guid.Empty)
                continue; // строка заголовка или мусор

            connections.Add(new RacConnectionInfo
            {
                Id = id,
                SessionId = ParseGuid(Col(row, 2)),
                Blocked = ParseBool(Col(row, 3)),
                Connector = Col(row, 4).Trim(),
                ProcessId = ParseGuid(Col(row, 5)),
                Host = Col(row, 6).Trim(),
                Port = ParseInt(Col(row, 7)),
                EstablishedAt = ParseDateTime(Col(row, 8)),
                LastConnectionTime = ParseDateTime(Col(row, 9)),
                Duration = ParseLong(Col(row, 10))
            });
        }

        return connections;
    }

    /// <summary>
    /// Разбирает вывод «lock list» в список блокировок объектов данных.
    /// Колонки: cluster, lock, session, infobase, connection, transaction, waiting, blocking,
    /// object. Минимальный набор — первые 5 колонок.
    /// </summary>
    public static IReadOnlyList<RacLockInfo> ToLocks(string output)
    {
        const int minColumns = 5;
        var locks = new List<RacLockInfo>();

        foreach (var row in ParseTable(output))
        {
            if (row.Count < minColumns)
                continue;

            var id = ParseGuid(row[1]);
            if (id == Guid.Empty)
                continue; // строка заголовка или мусор

            locks.Add(new RacLockInfo
            {
                Id = id,
                SessionId = ParseGuid(Col(row, 2)),
                InfobaseId = ParseGuid(Col(row, 3)),
                ConnectionId = ParseGuid(Col(row, 4)),
                TransactionId = ParseGuid(Col(row, 5)),
                Waiting = ParseBool(Col(row, 6)),
                Blocking = ParseBool(Col(row, 7)),
                Object = Col(row, 8).Trim()
            });
        }

        return locks;
    }

    /// <summary>
    /// Разбирает вывод «infobase summary list» в список информационных баз кластера.
    /// Колонки: infobase, name, descr, dbms, db-server, db-name, db-user, locale,
    /// security-level, licensed. Минимальный набор — первые 2 колонки; отсутствующие
    /// справа — значения по умолчанию, лишние — игнорируются.
    /// </summary>
    public static IReadOnlyList<RacInfobaseSummary> ToInfobaseSummaries(string output)
    {
        const int minColumns = 2;
        var infobases = new List<RacInfobaseSummary>();

        foreach (var row in ParseTable(output))
        {
            if (row.Count < minColumns)
                continue;

            var id = ParseGuid(row[0]);
            if (id == Guid.Empty)
                continue; // строка заголовка или мусор

            infobases.Add(new RacInfobaseSummary
            {
                InfobaseId = id,
                Name = row[1].Trim(),
                Descr = Col(row, 2).Trim(),
                Dbms = Col(row, 3).Trim(),
                DbServer = Col(row, 4).Trim(),
                DbName = Col(row, 5).Trim(),
                DbUser = Col(row, 6).Trim(),
                Locale = Col(row, 7).Trim(),
                SecurityLevel = ParseInt(Col(row, 8)),
                Licensed = ParseBool(Col(row, 9))
            });
        }

        return infobases;
    }

    /// <summary>
    /// Разбирает вывод «job list» в список регламентных заданий кластера.
    /// Колонки: cluster, job, infobase, name, method-name, predefined, schedule, state,
    /// started-at, next-start, last-start, last-end, last-success, last-error,
    /// last-error-descr, process, …, result. Минимальный набор — первые 3 колонки
    /// (cluster, job, infobase); отсутствующие справа — значения по умолчанию, лишние —
    /// игнорируются. У строковых полей снимаются обрамляющие двойные кавычки (rac может
    /// заключать значения с пробелами — например cron-расписание — в кавычки).
    /// </summary>
    public static IReadOnlyList<RacJobInfo> ToJobs(string output)
    {
        const int minColumns = 3;
        var jobs = new List<RacJobInfo>();

        foreach (var row in ParseTable(output))
        {
            if (row.Count < minColumns)
                continue;

            var id = ParseGuid(row[1]);
            if (id == Guid.Empty)
                continue; // строка заголовка или мусор

            jobs.Add(new RacJobInfo
            {
                Id = id,
                InfobaseId = ParseNullableGuid(Col(row, 2)),
                Name = Unquote(Col(row, 3)),
                MethodName = Unquote(Col(row, 4)),
                Predefined = ParseBool(Col(row, 5)),
                Schedule = Unquote(Col(row, 6)),
                State = Unquote(Col(row, 7)),
                StartedAt = ParseDateTime(Col(row, 8)),
                NextStart = ParseDateTime(Col(row, 9)),
                LastStart = ParseDateTime(Col(row, 10)),
                LastEnd = ParseDateTime(Col(row, 11)),
                LastSuccess = ParseBool(Col(row, 12)),
                LastError = ParseBool(Col(row, 13)),
                LastErrorDescr = Unquote(Col(row, 14)),
                ProcessId = ParseGuid(Col(row, 15)),
                Result = Unquote(Col(row, 21))
            });
        }

        return jobs;
    }

    /// <summary>
    /// Разбирает вывод «cluster info» (формат «ключ: значение») в <see cref="RacClusterInfo"/>:
    /// сохраняет полный словарь свойств и заполняет типизированные частые поля.
    /// </summary>
    public static RacClusterInfo ToClusterInfo(string output)
    {
        var properties = ParseInfo(output);

        return new RacClusterInfo
        {
            Properties = properties,
            Name = Get(properties, "name"),
            HostName = Get(properties, "hostName"),
            Port = ParseInt(Get(properties, "port")),
            ExpirationTimeout = ParseLong(Get(properties, "expirationTimeout")),
            LifetimeLimit = ParseLong(Get(properties, "lifetimeLimit")),
            MaxMemorySize = ParseLong(Get(properties, "maxMemorySize")),
            MaxMemoryTimeLimit = ParseLong(Get(properties, "maxMemoryTimeLimit")),
            SecurityLevel = ParseInt(Get(properties, "securityLevel")),
            SessionIdleTimeout = ParseLong(Get(properties, "sessionIdleTimeout")),
            SessionMaxMemorySize = ParseLong(Get(properties, "sessionMaxMemorySize")),
            SessionMaxTimeLimit = ParseLong(Get(properties, "sessionMaxTimeLimit"))
        };
    }

    /// <summary>Значение по ключу без учёта регистра или пустая строка.</summary>
    private static string Get(IReadOnlyDictionary<string, string> properties, string key)
    {
        foreach (var pair in properties)
        {
            if (string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase))
                return pair.Value;
        }

        return string.Empty;
    }

    /// <summary>Значение колонки с учётом «лишних колонок справа»: за пределами — пустая строка.</summary>
    private static string Col(IReadOnlyList<string> row, int index) =>
        index < row.Count ? row[index] : string.Empty;

    /// <summary>
    /// Снимает обрамляющие двойные кавычки со значения (rac может заключать поля
    /// с пробелами — например cron-расписание — в кавычки). Некавыченные значения
    /// возвращаются без изменений.
    /// </summary>
    private static string Unquote(string value)
    {
        var v = value.Trim();
        if (v.Length >= 2 && v[0] == '"' && v[v.Length - 1] == '"')
            return v.Substring(1, v.Length - 2).Trim();
        return v;
    }

    private static Guid ParseGuid(string value) =>
        Guid.TryParse(value.Trim(), out var guid) ? guid : Guid.Empty;

    private static Guid? ParseNullableGuid(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        return Guid.TryParse(value.Trim(), out var guid) ? guid : null;
    }

    private static bool ParseBool(string value)
    {
        var v = value.Trim();
        if (v is "1" or "true" or "True" or "TRUE")
            return true;
        if (v is "0" or "false" or "False" or "FALSE")
            return false;
        return false;
    }

    private static int ParseInt(string value) =>
        int.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var result)
            ? result
            : 0;

    private static long ParseLong(string value) =>
        long.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var result)
            ? result
            : 0L;

    private static double ParseDouble(string value) =>
        double.TryParse(value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var result)
            ? result
            : 0d;

    private static DateTime ParseDateTime(string value)
    {
        // rac отдаёт даты в фиксированном формате (например 2026-09-28T14:00:00),
        // но для устойчивости используем TryParse с инвариантной культурой.
        if (DateTime.TryParse(
                value.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            return date;

        return default;
    }
}