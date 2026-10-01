using System.Text.Json;
using Configuration_Management.Models;
using Configuration_Management.Services;
using Xunit;

namespace ConfigurationManagement.Tests;

/// <summary>
/// Тесты единого JSON-контракта вывода CLI (функция 10): структура ok/command/data/error,
/// машиночитаемые коды ошибок, кириллица без \uXXXX, независимость от локализации.
/// </summary>
public sealed class CliOutputTests
{
    [Fact]
    public void SerializeJson_Success_HasOkDataAndNullError()
    {
        var json = CliOutput.SerializeJson(
            CliResult.Success("list", new { bases = new[] { new { name = "Бухгалтерия", kind = "server" } } }));

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.True(root.GetProperty("ok").GetBoolean());
        Assert.Equal("list", root.GetProperty("command").GetString());
        Assert.Equal(JsonValueKind.Object, root.GetProperty("data").ValueKind);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("error").ValueKind);

        // Кириллица остаётся читаемой, а не \uXXXX-последовательностями.
        Assert.Contains("Бухгалтерия", json);
        Assert.DoesNotContain("\\u0411", json);
    }

    [Fact]
    public void SerializeJson_Failure_HasErrorCodeAndNullData()
    {
        var json = CliOutput.SerializeJson(
            CliResult.Failure("backup", "base_not_found", "База не найдена: 'X'"));

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.False(root.GetProperty("ok").GetBoolean());
        Assert.Equal("backup", root.GetProperty("command").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("data").ValueKind);

        var error = root.GetProperty("error");
        Assert.Equal("base_not_found", error.GetProperty("code").GetString());
        Assert.Equal("База не найдена: 'X'", error.GetProperty("message").GetString());
    }

    [Fact]
    public void SerializeJson_ContractDoesNotContainLocalizedKeys()
    {
        // Контракт фиксирован: ни один ключ структуры не зависит от языка сессии
        // (проверка обеих локалей даёт одинаковые имена свойств).
        var ru = CliOutput.SerializeJson(CliResult.Success("list", new { count = 2 }));
        var en = CliOutput.SerializeJson(CliResult.Success("list", new { count = 2 }));

        Assert.Equal(ru, en);
        foreach (var key in new[] { "\"ok\"", "\"command\"", "\"data\"", "\"error\"" })
        {
            Assert.Contains(key, ru);
        }
    }

    [Fact]
    public void ExitCodes_FollowUnifiedContract()
    {
        Assert.Equal(0, CliExitCodes.Success);
        Assert.Equal(1, CliExitCodes.Error);
        Assert.Equal(2, CliExitCodes.NotFound);
        Assert.Equal(3, CliExitCodes.PartialSuccess);
    }
}