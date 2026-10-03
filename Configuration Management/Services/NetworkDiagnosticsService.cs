using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Configuration_Management.Models;

namespace Configuration_Management.Services;

/// <summary>
/// Результат ICMP-зонда (функция 12). <see cref="Performed"/>=false — пинг не
/// выполнялся (нет прав на raw-socket на Linux и т.п.) — TCP остаётся основным
/// критерием, «не проверено» не считается ошибкой.
/// </summary>
public sealed record PingProbeResult(bool Performed, bool Ok, long? RttMs, string? ErrorCode);

/// <summary>
/// Результат TCP-зонда порта (функция 12): достижимость, RTT установки соединения
/// и машинный код причины ("refused", "timeout", "socket_error"; null — успех).
/// </summary>
public sealed record TcpProbeResult(bool Reachable, long? RttMs, string? ErrorCode);

/// <summary>
/// Сервис сетевой диагностики сервера 1С (функция 12, цикл 0.3.9.229–233).
/// Чистый сервис без UI-зависимостей — обе платформы. Три независимых шага:
/// резолв DNS → ICMP-пинг (переносимый, см. <see cref="PingProbeResult.Performed"/>) →
/// TCP-проверка портов параллельно с измерением RTT. Низкоуровневые операции
/// инжектируются делегатами (образец — PlatformUpdateViewModel), чтобы юнит-тесты
/// не зависели от реальной сети.
/// </summary>
public interface INetworkDiagnosticsService
{
    /// <summary>Полный прогон: парсинг адреса → DNS → ICMP → TCP по каждому порту.</summary>
    /// <param name="address">Адрес: "host", "host:port", "[IPv6]:port". Порт в адресе
    /// не используется для проверок — только хост (порты передаются списком).</param>
    /// <param name="ports">Порты для TCP-проверки; пустой список — только DNS+ICMP.</param>
    /// <param name="timeoutMs">Таймаут ICMP и каждого TCP-зонда, мс.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <param name="serviceKeys">Ключи локализации имён сервисов (по одному на порт,
    /// issue #335); null — имя по значению порта через <see cref="OneCPorts.GetServiceKey"/>.
    /// Для наборов из карты портов 1С (агент/кластер/хранилище/RAS) ключи передаются
    /// явно, иначе в колонке «Сервис» у нестандартных портов показывался бы «порт».</param>
    Task<NetworkDiagnosticsResult> RunAsync(
        string address,
        IReadOnlyList<int> ports,
        int timeoutMs = 3000,
        CancellationToken cancellationToken = default,
        IReadOnlyList<string>? serviceKeys = null);
}

/// <inheritdoc cref="INetworkDiagnosticsService"/>
public sealed class NetworkDiagnosticsService : INetworkDiagnosticsService
{
    private readonly Func<string, CancellationToken, Task<IReadOnlyList<string>>> _resolveHost;
    private readonly Func<string, int, CancellationToken, Task<PingProbeResult>> _ping;
    private readonly Func<string, int, int, CancellationToken, Task<TcpProbeResult>> _tcpProbe;

    /// <param name="resolveHost">Резолв имени; null — Dns.GetHostAddressesAsync.</param>
    /// <param name="ping">ICMP-зонд; null — System.Net.NetworkInformation.Ping.</param>
    /// <param name="tcpProbe">TCP-зонд; null — TcpClient.ConnectAsync + Stopwatch.</param>
    public NetworkDiagnosticsService(
        Func<string, CancellationToken, Task<IReadOnlyList<string>>>? resolveHost = null,
        Func<string, int, CancellationToken, Task<PingProbeResult>>? ping = null,
        Func<string, int, int, CancellationToken, Task<TcpProbeResult>>? tcpProbe = null)
    {
        _resolveHost = resolveHost ?? ResolveHostAsync;
        _ping = ping ?? PingAsync;
        _tcpProbe = tcpProbe ?? TcpPortCheckAsync;
    }

    /// <inheritdoc />
    public async Task<NetworkDiagnosticsResult> RunAsync(
        string address,
        IReadOnlyList<int> ports,
        int timeoutMs = 3000,
        CancellationToken cancellationToken = default,
        IReadOnlyList<string>? serviceKeys = null)
    {
        var parsed = ParseAddress(address);
        if (parsed is null)
            return new NetworkDiagnosticsResult
            {
                Host = (address ?? string.Empty).Trim(),
                HostValid = false
            };

        // ---- Шаг 1: резолв DNS (для IP-адреса пропускается). ----
        var dnsSkipped = IPAddress.TryParse(parsed.Host, out _);
        IReadOnlyList<string> resolvedAddresses = dnsSkipped
            ? new[] { parsed.Host }
            : Array.Empty<string>();
        var dnsOk = true;
        string? dnsErrorCode = null;

        if (!dnsSkipped)
        {
            try
            {
                resolvedAddresses = await _resolveHost(parsed.Host, cancellationToken)
                    .ConfigureAwait(false);
                if (resolvedAddresses.Count == 0)
                {
                    dnsOk = false;
                    dnsErrorCode = "dns_failed";
                }
            }
            catch
            {
                dnsOk = false;
                dnsErrorCode = "dns_failed";
            }
        }

        // ---- Шаг 2: ICMP-пинг (не роняет проверку при отсутствии прав). ----
        var pingResult = new PingProbeResult(false, false, null, null);
        if (dnsOk)
        {
            try
            {
                pingResult = await _ping(parsed.Host, timeoutMs, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch
            {
                pingResult = new PingProbeResult(false, false, null, "ping_error");
            }
        }

        // ---- Шаг 3: TCP-проверка портов (параллельно; только при успешном DNS). ----
        var portProbes = new List<NetworkPortProbe>(ports.Count);
        if (dnsOk && ports.Count > 0)
        {
            var probes = await Task.WhenAll(ports.Select((port, index) =>
                    ProbePortAsync(parsed.Host, port, ServiceKeyAt(serviceKeys, index, port),
                        timeoutMs, cancellationToken)))
                .ConfigureAwait(false);
            portProbes.AddRange(probes);
        }
        else if (!dnsOk && ports.Count > 0)
        {
            // Без адреса TCP-проверка бессмысленна — порты помечаются как не проверенные
            // с причиной DNS-ошибки (для построения подсказок).
            for (var i = 0; i < ports.Count; i++)
                portProbes.Add(new NetworkPortProbe(ports[i], ServiceKeyAt(serviceKeys, i),
                    DiagnosticPortState.NotChecked, null, "dns_error"));
        }

        return new NetworkDiagnosticsResult
        {
            Host = parsed.Host,
            HostValid = true,
            DnsOk = dnsOk,
            DnsErrorCode = dnsErrorCode,
            ResolvedAddresses = resolvedAddresses,
            PingPerformed = pingResult.Performed,
            PingOk = pingResult.Ok,
            PingRttMs = pingResult.RttMs,
            PingErrorCode = pingResult.ErrorCode,
            Ports = portProbes
        };
    }

    /// <summary>
    /// Разбор адреса диагностики: "host", "host:port", "[IPv6]:port", голый IPv4/IPv6.
    /// Строгий контракт: пустая строка, нечисловой/внедиапазонный порт → null
    /// (невалидный адрес). «host:» (пустой порт) трактуется как host с портом по
    /// умолчанию; «host:abc» отклоняется. Internal — для юнит-тестов.
    /// </summary>
    internal static NetworkAddress? ParseAddress(string? value, int defaultPort = OneCPorts.Cluster)
    {
        var s = (value ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(s))
            return null;

        // [IPv6]:port — скобочная форма (как в ConnectionSettings.ParseServerAndPort).
        if (s.StartsWith('['))
        {
            var close = s.IndexOf(']');
            if (close <= 0)
                return null;

            var host = s.Substring(1, close - 1);
            if (string.IsNullOrWhiteSpace(host))
                return null;

            // Скобочная форма — только для IPv6: хост с «:», который не является
            // адресом IPv6 («[:1540]»), — невалиден.
            if (host.Contains(':') && !System.Net.IPAddress.TryParse(host, out _))
                return null;

            var port = defaultPort;
            if (close + 1 < s.Length)
            {
                if (s[close + 1] != ':')
                    return null;
                var portPart = s.Substring(close + 2).Trim();
                if (!TryParsePort(portPart, out port))
                    return null;
            }
            return new NetworkAddress(host, port);
        }

        // host:port — берём последнее «:», чтобы не ломать имена с двоеточием без порта.
        var colon = s.LastIndexOf(':');
        if (colon > 0 && colon < s.Length - 1)
        {
            var head = s.Substring(0, colon);
            var portPart = s.Substring(colon + 1).Trim();

            // Голый IPv6 без скобок («2001:db8::1») — в head остались «:» → это адрес, не порт.
            if (head.Contains(':'))
                return new NetworkAddress(s, defaultPort);

            if (!TryParsePort(portPart, out var port))
                return null;

            var host = head.Trim();
            if (string.IsNullOrEmpty(host))
                return null;
            return new NetworkAddress(host, port);
        }

        // «host:» — пустой порт → хост с портом по умолчанию.
        if (colon > 0 && colon == s.Length - 1)
            return new NetworkAddress(s.Substring(0, colon).Trim(), defaultPort);

        return new NetworkAddress(s, defaultPort);
    }

    private static bool TryParsePort(string portPart, out int port)
        => int.TryParse(portPart, out port) && port >= 1 && port <= 65535;

    private static async Task<IReadOnlyList<string>> ResolveHostAsync(
        string host, CancellationToken cancellationToken)
    {
        var addresses = await Dns.GetHostAddressesAsync(host, cancellationToken)
            .ConfigureAwait(false);
        return addresses.Select(a => a.ToString()).ToList();
    }

    private static async Task<PingProbeResult> PingAsync(
        string host, int timeoutMs, CancellationToken cancellationToken)
    {
        try
        {
            using var ping = new Ping();
            var sw = Stopwatch.StartNew();
            var reply = await ping.SendPingAsync(host, timeoutMs).ConfigureAwait(false);
            sw.Stop();

            if (reply.Status == IPStatus.Success)
                return new PingProbeResult(true, true, sw.ElapsedMilliseconds, null);
            if (reply.Status == IPStatus.TimedOut)
                return new PingProbeResult(true, false, null, "timeout");
            return new PingProbeResult(true, false, null, "no_reply");
        }
        catch (PlatformNotSupportedException)
        {
            return new PingProbeResult(false, false, null, "ping_permission");
        }
        catch (PingException ex) when (IsPermissionError(ex.InnerException))
        {
            return new PingProbeResult(false, false, null, "ping_permission");
        }
        catch (PingException)
        {
            return new PingProbeResult(false, false, null, "ping_error");
        }
        catch (SocketException se) when (IsPermissionError(se))
        {
            return new PingProbeResult(false, false, null, "ping_permission");
        }
        catch (SocketException)
        {
            return new PingProbeResult(false, false, null, "ping_error");
        }
        catch
        {
            return new PingProbeResult(false, false, null, "ping_error");
        }
    }

    /// <summary>
    /// TCP-проверка порта с таймаутом и измерением RTT. Отказ соединения (refused)
    /// отличается от таймаута: первое — порт закрыт, второе — файрвол/потеря сети.
    /// Internal — для быстрой проверки в IsBaseAvailable (этап 0.3.9.233) и тестов.
    /// </summary>
    internal static async Task<TcpProbeResult> TcpPortCheckAsync(
        string host, int port, int timeoutMs, CancellationToken cancellationToken = default)
    {
        using var client = new TcpClient();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeoutMs);

        var sw = Stopwatch.StartNew();
        try
        {
            await client.ConnectAsync(host, port, cts.Token).ConfigureAwait(false);
            sw.Stop();
            return new TcpProbeResult(true, sw.ElapsedMilliseconds, null);
        }
        catch (OperationCanceledException)
        {
            return new TcpProbeResult(false, null, "timeout");
        }
        catch (SocketException se)
        {
            return se.SocketErrorCode == SocketError.ConnectionRefused
                ? new TcpProbeResult(false, null, "refused")
                : new TcpProbeResult(false, null, "socket_error");
        }
        catch
        {
            return new TcpProbeResult(false, null, "socket_error");
        }
    }

    private async Task<NetworkPortProbe> ProbePortAsync(
        string host, int port, string? serviceKey, int timeoutMs, CancellationToken cancellationToken)
    {
        var result = await _tcpProbe(host, port, timeoutMs, cancellationToken)
            .ConfigureAwait(false);

        var state = result.Reachable
            ? DiagnosticPortState.Available
            : result.ErrorCode == "timeout"
                ? DiagnosticPortState.Timeout
                : DiagnosticPortState.Closed;

        // Явный ключ сервиса (набор из карты портов 1С) приоритетнее имени по значению порта.
        return new NetworkPortProbe(port,
            string.IsNullOrWhiteSpace(serviceKey) ? OneCPorts.GetServiceKey(port) : serviceKey!,
            state, result.RttMs, result.ErrorCode);
    }

    /// <summary>Возвращает явный ключ сервиса для индекса порта (issue #335) либо
    /// имя по значению порта через <see cref="OneCPorts.GetServiceKey"/>.</summary>
    private static string ServiceKeyAt(IReadOnlyList<string>? keys, int index, int port = 0)
    {
        if (keys is not null && index >= 0 && index < keys.Count && !string.IsNullOrWhiteSpace(keys[index]))
            return keys[index]!;
        return OneCPorts.GetServiceKey(port);
    }

    private static bool IsPermissionError(Exception? exception)
        => exception is SocketException se &&
           (se.SocketErrorCode == SocketError.AccessDenied ||
            (int)se.SocketErrorCode is 1 or 13);   // EPERM / EACCES (Unix raw-socket)
}