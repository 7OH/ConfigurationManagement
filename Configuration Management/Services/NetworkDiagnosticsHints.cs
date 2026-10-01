using Configuration_Management.Models;

namespace Configuration_Management.Services;

/// <summary>
/// Локализуемая подсказка по результату диагностики: ключ словаря и аргументы
/// форматирования (порт, имя хоста). Текст формируется на этапе VM через
/// <c>LocalizationManager.T(Key, Args...)</c> — сервис остаётся чистым.
/// </summary>
public sealed class NetworkDiagnosticHint
{
    public NetworkDiagnosticHint(string key, params object[] args)
    {
        Key = key;
        Args = args;
    }

    /// <summary>Ключ локализации (Diagnostics.Hint*).</summary>
    public string Key { get; }

    /// <summary>Аргументы подстановки {0}, {1}, …</summary>
    public IReadOnlyList<object> Args { get; }
}

/// <summary>
/// Чистое построение выводов/подсказок по результату диагностики (функция 12,
/// этап 0.3.9.230): матрица правил «состояния → набор ключей». Порядок — от
/// критичного к справке: невалидный адрес / DNS-ошибка (терминальные), затем
/// проблемные порты, затем агрегатные выводы (агент работает — кластер нет,
/// всё недоступно, всё доступно) и примечания по ICMP.
/// </summary>
public static class NetworkDiagnosticsHints
{
    /// <summary>Порт кластера 1С по умолчанию (для агрегатных подсказок).</summary>
    public const int DefaultClusterPort = OneCPorts.Cluster;

    public static IReadOnlyList<NetworkDiagnosticHint> Build(NetworkDiagnosticsResult result)
    {
        if (result is null)
            return Array.Empty<NetworkDiagnosticHint>();

        var hints = new List<NetworkDiagnosticHint>();

        // Терминальные случаи — проверять дальше нечего.
        if (!result.HostValid)
        {
            hints.Add(new NetworkDiagnosticHint("Diagnostics.HintInvalidAddress"));
            return hints;
        }

        if (!result.DnsOk)
        {
            hints.Add(new NetworkDiagnosticHint("Diagnostics.HintDnsFailed", result.Host));
            return hints;
        }

        // Проблемные порты (в порядке списка): закрыт / таймаут.
        foreach (var probe in result.Ports)
        {
            if (probe.State == DiagnosticPortState.Closed)
                hints.Add(new NetworkDiagnosticHint("Diagnostics.HintPortClosed",
                    probe.Port, probe.ServiceKey));
            else if (probe.State == DiagnosticPortState.Timeout)
                hints.Add(new NetworkDiagnosticHint("Diagnostics.HintPortTimeout",
                    probe.Port, probe.ServiceKey));
        }

        var anyAvailable = result.Ports.Any(p => p.State == DiagnosticPortState.Available);
        var allAvailable = result.Ports.Count > 0 &&
                           result.Ports.All(p => p.State == DiagnosticPortState.Available);
        var allDown = result.Ports.Count > 0 &&
                      result.Ports.All(p => p.State is DiagnosticPortState.Closed or DiagnosticPortState.Timeout);

        // Агрегатные выводы по портам 1С.
        var agentUp = result.Ports.Any(p =>
            p.Port == OneCPorts.Agent && p.State == DiagnosticPortState.Available);
        var clusterProbe = result.Ports.FirstOrDefault(p => p.Port == OneCPorts.Cluster);
        var rasUp = result.Ports.Any(p =>
            p.Port == OneCPorts.Ras && p.State == DiagnosticPortState.Available);
        var otherUp = result.Ports.Any(p =>
            p.Port is not (OneCPorts.Agent or OneCPorts.Cluster or OneCPorts.Ras) &&
            p.State == DiagnosticPortState.Available);

        if (agentUp && (clusterProbe is null || clusterProbe.State != DiagnosticPortState.Available))
            hints.Add(new NetworkDiagnosticHint("Diagnostics.HintAgentOkClusterDown",
                DefaultClusterPort));

        if (rasUp && !agentUp && !otherUp &&
            result.Ports.All(p => p.Port != OneCPorts.Cluster ||
                                  p.State != DiagnosticPortState.Available))
            hints.Add(new NetworkDiagnosticHint("Diagnostics.HintRasOnly"));

        if (allDown)
            hints.Add(new NetworkDiagnosticHint("Diagnostics.HintAllUnreachable"));

        // Примечания по ICMP (после портовых — они менее критичны).
        if (!result.PingPerformed && result.PingErrorCode == "ping_permission")
            hints.Add(new NetworkDiagnosticHint("Diagnostics.HintPingPermission"));

        if (result.PingPerformed && !result.PingOk && anyAvailable)
            hints.Add(new NetworkDiagnosticHint("Diagnostics.HintPingBlockedButTcpOk"));

        if (allAvailable)
            hints.Add(new NetworkDiagnosticHint("Diagnostics.HintAllOk"));

        return hints;
    }
}