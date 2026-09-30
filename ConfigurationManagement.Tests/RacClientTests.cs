using Configuration_Management.Services;
using Xunit;

namespace ConfigurationManagement.Tests;

/// <summary>
/// Тесты сборки аргументов командной строки rac (<see cref="RacClient.BuildArguments"/>)
/// и маскирования пароля (<see cref="SensitiveDataMasker.MaskRacPassword"/>) —
/// без запуска реального процесса rac.
/// Точка подключения собирается ЕДИНЫМ токеном «host:port» первым аргументом
/// (rac.exe host:port cluster list): раздельные --host=/--port= не разбираются
/// новыми версиями платформы (issue #324).
/// </summary>
public sealed class RacClientTests
{
    private static RacConnectionParams Params(
        string address = "localhost", int port = 1540,
        string user = "Admin", string password = "secret")
    {
        return new RacConnectionParams
        {
            Address = address,
            Port = port,
            User = user,
            Password = password
        };
    }

    [Fact]
    public void BuildArguments_IncludesConnectionParams_ThenCommand()
    {
        var args = RacClient.BuildArguments(Params(), "cluster", "list");

        Assert.Equal(new[]
        {
            "localhost:1540",
            "--user=Admin",
            "--password=secret",
            "cluster",
            "list"
        }, args);
    }

    [Fact]
    public void BuildArguments_HostAndPort_AreSingleToken()
    {
        // Ключевой формат issue #324: rac.exe localhost:1545 cluster list.
        var args = RacClient.BuildArguments(
            new RacConnectionParams { Address = "srv1", Port = 1545 }, "cluster", "list");

        Assert.Equal("srv1:1545", args[0]);
        Assert.DoesNotContain(args, a => a.StartsWith("--host="));
        Assert.DoesNotContain(args, a => a.StartsWith("--port="));
        Assert.Equal(new[] { "srv1:1545", "cluster", "list" }, args);
    }

    [Fact]
    public void BuildArguments_OmitsEmptyUserAndPassword()
    {
        var args = RacClient.BuildArguments(
            new RacConnectionParams { Address = "srv1", Port = 1545 }, "cluster", "list");

        Assert.Equal(new[] { "srv1:1545", "cluster", "list" }, args);
    }

    [Fact]
    public void BuildArguments_EmptyAddress_OmitsConnectionToken()
    {
        var args = RacClient.BuildArguments(
            new RacConnectionParams { Address = "   ", Port = 1540, User = "Admin" },
            "cluster", "list");

        Assert.Equal(new[] { "--user=Admin", "cluster", "list" }, args);
    }

    [Fact]
    public void BuildArguments_ZeroPort_AddressIsPlainHost()
    {
        // Порт ≤ 0: rac.exe host cluster list — без «:port».
        var args = RacClient.BuildArguments(Params(port: 0), "cluster", "list");

        Assert.Equal("localhost", args[0]);
        Assert.DoesNotContain(args, a => a.StartsWith("--port="));
        Assert.DoesNotContain(args, a => a.StartsWith("--host="));
        Assert.Equal(1540, IRacClient.DefaultPort);
    }

    [Fact]
    public void BuildArguments_ClusterList_HasNoClusterOption()
    {
        var args = RacClient.BuildArguments(Params(), "cluster", "list");

        Assert.DoesNotContain(args, a => a.StartsWith("--cluster="));
    }

    [Fact]
    public void BuildArguments_ProcessList_IncludesClusterOption()
    {
        var clusterId = Guid.NewGuid();
        var args = RacClient.BuildArguments(Params(), "process", "list", $"--cluster={clusterId}");

        Assert.Contains($"--cluster={clusterId}", args);
        Assert.Equal("--cluster=" + clusterId, args[^1]);
    }

    [Fact]
    public void BuildArguments_KeepsValuesWithSpacesAsSingleToken()
    {
        // Адрес, пароль и логин с пробелами передаются одним токеном (ArgumentList без shell).
        var args = RacClient.BuildArguments(
            new RacConnectionParams
            {
                Address = "srv 1",
                Port = 1540,
                User = "Иванов",
                Password = "пароль с пробелами"
            },
            "session", "list");

        Assert.Contains("srv 1:1540", args);
        Assert.Contains("--user=Иванов", args);
        Assert.Contains("--password=пароль с пробелами", args);
        Assert.DoesNotContain(args, a => a.StartsWith("--host="));
    }

    [Fact]
    public void MaskRacPassword_HidesPasswordValue()
    {
        // Формат журнала после перехода на единый токен «host:port»: пароль по-прежнему
        // передаётся как --password=... и маскируется целиком.
        const string line = "localhost:1540 --user=Admin --password=secret cluster list";
        var masked = SensitiveDataMasker.MaskRacPassword(line);

        Assert.DoesNotContain("secret", masked);
        Assert.Contains("--password=***", masked);
        // Точка подключения host:port и остальные аргументы не затрагиваются.
        Assert.Contains("localhost:1540", masked);
        Assert.Contains("--user=Admin", masked);
    }

    [Fact]
    public void MaskRacPassword_LeavesOtherArguments_AndNull()
    {
        const string line = "srv1:1540 cluster list";

        Assert.Equal(line, SensitiveDataMasker.MaskRacPassword(line));
        Assert.Null(SensitiveDataMasker.MaskRacPassword(null!));
    }
}