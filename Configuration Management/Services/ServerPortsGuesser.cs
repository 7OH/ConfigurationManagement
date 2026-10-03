using System;
using Configuration_Management.Models;

namespace Configuration_Management.Services;

/// <summary>
/// Вычисляет порты сервисов 1С по порту из поля диагностики (issue #335):
/// стандартная карта 1540 (агент) / 1541 (кластер) / 1542 (хранилище) / 1545 (RAS).
/// Введённый нестандартный порт интерпретируется как порт ближайшего стандартного
/// сервиса, и вся карта сдвигается одним смещением: 2541 → 2540/2541/2542/2545,
/// 27545 (похож на RAS) → 27540/27541/27542/27545 — введённый порт остаётся в карте.
/// Чистый helper без зависимостей — покрыт юнит-тестами.
/// </summary>
public static class ServerPortsGuesser
{
    /// <summary>Стандартная карта портов сервисов 1С.</summary>
    public static readonly ServerPortsSettings Default =
        new(OneCPorts.Cluster, OneCPorts.Agent, OneCPorts.Repository, OneCPorts.Ras);

    /// <summary>
    /// Возвращает карту портов для указанного порта. Входной порт, равный одному из
    /// стандартных (1540/1541/1542/1545) или не попадающий в допустимый диапазон, даёт
    /// стандартную карту без сдвига. Иначе сдвиг считается от стандартного порта с той же
    /// последней цифрой (2541 → кластер 1541 → 2540/2541/2542/2545; 27545 → RAS 1545 →
    /// 27540/27541/27542/27545), при отсутствии совпадения — от порта кластера (прежнее
    /// поведение). Сдвиг «от кластера всегда» давал лишние +4 для портов вида 27545
    /// (27544/27545/27546/27549) — issue #335.
    /// </summary>
    public static ServerPortsSettings Guess(int port)
    {
        if (port is OneCPorts.Agent or OneCPorts.Cluster or OneCPorts.Repository or OneCPorts.Ras ||
            port is < 1 or > 65535)
        {
            return Default;
        }

        // База сдвига: стандартный порт с той же последней цифрой, что и введённый
        // (карта сохраняет «узнаваемость» сервисов: 2541 похож на кластер, 27545 — на RAS).
        // Введённый порт остаётся в карте как значение СВОЕГО сервиса (например, 27545 — RAS).
        var basePort = MatchByLastDigit(port) ?? OneCPorts.Cluster;
        var offset = port - basePort;
        var cluster = OneCPorts.Cluster + offset;
        var agent = OneCPorts.Agent + offset;
        var repository = OneCPorts.Repository + offset;
        var ras = OneCPorts.Ras + offset;

        // Сдвиг применяется, только если все порты остаются в допустимом диапазоне.
        if (agent is >= 1 and <= 65535 &&
            repository is >= 1 and <= 65535 &&
            ras is >= 1 and <= 65535)
        {
            return new ServerPortsSettings(cluster, agent, repository, ras);
        }

        return Default;
    }

    /// <summary>Стандартный порт, последняя цифра которого совпадает с цифрой введённого
    /// (агент 1540 → 0, кластер 1541 → 1, хранилище 1542 → 2, RAS 1545 → 5); null — нет.</summary>
    private static int? MatchByLastDigit(int port)
    {
        var digit = port % 10;
        if (digit == OneCPorts.Agent % 10) return OneCPorts.Agent;
        if (digit == OneCPorts.Cluster % 10) return OneCPorts.Cluster;
        if (digit == OneCPorts.Repository % 10) return OneCPorts.Repository;
        if (digit == OneCPorts.Ras % 10) return OneCPorts.Ras;
        return null;
    }
}