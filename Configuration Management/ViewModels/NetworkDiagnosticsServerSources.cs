using System.Collections.Generic;
using System.Linq;
using Configuration_Management.Services;

namespace Configuration_Management.ViewModels;

/// <summary>
/// Источники известных серверов 1С для выпадающего списка «Серверы» окна
/// «Диагностика подключения» (issue #335): серверы клиент-серверных баз списка,
/// ключи хранилища сохранённых портов (мониторинг/диагностика) и адрес цели
/// проверки. Адрес цели включается всегда — даже когда внешние источники пусты,
/// поле «Серверы» остаётся заполненным текущим сервером.
/// </summary>
public static class NetworkDiagnosticsServerSources
{
    /// <summary>
    /// Объединяет источники: без дублей (регистронезависимо), по алфавиту;
    /// пустые/пробельные значения отбрасываются. Адрес цели присутствует
    /// всегда, даже если остальные источники пусты.
    /// </summary>
    public static IReadOnlyList<string> Merge(
        IEnumerable<string>? baseServers,
        IServerPortsStore? portsStore,
        string? targetHost)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddRange(set, baseServers);
        AddRange(set, SavedServerNames(portsStore));
        AddOne(set, targetHost);
        return set.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Серверы из хранилища сохранённых портов (их пользователь проверял ранее).</summary>
    private static IEnumerable<string> SavedServerNames(IServerPortsStore? portsStore)
    {
        if (portsStore is null)
            return Array.Empty<string>();

        try
        {
            return portsStore.Load().Keys.ToList();
        }
        catch
        {
            // Битое хранилище не должно ронять заполнение списка серверов.
            return Array.Empty<string>();
        }
    }

    private static void AddRange(HashSet<string> set, IEnumerable<string?>? servers)
    {
        if (servers is null)
            return;
        foreach (var server in servers)
            AddOne(set, server);
    }

    private static void AddOne(HashSet<string> set, string? server)
    {
        var s = server?.Trim() ?? string.Empty;
        if (s.Length > 0)
            set.Add(s);
    }
}