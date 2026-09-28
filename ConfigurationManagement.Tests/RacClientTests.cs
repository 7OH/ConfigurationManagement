using Configuration_Management.Services;
using Xunit;

namespace ConfigurationManagement.Tests;

/// <summary>
/// Тесты сборки аргументов командной строки rac (<see cref="RacClient.BuildArguments"/>)
/// и маскирования пароля (<see cref="SensitiveDataMasker.MaskRacPassword"/>) —
/// без запуска реального процесса rac.
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
            "--host=localhost",
            "--port=1540",
            "--user=Admin",
            "--password=secret",
            "cluster",
            "list"
        }, args);
    }

    [Fact]
    public void BuildArguments_OmitsEmptyUserAndPassword()
    {
        var args = RacClient.BuildArguments(
            new RacConnectionParams { Address = "srv1", Port = 1545 }, "cluster", "list");

        Assert.Equal(new[] { "--host=srv1", "--port=1545", "cluster", "list" }, args);
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
    public void BuildArguments_ZeroPort_IsOmitted_AndDefaultIs1540()
    {
        var args = RacClient.BuildArguments(Params(port: 0), "cluster", "list");

        Assert.DoesNotContain(args, a => a.StartsWith("--port="));
        Assert.Equal(1540, IRacClient.DefaultPort);
    }

    [Fact]
    public void BuildArguments_KeepsValuesWithSpacesAsSingleToken()
    {
        // Пароль и адрес с пробелами передаются одним токеном (ArgumentList без shell).
        var args = RacClient.BuildArguments(
            new RacConnectionParams
            {
                Address = "srv 1",
                Port = 1540,
                User = "Иванов",
                Password = "пароль с пробелами"
            },
            "session", "list");

        Assert.Contains("--password=пароль с пробелами", args);
        Assert.Contains("--user=Иванов", args);
        Assert.Contains("--host=srv 1", args);
    }

    [Fact]
    public void MaskRacPassword_HidesPasswordValue()
    {
        const string line = "--host=localhost --port=1540 --user=Admin --password=secret cluster list";
        var masked = SensitiveDataMasker.MaskRacPassword(line);

        Assert.DoesNotContain("secret", masked);
        Assert.Contains("--password=***", masked);
    }

    [Fact]
    public void MaskRacPassword_LeavesOtherArguments_AndNull()
    {
        const string line = "--host=srv1 cluster list";

        Assert.Equal(line, SensitiveDataMasker.MaskRacPassword(line));
        Assert.Null(SensitiveDataMasker.MaskRacPassword(null!));
    }
}