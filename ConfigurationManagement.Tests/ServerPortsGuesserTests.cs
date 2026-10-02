using Configuration_Management.Models;
using Configuration_Management.Services;
using Xunit;

namespace ConfigurationManagement.Tests;

/// <summary>
/// Тесты «догадки» портов сервисов 1С по порту кластера (issue #335): нестандартный порт
/// (2541 вместо 1541) сдвигает всю карту (2540/2541/2542/2545); стандартные и недопустимые
/// значения дают стандартную карту без сдвига.
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
    public void Guess_PortNearUpperBound_FallsBackToDefaults()
    {
        // При 65535 сдвиг выводит хранилище (1542+63994=65536) и RAS (1545+63994=65539)
        // за допустимый диапазон — возвращается стандартная карта без сдвига.
        var result = ServerPortsGuesser.Guess(65535);

        Assert.Equal(ServerPortsGuesser.Default, result);
    }
}