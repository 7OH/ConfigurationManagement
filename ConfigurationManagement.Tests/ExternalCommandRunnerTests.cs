using Configuration_Management.Services;
using Xunit;

namespace ConfigurationManagement.Tests;

/// <summary>
/// Тесты помощника выполнения пользовательских команд при запуске базы
/// (<see cref="ExternalCommandRunner"/>, функция №8, 0.3.9.98): построение
/// команды для системного shell и запуск тривиальных процессов.
/// Платформозависимые проверки (Windows — cmd /c, Linux — sh -c) гейтятся
/// по текущей ОС; реальные процессы в тестах — только тривиальные
/// (exit 0/1, true/false, короткий таймаут).
/// </summary>
public sealed class ExternalCommandRunnerTests
{
    // ------------------- Построение команды для shell -------------------

    [Fact]
    public void BuildShellCommand_OnWindows_UsesCmd()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var (fileName, arguments) = ExternalCommandRunner.BuildShellCommand("echo hello");

        Assert.Equal("cmd.exe", fileName);
        Assert.Equal("/c echo hello", arguments);
    }

    [Fact]
    public void BuildShellCommand_OnLinux_UsesSh()
    {
        if (OperatingSystem.IsWindows())
            return;

        var (fileName, arguments) = ExternalCommandRunner.BuildShellCommand("echo hello");

        Assert.Equal("/bin/sh", fileName);
        Assert.Equal("-c echo hello", arguments);
    }

    [Fact]
    public void BuildShellCommand_EmptyCommand_ReturnsShellOnly()
    {
        var (fileName, arguments) = ExternalCommandRunner.BuildShellCommand(string.Empty);
        var (fileName2, arguments2) = ExternalCommandRunner.BuildShellCommand("   ");

        // Shell выбран по платформе, тело команды пустое — без хвостового пробела.
        Assert.False(string.IsNullOrWhiteSpace(fileName));
        Assert.Equal(fileName, fileName2);
        Assert.Equal(arguments, arguments2);
        Assert.Equal(
            OperatingSystem.IsWindows() ? "/c" : "-c",
            arguments);
    }

    [Fact]
    public void BuildShellCommand_WithQuotes_PreservesCommandAsIs()
    {
        // Пользовательская команда передаётся в shell как есть, без экранирования:
        // кавычки сохраняются для самого shell (например, echo "hello world").
        var (fileName, arguments) = ExternalCommandRunner.BuildShellCommand("echo \"hello world\"");

        Assert.False(string.IsNullOrWhiteSpace(fileName));
        Assert.EndsWith("echo \"hello world\"", arguments);
        Assert.Contains("\"", arguments);
    }

    [Fact]
    public void BuildShellCommand_Null_ReturnsShellOnly()
    {
        var (fileName, arguments) = ExternalCommandRunner.BuildShellCommand(null);

        Assert.False(string.IsNullOrWhiteSpace(fileName));
        Assert.Equal(
            OperatingSystem.IsWindows() ? "/c" : "-c",
            arguments);
    }

    // ------------------- Исполнение тривиальных процессов -------------------

    [Fact]
    public async Task RunAsync_ZeroExit_ReturnsTrue()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var ok = await ExternalCommandRunner.RunAsync("exit 0", 5000);

        Assert.True(ok);
    }

    [Fact]
    public async Task RunAsync_NonZeroExit_ReturnsFalse()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var ok = await ExternalCommandRunner.RunAsync("exit 1", 5000);

        Assert.False(ok);
    }

    [Fact]
    public async Task RunAsync_LinuxTrue_ReturnsTrue()
    {
        if (OperatingSystem.IsWindows())
            return;

        var ok = await ExternalCommandRunner.RunAsync("true", 5000);

        Assert.True(ok);
    }

    [Fact]
    public async Task RunAsync_LinuxFalse_ReturnsFalse()
    {
        if (OperatingSystem.IsWindows())
            return;

        var ok = await ExternalCommandRunner.RunAsync("false", 5000);

        Assert.False(ok);
    }

    [Fact]
    public async Task RunAsync_Timeout_ReturnsFalse()
    {
        // Команда заведомо дольше таймаута: на Windows — ping (≈4 с),
        // на Linux — sleep 5. Таймаут 300 мс — команда считается неуспешной.
        var command = OperatingSystem.IsWindows()
            ? "ping -n 5 127.0.0.1 >nul"
            : "sleep 5";

        var ok = await ExternalCommandRunner.RunAsync(command, 300);

        Assert.False(ok);
    }

    [Fact]
    public async Task RunAsync_EmptyCommand_ReturnsTrue()
    {
        // Пустая команда — shell запускается и завершается с кодом 0.
        var ok = await ExternalCommandRunner.RunAsync(string.Empty, 5000);

        Assert.True(ok);
    }
}