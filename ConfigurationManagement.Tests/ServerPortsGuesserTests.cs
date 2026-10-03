using Configuration_Management.Models;
using Configuration_Management.Services;
using Xunit;

namespace ConfigurationManagement.Tests;

/// <summary>
/// Тесты «догадки» портов сервисов 1С (issue #335): нестандартный порт интерпретируется
/// как порт БЛИЖАЙШЕГО стандартного сервиса, вся карта сдвигается одним смещением
/// (2541 → 2540/2541/2542/2545; 27545 → 27540/27541/27542/27545 — сценарий пользователя);
/// стандартные и недопустимые значения дают стандартную карту без сдвига.
/// </summary>
public sealed class ServerPortsGuesserTests
{
    [Fact]
    public void Guess_CustomClusterPort2541_ShiftsAllServices()
    {
        var result = ServerPortsGuesser.Guess(2541);

        // По образцу из issue: 2541 → 2540 (агент), 2541 (кластер), 2542 (хранилище), 2545 (RAS).
        Assert.Equal(2541, result.Cluster);
        Assert.Equal(2540, result.Agent);
        Assert.Equal(2542, result.Repository);
        Assert.Equal(2545, result.Ras);
    }

    [Fact]
    public void Guess_RasLikePort27545_ShiftsAllServicesWithoutPlus4()
    {
        // Реальный сценарий пользователя (#335): введено 27545 (похоже на RAS-порт 1545),
        // раньше сдвиг считался от кластера (1541) и давал 27544/27545/27546/27549 (+4).
        var result = ServerPortsGuesser.Guess(27545);

        Assert.Equal(27541, result.Cluster);
        Assert.Equal(27540, result.Agent);
        Assert.Equal(27542, result.Repository);
        Assert.Equal(27545, result.Ras);
    }

    [Theory]
    [InlineData(27540)] // агент
    [InlineData(27542)] // хранилище
    public void Guess_ServiceLikePort_SameShiftedMap(int servicePort)
    {
        // Введённый порт любого сервиса даёт одну и ту же сдвинутую карту 27540/27541/27542/27545.
        var result = ServerPortsGuesser.Guess(servicePort);

        Assert.Equal(27541, result.Cluster);
        Assert.Equal(27540, result.Agent);
        Assert.Equal(27542, result.Repository);
        Assert.Equal(27545, result.Ras);
    }

    [Fact]
    public void Guess_StandardClusterPort_ReturnsDefaults()
    {
        var result = ServerPortsGuesser.Guess(OneCPorts.Cluster);

        Assert.Equal(OneCPorts.Cluster, result.Cluster);
        Assert.Equal(OneCPorts.Agent, result.Agent);
        Assert.Equal(OneCPorts.Repository, result.Repository);
        Assert.Equal(OneCPorts.Ras, result.Ras);
    }

    [Theory]
    [InlineData(OneCPorts.Agent)]
    [InlineData(OneCPorts.Repository)]
    [InlineData(OneCPorts.Ras)]
    public void Guess_StandardServicePort_ReturnsDefaults(int standardPort)
    {
        // Входной порт, совпадающий с одним из стандартных, не сдвигает карту —
        // иначе «сдвиг» ломал бы стандартные порты (например 1540 дал бы агент 1539).
        var result = ServerPortsGuesser.Guess(standardPort);

        Assert.Equal(OneCPorts.Cluster, result.Cluster);
        Assert.Equal(OneCPorts.Agent, result.Agent);
        Assert.Equal(OneCPorts.Repository, result.Repository);
        Assert.Equal(OneCPorts.Ras, result.Ras);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(70000)]
    [InlineData(65536)]
    public void Guess_InvalidPort_ReturnsDefaults(int invalidPort)
    {
        var result = ServerPortsGuesser.Guess(invalidPort);

        Assert.Equal(ServerPortsGuesser.Default, result);
    }

    [Fact]
    public void Guess_PortNearUpperBound_ShiftsWhenAllPortsInRange()
    {
        // 65535 оканчивается на 5 (как RAS 1545): offset = 63990, хранилище 1542+63990=65532,
        // RAS 1545+63990=65535 — все в диапазоне, карта сдвигается (введённый порт остаётся RAS).
        var result = ServerPortsGuesser.Guess(65535);

        Assert.Equal(65530, result.Agent);
        Assert.Equal(65531, result.Cluster);
        Assert.Equal(65532, result.Repository);
        Assert.Equal(65535, result.Ras);
    }

    [Fact]
    public void Guess_OffsetOutOfRange_FallsBackToDefaults()
    {
        // 65533 не совпадает последней цифрой ни с одним стандартным портом → фолбэк
        // на кластер (1541): offset=63992, RAS=1545+63992=65537 — за верхней границей,
        // возвращается стандартная карта без сдвига.
        var result = ServerPortsGuesser.Guess(65533);

        Assert.Equal(ServerPortsGuesser.Default, result);
    }

    [Theory]
    [InlineData(27540)] // цифра 0 → агент 1540
    [InlineData(27541)] // цифра 1 → кластер 1541
    [InlineData(27542)] // цифра 2 → хранилище 1542
    [InlineData(27545)] // цифра 5 → RAS 1545
    public void Guess_ServiceLikePort_SameShiftedMapByLastDigit(int servicePort)
    {
        // Введённый порт любого сервиса даёт одну и ту же сдвинутую карту 27540/27541/27542/27545.
        var result = ServerPortsGuesser.Guess(servicePort);

        Assert.Equal(27541, result.Cluster);
        Assert.Equal(27540, result.Agent);
        Assert.Equal(27542, result.Repository);
        Assert.Equal(27545, result.Ras);
    }
}