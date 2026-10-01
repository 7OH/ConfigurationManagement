using Configuration_Management.Models;

namespace Configuration_Management.Services;

/// <summary>
/// Цель диагностики сети (функция 12, этап 0.3.9.230): хост и стартовый список
/// портов для первого прогона окна. Для базы клиент-серверной — адрес и порт
/// кластера из настроек подключения; для веб-базы — хост и порт веб-сервера
/// из URL публикации; для монитора серверов — адрес:порт текущего подключения.
/// </summary>
public sealed record NetworkDiagnosticsTarget(string Host, IReadOnlyList<int> Ports);

/// <inheritdoc cref="NetworkDiagnosticsTarget"/>
public static class NetworkDiagnosticsTargets
{
    /// <summary>
    /// Цель из базы списка. File → null (диагностика сети недоступна, пункт меню скрыт);
    /// пустой сервер/битый или пустой WebUrl → null.
    /// </summary>
    public static NetworkDiagnosticsTarget? FromInfobase(Infobase? infobase)
    {
        if (infobase?.Connection is null)
            return null;

        var connection = infobase.Connection;
        switch (connection.Type)
        {
            case ConnectionType.ClientServer:
            {
                var server = connection.Server?.Trim() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(server))
                    return null;

                // Server может содержать порт («srv:1540»), как в строке подключения 1С —
                // разбираем единым парсером (порт из настройки базы имеет приоритет).
                var defaultPort = connection.Port > 0 ? connection.Port : OneCPorts.Cluster;
                var parsed = NetworkDiagnosticsService.ParseAddress(server, defaultPort);
                if (parsed is null)
                    return null;
                return new NetworkDiagnosticsTarget(parsed.Host, new[] { parsed.Port });
            }

            case ConnectionType.WebServer:
            {
                var url = connection.WebUrl?.Trim();
                if (string.IsNullOrWhiteSpace(url) ||
                    !Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
                    string.IsNullOrWhiteSpace(uri.Host))
                    return null;

                // DnsSafeHost: hostname в нижнем регистре, IPv6 без квадратных скобок.
                var host = uri.DnsSafeHost;
                if (string.IsNullOrWhiteSpace(host))
                    return null;
                return new NetworkDiagnosticsTarget(host, new[] { uri.Port });
            }

            default:
                return null;
        }
    }

    /// <summary>
    /// Цель из монитора серверов (адрес:порт текущего подключения rac).
    /// Пустой адрес — localhost; порт ≤ 0 — порт агента 1540.
    /// </summary>
    public static NetworkDiagnosticsTarget FromServerMonitor(string? address, int port)
    {
        var host = (address ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(host))
            host = "localhost";

        var targetPort = port > 0 ? port : OneCPorts.Agent;
        return new NetworkDiagnosticsTarget(host, new[] { targetPort });
    }
}