using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Configuration_Management.Models;
using Configuration_Management.Services;
using Xunit;

namespace ConfigurationManagement.Tests;

/// <summary>
/// Тесты оркестратора выполнения пользовательских действий (0.3.9.195, функция 7):
/// выполнение одной базы через fake-исполнитель (успех/ошибка/таймаут/исключение/
/// пустая команда), параллельный пакет (число вызовов, подстановка имени, независимость
/// ошибок, порядок результатов) и маскирование пароля в командной строке.
/// </summary>
public sealed class CustomActionRunnerTests
{
    private static Infobase Base(string name, string password = "") => new()
    {
        Id = Guid.NewGuid().ToString("N"),
        Name = name,
        Connection = new ConnectionSettings
        {
            Type = ConnectionType.ClientServer,
            Server = "srv",
            DatabaseName = name,
            Password = password
        }
    };

    private static CustomAction Action(string command = "echo {ИмяБазы}", int timeoutMs = 1000) => new()
    {
        Name = "Тестовое действие",
        Command = command,
        Shell = ScriptShell.Cmd,
        TimeoutMs = timeoutMs,
        EscapeValues = false
    };

    [Fact]
    public async Task RunOneAsync_Success_ReturnsSuccessWithoutTimeout()
    {
        var runner = new CustomActionRunner((_, _, _) => Task.FromResult(true));

        var result = await runner.RunOneAsync(Action(), Base("Бухгалтерия"), CancellationToken.None);

        Assert.True(result.Success);
        Assert.False(result.TimedOut);
        Assert.Null(result.Error);
    }

    [Fact]
    public async Task RunOneAsync_ImmediateFailure_IsNotTimedOut()
    {
        // Мгновенный false (код ошибки) не должен считаться таймаутом: трекер времени
        // фиксирует, что вызов занял заметно меньше TimeoutMs.
        var runner = new CustomActionRunner((_, _, _) => Task.FromResult(false));

        var result = await runner.RunOneAsync(Action(timeoutMs: 5000), Base("Бухгалтерия"), CancellationToken.None);

        Assert.False(result.Success);
        Assert.False(result.TimedOut);
        Assert.Equal("Failed", result.Error);
    }

    [Fact]
    public async Task RunOneAsync_SlowFailure_IsTimedOut()
    {
        // fake спит дольше таймаута и возвращает false — трекер помечает TimedOut=true.
        var runner = new CustomActionRunner(async (_, timeout, ct) =>
        {
            await Task.Delay(TimeSpan.FromMilliseconds(timeout + 100), ct);
            return false;
        });

        var result = await runner.RunOneAsync(Action(timeoutMs: 100), Base("Бухгалтерия"), CancellationToken.None);

        Assert.False(result.Success);
        Assert.True(result.TimedOut);
        Assert.Equal("Timeout", result.Error);
    }

    [Fact]
    public async Task RunOneAsync_ExecutorThrows_ReturnsFailureWithMessage()
    {
        var runner = new CustomActionRunner((_, _, _) => throw new InvalidOperationException("boom"));

        var result = await runner.RunOneAsync(Action(), Base("Бухгалтерия"), CancellationToken.None);

        Assert.False(result.Success);
        Assert.False(result.TimedOut);
        Assert.Contains("boom", result.Error);
    }

    [Fact]
    public async Task RunOneAsync_EmptyCommand_DoesNotInvokeExecutor()
    {
        var invoked = 0;
        var runner = new CustomActionRunner((_, _, _) =>
        {
            Interlocked.Increment(ref invoked);
            return Task.FromResult(true);
        });

        var result = await runner.RunOneAsync(Action(command: "   "), Base("Бухгалтерия"), CancellationToken.None);

        Assert.False(result.Success);
        Assert.False(result.TimedOut);
        Assert.Equal("EmptyCommand", result.Error);
        Assert.Equal(0, invoked);
    }

    [Fact]
    public async Task RunBatchAsync_CallsForEachBase_CommandsDifferBySubstitution()
    {
        var commands = new List<string>();
        var runner = new CustomActionRunner((command, _, _) =>
        {
            lock (commands)
                commands.Add(command);
            return Task.FromResult(true);
        });

        var targets = new[] { Base("Альфа"), Base("Бета"), Base("Гамма") };
        var results = await runner.RunBatchAsync(Action(), targets, CancellationToken.None);

        Assert.Equal(3, results.Count);
        Assert.All(results, r => Assert.True(r.Success));
        // Число вызовов = N; команды различаются подстановкой {ИмяБазы}.
        Assert.Equal(3, commands.Count);
        Assert.Equal(
            new HashSet<string> { "echo Альфа", "echo Бета", "echo Гамма" },
            commands.ToHashSet(StringComparer.Ordinal));
    }

    [Fact]
    public async Task RunBatchAsync_OneBaseFails_OthersSucceed()
    {
        var runner = new CustomActionRunner((command, _, _) => Task.FromResult(!command.Contains("Бета")));

        var targets = new[] { Base("Альфа"), Base("Бета"), Base("Гамма") };
        var results = await runner.RunBatchAsync(Action(), targets, CancellationToken.None);

        Assert.Equal(3, results.Count);
        Assert.True(results[0].Success);
        Assert.False(results[1].Success);
        Assert.True(results[2].Success);
        Assert.Equal("Failed", results[1].Error);
    }

    [Fact]
    public async Task RunBatchAsync_OrderMatchesTargets()
    {
        var runner = new CustomActionRunner((_, _, _) => Task.FromResult(true));

        var targets = new[] { Base("Первый"), Base("Второй"), Base("Третий") };
        var results = await runner.RunBatchAsync(Action(), targets, CancellationToken.None);

        Assert.Equal(targets.Select(t => t.Name), results.Select(r => r.Infobase.Name));
        Assert.Equal(targets.Select(t => t.Id), results.Select(r => r.Infobase.Id));
    }

    [Fact]
    public async Task RunBatchAsync_EmptyTargets_ReturnsEmptyWithoutCalls()
    {
        var invoked = 0;
        var runner = new CustomActionRunner((_, _, _) =>
        {
            Interlocked.Increment(ref invoked);
            return Task.FromResult(true);
        });

        var results = await runner.RunBatchAsync(Action(), Array.Empty<Infobase>(), CancellationToken.None);

        Assert.Empty(results);
        Assert.Equal(0, invoked);
    }

    [Fact]
    public void MaskSecrets_ReplacesPasswordValueIncludingEscapedForms()
    {
        Assert.Equal("echo ***", CustomActionRunner.MaskSecrets("echo pass", "pass"));
        Assert.Equal("echo \"***\"", CustomActionRunner.MaskSecrets("echo \"pass\"", "pass"));
        Assert.Equal("echo '***'", CustomActionRunner.MaskSecrets("echo 'pass'", "pass"));
        // Значение встречается несколько раз — заменяются все вхождения.
        Assert.Equal("*** -> ***", CustomActionRunner.MaskSecrets("pass -> pass", "pass"));
    }

    [Fact]
    public void MaskSecrets_EmptyPassword_ReturnsStringUnchanged()
    {
        const string line = "echo \"секрет\"";
        Assert.Equal(line, CustomActionRunner.MaskSecrets(line, ""));
        Assert.Equal(line, CustomActionRunner.MaskSecrets(line, null));
        Assert.Equal("", CustomActionRunner.MaskSecrets("", "pass"));
    }
}