using Configuration_Management.Models;
using Configuration_Management.Services;
using Xunit;

namespace ConfigurationManagement.Tests;

/// <summary>
/// Тесты подсветки «зависших» процессов (issue #310): классификация статуса
/// точки «база запущена» (<see cref="RunningDotStatusClassifier"/>) и эвристика
/// отклика процесса по /proc/<pid>/stat (<see cref="LinuxProcessStateInspector"/>).
/// </summary>
public sealed class RunningDotStatusTests
{
    // ===================== Классификация статуса точки =====================

    [Theory]
    [InlineData(false, false, 0, RunningDotStatus.NotRunning)]
    [InlineData(false, true, 1, RunningDotStatus.NotRunning)]  // не запущена — скрыта, даже если «не отвечает»
    public void Classify_NotRunning_Hidden(bool isRunning, bool isNotResponding, int streak, RunningDotStatus expected)
    {
        Assert.Equal(expected, RunningDotStatusClassifier.Classify(isRunning, isNotResponding, streak));
    }

    [Theory]
    [InlineData(true, false, 0, RunningDotStatus.Responding)]  // running + responding → зелёная
    [InlineData(true, false, 3, RunningDotStatus.Responding)]  // streak не важен, если процесс отвечает
    public void Classify_RunningAndResponding_Green(bool isRunning, bool isNotResponding, int streak, RunningDotStatus expected)
    {
        Assert.Equal(expected, RunningDotStatusClassifier.Classify(isRunning, isNotResponding, streak));
    }

    [Theory]
    [InlineData(true, true, 1, RunningDotStatus.Hung)]        // running + not responding (1-й опрос) → оранжевая
    [InlineData(true, true, 2, RunningDotStatus.Critical)]    // running + not responding (2+ опроса) → красная
    [InlineData(true, true, 5, RunningDotStatus.Critical)]
    public void Classify_RunningAndNotResponding_OrangeOrRed(bool isRunning, bool isNotResponding, int streak, RunningDotStatus expected)
    {
        Assert.Equal(expected, RunningDotStatusClassifier.Classify(isRunning, isNotResponding, streak));
    }

    [Fact]
    public void Classify_StreakZeroWithNotResponding_StillHung()
    {
        // Пограничный случай: флаг «не отвечает» стоит, но счётчик ещё 0 — оранжевая.
        Assert.Equal(RunningDotStatus.Hung, RunningDotStatusClassifier.Classify(true, true, 0));
    }

    // ===================== Эвристика Linux /proc/<pid>/stat =====================

    [Theory]
    [InlineData("12345 (1cv8) S 1 12345 12345 0 -1 4194560 1510 0 0 0 0 0 0 0 20 0 1 0 123 456 0 0", true)]
    [InlineData("12345 (1cv8) R 1 12345 12345 0 -1 4194560 1510 0 0 0 0 0 0 0 20 0 1 0 123 456 0 0", true)]
    [InlineData("12345 (1cv8) Ss 1 12345 12345 0 -1 4194560 1510 0 0 0 0 0 0 0 20 0 1 0 123 456 0 0", true)]
    [InlineData("12345 (1cv8) Sl 1 12345 12345 0 -1 4194560 1510 0 0 0 0 0 0 0 20 0 1 0 123 456 0 0", true)]
    [InlineData("12345 (1cv8) I 1 12345 12345 0 -1 4194560 1510 0 0 0 0 0 0 0 20 0 1 0 123 456 0 0", true)]
    public void StatIndicatesResponding_NormalStates_True(string stat, bool expected)
    {
        Assert.Equal(expected, LinuxProcessStateInspector.StatIndicatesResponding(stat));
    }

    [Theory]
    [InlineData("12345 (1cv8) D 1 12345 12345 0 -1 4194560 1510 0 0 0 0 0 0 0 20 0 1 0 123 456 0 0")]   // uninterruptible sleep → не отвечает
    [InlineData("12345 (1cv8) Z 1 12345 12345 0 -1 4194560 1510 0 0 0 0 0 0 0 20 0 1 0 123 456 0 0")]   // зомби → не отвечает
    public void StatIndicatesResponding_UninterruptibleOrZombie_False(string stat)
    {
        Assert.False(LinuxProcessStateInspector.StatIndicatesResponding(stat));
    }

    [Fact]
    public void StatIndicatesResponding_CommWithSpaces_ParsedAfterLastClosingParen()
    {
        // Имя в скобках может содержать пробелы: состояние ищем за последней ')'.
        const string stat = "9999 (some process name (1cv8)) D 1 9999 9999 0 -1 4194560 1510";
        Assert.False(LinuxProcessStateInspector.StatIndicatesResponding(stat));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("no closing paren here")]
    public void StatIndicatesResponding_Garbage_True(string? stat)
    {
        // Тихая деградация: нет данных — считаем процесс отвечающим.
        Assert.True(LinuxProcessStateInspector.StatIndicatesResponding(stat));
    }
}