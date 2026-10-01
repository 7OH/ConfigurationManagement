namespace Configuration_Management.Models;

/// <summary>
/// Состояние проверки порта (диагностика сети до сервера 1С, функция 12).
/// </summary>
public enum DiagnosticPortState
{
    /// <summary>Проверка не выполнялась (DNS-ошибка, пустой список портов).</summary>
    NotChecked,

    /// <summary>TCP-соединение установлено (порт открыт).</summary>
    Available,

    /// <summary>Соединение отклонено (порт закрыт: сервис не слушает).</summary>
    Closed,

    /// <summary>Истёк таймаут соединения (файрвол/потеря сети).</summary>
    Timeout
}

/// <summary>
/// Результат проверки одного порта: состояние, задержка установки TCP-соединения (RTT)
/// и машинный код причины ошибки (для подсказок на этапе построения выводов).
/// </summary>
public sealed record NetworkPortProbe(
    int Port,
    string ServiceKey,          // ключ локализации имени сервиса (Diagnostics.PortAgent / PortRas / PortCluster / PortCustom / PortBase)
    DiagnosticPortState State,
    long? RttMs,                // задержка установки TCP-соединения, мс; null при ошибке
    string? ErrorCode);         // "dns_error", "refused", "timeout", "socket_error", null — успех

/// <summary>
/// Сводный результат диагностики хоста (функция 12): парсинг адреса, резолв DNS,
/// ICMP-пинг (может быть «не проверено» — нет прав на Linux), TCP-проверка портов.
/// Чистая модель без UI-зависимостей — обе платформы.
/// </summary>
public sealed class NetworkDiagnosticsResult
{
    /// <summary>Хост после разбора адреса (без порта, без скобок IPv6).</summary>
    public string Host { get; init; } = string.Empty;

    /// <summary>true — адрес распознан парсером; false — невалидный адрес/пустая строка.</summary>
    public bool HostValid { get; init; }

    /// <summary>true — имя разрешено (или хост — IP-адрес и резолв не требовался).</summary>
    public bool DnsOk { get; init; }

    /// <summary>"dns_failed" при ошибке резолва; null — ОК.</summary>
    public string? DnsErrorCode { get; init; }

    /// <summary>Разрешённые IP-адреса (IPv4/IPv6); при host=IP — сам адрес.</summary>
    public IReadOnlyList<string> ResolvedAddresses { get; init; } = Array.Empty<string>();

    /// <summary>false — ICMP недоступен (нет прав на Linux) и пинг пропущен.</summary>
    public bool PingPerformed { get; init; }

    /// <summary>Ответил ли хост на ICMP (значимо при <see cref="PingPerformed"/>).</summary>
    public bool PingOk { get; init; }

    /// <summary>Задержка ответа на ICMP, мс; null — нет ответа/не проверялось.</summary>
    public long? PingRttMs { get; init; }

    /// <summary>"ping_permission", "ping_error", "timeout", "no_reply"; null — ОК.</summary>
    public string? PingErrorCode { get; init; }

    /// <summary>Результаты TCP-проверки портов (в заданном порядке).</summary>
    public IReadOnlyList<NetworkPortProbe> Ports { get; init; } = Array.Empty<NetworkPortProbe>();
}

/// <summary>
/// Разобранный адрес диагностики: хост и порт. Порт — контекстный (по умолчанию
/// 1541 — порт кластера); для быстрых проверок из IsBaseAvailable используется
/// отдельно от списка портов RunAsync.
/// </summary>
public sealed record NetworkAddress(string Host, int Port);

/// <summary>
/// Стандартные порты 1С:Предприятие 8.3 (функция 12). 1540 — агент сервера (ragent),
/// точка входа rac; 1541 — порт кластера по умолчанию (rphost), фактический порт
/// кластера может отличаться; 1542 — сервер хранилища конфигурации (1cv8 8.3.x);
/// 1545 — сервер администрирования (RAS).
/// </summary>
public static class OneCPorts
{
    /// <summary>Порт агента сервера 1С (ragent).</summary>
    public const int Agent = 1540;

    /// <summary>Порт кластера 1С по умолчанию.</summary>
    public const int Cluster = 1541;

    /// <summary>Порт сервера хранилища конфигурации (issue #335).</summary>
    public const int Repository = 1542;

    /// <summary>Порт сервера администрирования RAS.</summary>
    public const int Ras = 1545;

    /// <summary>
    /// Ключ локализации имени сервиса для порта: известные порты — именованные,
    /// прочие — «порт базы/пользовательский». Используется на этапе построения строк
    /// таблицы портов (VM/окно), сама локализация здесь не вызывается (чистая логика).
    /// </summary>
    public static string GetServiceKey(int port) => port switch
    {
        Agent => "Diagnostics.PortAgent",
        Cluster => "Diagnostics.PortCluster",
        Repository => "Diagnostics.PortRepository",
        Ras => "Diagnostics.PortRas",
        _ => "Diagnostics.PortCustom"
    };
}