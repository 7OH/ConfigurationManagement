using System;
using System.Collections.Generic;
using System.Linq;
using Configuration_Management.Models;

namespace Configuration_Management.Services;

/// <summary>
/// Чистая логика импорта информационных баз из кластера 1С (цикл 0.3.9.172–0.3.9.175):
/// маппинг записи «infobase summary list» (<see cref="RacInfobaseSummary"/>) в базу списка
/// приложения (<see cref="Infobase"/>/<see cref="ConnectionSettings"/>) — клиент-серверное
/// подключение с хостом кластера и его портом, — а также дедупликация по строке подключения.
/// Не зависит от платформы и UI — покрыта тестами.
/// </summary>
public static class RacInfobaseMapper
{
    /// <summary>
    /// Преобразует запись кластера в клиент-серверную базу списка приложения.
    /// Хост строки подключения — <paramref name="clusterHostName"/> (из «cluster info»,
    /// корректен для удалённого RAS); при пустом значении — host-часть введённого адреса
    /// <paramref name="serverAddress"/> (порт RAS убирается). Порт подключения клиентов —
    /// <paramref name="clusterPort"/> (порт КЛАСТЕРА, не ragent/RAS). Имя базы 1С
    /// (<c>Ref</c>) — имя в кластере; учётные данные не переносятся
    /// (<see cref="AuthenticationMode.Prompt"/>).
    /// </summary>
    /// <param name="source">Запись «infobase summary list» кластера.</param>
    /// <param name="serverAddress">Адрес, введённый пользователем для подключения к ragent/RAS (host или host:port).</param>
    /// <param name="clusterPort">Порт кластера из «cluster list» (по умолчанию 1541).</param>
    /// <param name="clusterHostName">Хост кластера из «cluster info» (hostName); null/пусто — fallback на адрес.</param>
    /// <param name="groupName">Группа, в которую добавляется база (обычно имя кластера).</param>
    public static Infobase ToInfobase(RacInfobaseSummary source,
        string serverAddress, int clusterPort, string? clusterHostName, string groupName)
    {
        return new Infobase
        {
            Name = source.Name,
            Group = groupName,
            Id = string.Empty, // GUID кластера не переносится: это ключ списка 1С ibases.v8i
            Connection = new ConnectionSettings
            {
                Type = ConnectionType.ClientServer,
                Server = ResolveHost(serverAddress, clusterHostName),
                Port = clusterPort,
                DatabaseName = source.Name,
                AuthenticationMode = AuthenticationMode.Prompt,
                BlockScheduledJobs = false,
                ForbidSpeechRecognition = false
            }
        };
    }

    /// <summary>
    /// Нормализованная строка подключения базы — ключ дедупликации при импорте.
    /// Строка подключения уже нормализует регистр-независимые различия неявно:
    /// сравнение ключей выполняется без учёта регистра (<see cref="IsDuplicate"/>),
    /// а порт 1541 по умолчанию в строку не выводится.
    /// </summary>
    public static string ConnectionKey(Infobase ib) =>
        (ib.Connection?.ToConnectionString() ?? string.Empty).Trim();

    /// <summary>
    /// Проверяет, есть ли база-кандидат в списке существующих: дубликатом считается база
    /// с совпадающей строкой подключения (без учёта регистра хоста/имени). Одноимённые базы
    /// разных кластеров (разные порты) дают разные строки подключения — не дубликаты.
    /// Эквивалентность хостов localhost ↔ 127.0.0.1 не распознаётся (документированный риск).
    /// </summary>
    public static bool IsDuplicate(IEnumerable<Infobase> existing, Infobase candidate)
    {
        ArgumentNullException.ThrowIfNull(existing);
        ArgumentNullException.ThrowIfNull(candidate);

        var existingKeys = new HashSet<string>(
            existing.Select(ConnectionKey),
            StringComparer.OrdinalIgnoreCase);

        return existingKeys.Contains(ConnectionKey(candidate));
    }

    /// <summary>
    /// Хост строки подключения: приоритет у <paramref name="clusterHostName"/> (из «cluster
    /// info», корректен, когда RAS установлен отдельно от кластера); иначе — host-часть
    /// введённого адреса с удалённым портом RAS (через
    /// <see cref="ConnectionSettings.ParseServerAndPort"/>, порт из неё НЕ берётся).
    /// </summary>
    private static string ResolveHost(string serverAddress, string? clusterHostName)
    {
        var host = (clusterHostName ?? string.Empty).Trim();
        if (host.Length > 0)
            return host;

        // IPv6 в квадратных скобках ([2001:db8::1]:1545) сохраняется с ними — так
        // строку подключения 1С читает ParseServerAndPort и GetServerWithPort.
        var parsed = new ConnectionSettings();
        ConnectionSettings.ParseServerAndPort(serverAddress, parsed);
        return parsed.Server.Trim();
    }
}