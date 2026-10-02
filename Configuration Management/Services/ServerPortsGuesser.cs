using Configuration_Management.Models;

namespace Configuration_Management.Services;

/// <summary>
/// Вычисляет порты сервисов 1С по «порту кластера» из поля диагностики (issue #335):
/// стандартная карта 1540 (агент) / 1541 (кластер) / 1542 (хранилище) / 1545 (RAS).
/// Если указан нестандартный порт кластера (например 2541 вместо 1541) — остальные
/// порты вычисляются тем же смещением (2540 / 2541 / 2542 / 2545).
/// Чистый helper без зависимостей — покрыт юнит-тестами.
/// </summary>
public static class ServerPortsGuesser
{
    /// <summary>Стандартная карта портов сервисов 1С.</summary>
    public static readonly ServerPortsSettings Default =
        new(OneCPorts.Cluster, OneCPorts.Agent, OneCPorts.Repository, OneCPorts.Ras);

    /// <summary>
    /// Возвращает карту портов для указанного «порта кластера».
    /// Входной порт, равный одному из стандартных (1540/1541/1542/1545) или не
    /// попадающий в допустимый диапазон, даёт стандартную карту без сдвига.
    /// </summary>
    public static ServerPortsSettings Guess(int clusterPort)
    {
        if (clusterPort is OneCPorts.Agent or OneCPorts.Cluster or OneCPorts.Repository or OneCPorts.Ras ||
            clusterPort is < 1 or > 65535)
        {
            return Default;
        }

        // Сдвиг относительно стандартного порта кластера: 2541 - 1541 = +1000.
        var offset = clusterPort - OneCPorts.Cluster;
        var agent = OneCPorts.Agent + offset;
        var repository = OneCPorts.Repository + offset;
        var ras = OneCPorts.Ras + offset;

        // Сдвиг применяется, только если все порты остаются в допустимом диапазоне.
        if (agent is >= 1 and <= 65535 &&
            repository is >= 1 and <= 65535 &&
            ras is >= 1 and <= 65535)
        {
            return new ServerPortsSettings(clusterPort, agent, repository, ras);
        }

        return Default;
    }
}