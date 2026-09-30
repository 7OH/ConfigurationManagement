using System;
using System.Collections.Generic;
using Configuration_Management.Localization;
using Configuration_Management.Services;
using Xunit;

namespace ConfigurationManagement.Tests;

/// <summary>
/// Тесты проверки готовности к установке платформы 1С
/// (<see cref="PlatformInstallPreflight"/>, функция 9, этап 0.3.9.214):
/// занятые процессы 1С, права администратора, свободное место (размер дистрибутива
/// + 1 ГБ запаса) и подпись файла. Чистая функция — тестируется без платформы.
/// </summary>
public sealed class PlatformInstallPreflightTests
{
    private const long MiB = 1024 * 1024;
    private const long GiB = 1024L * MiB;

    [Fact]
    public void Check_NoProblems_ReturnsNoWarnings()
    {
        var warnings = PlatformInstallPreflight.Check(
            runningProcesses: Array.Empty<string>(),
            isAdmin: true,
            freeBytes: 10 * GiB,
            neededBytes: 500 * MiB,
            isSigned: true);

        Assert.Empty(warnings);
    }

    [Fact]
    public void Check_NullRunningProcesses_ReturnsNoWarnings()
    {
        var warnings = PlatformInstallPreflight.Check(
            runningProcesses: null,
            isAdmin: true,
            freeBytes: 10 * GiB,
            neededBytes: 500 * MiB,
            isSigned: true);

        Assert.Empty(warnings);
    }

    [Fact]
    public void Check_RunningProcesses_AddsWarningWithNames()
    {
        var warnings = PlatformInstallPreflight.Check(
            new List<string> { "1cv8c", "1cv8" },
            isAdmin: true,
            freeBytes: 10 * GiB,
            neededBytes: 500 * MiB,
            isSigned: true);

        var warning = Assert.Single(warnings);
        Assert.Equal(PlatformPreflightWarningKind.Warning, warning.Kind);
        // Текст собирается по ключу локализации с подстановкой имён процессов.
        Assert.Equal(
            string.Format(LocalizationManager.T("PlatformUpdate.Error.RunningProcesses"), "1cv8c, 1cv8"),
            warning.Text);
    }

    [Fact]
    public void Check_NotEnoughSpace_AddsWarning()
    {
        // Места ровно на размер дистрибутива — меньше размера + 1 ГБ запаса.
        var warnings = PlatformInstallPreflight.Check(
            Array.Empty<string>(),
            isAdmin: true,
            freeBytes: 500 * MiB,
            neededBytes: 500 * MiB,
            isSigned: true);

        var warning = Assert.Single(warnings);
        Assert.Equal(PlatformPreflightWarningKind.Warning, warning.Kind);
        Assert.Equal(LocalizationManager.T("PlatformUpdate.Error.NotEnoughSpace"), warning.Text);
    }

    [Fact]
    public void Check_ExactlyEnoughSpace_DoesNotWarn()
    {
        // Свободно ровно размер + запас: граница не считается проблемой (строгое меньше).
        var warnings = PlatformInstallPreflight.Check(
            Array.Empty<string>(),
            isAdmin: true,
            freeBytes: 500 * MiB + PlatformInstallPreflight.RequiredExtraBytes,
            neededBytes: 500 * MiB,
            isSigned: true);

        Assert.Empty(warnings);
    }

    [Fact]
    public void Check_UnknownFreeSpace_DoesNotWarn()
    {
        // Свободное место не удалось определить (null) — предупреждения нет,
        // операция не блокируется из-за недоступности проверки.
        var warnings = PlatformInstallPreflight.Check(
            Array.Empty<string>(),
            isAdmin: true,
            freeBytes: null,
            neededBytes: 500 * MiB,
            isSigned: true);

        Assert.Empty(warnings);
    }

    [Fact]
    public void Check_NotAdmin_AddsInfo()
    {
        var warnings = PlatformInstallPreflight.Check(
            Array.Empty<string>(),
            isAdmin: false,
            freeBytes: 10 * GiB,
            neededBytes: 500 * MiB,
            isSigned: true);

        var warning = Assert.Single(warnings);
        Assert.Equal(PlatformPreflightWarningKind.Info, warning.Kind);
        Assert.Equal(LocalizationManager.T("PlatformUpdate.Error.NotAdmin"), warning.Text);
    }

    [Fact]
    public void Check_UnsignedFile_AddsWarning()
    {
        var warnings = PlatformInstallPreflight.Check(
            Array.Empty<string>(),
            isAdmin: true,
            freeBytes: 10 * GiB,
            neededBytes: 500 * MiB,
            isSigned: false);

        var warning = Assert.Single(warnings);
        Assert.Equal(PlatformPreflightWarningKind.Warning, warning.Kind);
        Assert.Equal(LocalizationManager.T("PlatformUpdate.Error.Signature"), warning.Text);
    }

    [Fact]
    public void Check_AllProblems_AddsAllWarnings()
    {
        var warnings = PlatformInstallPreflight.Check(
            new List<string> { "1cv8c" },
            isAdmin: false,
            freeBytes: 100 * MiB,
            neededBytes: 500 * MiB,
            isSigned: false);

        Assert.Equal(4, warnings.Count);
        Assert.Contains(warnings, w => w.Kind == PlatformPreflightWarningKind.Warning &&
                                        w.Text == string.Format(
                                            LocalizationManager.T("PlatformUpdate.Error.RunningProcesses"), "1cv8c"));
        Assert.Contains(warnings, w => w.Kind == PlatformPreflightWarningKind.Info &&
                                        w.Text == LocalizationManager.T("PlatformUpdate.Error.NotAdmin"));
        Assert.Contains(warnings, w => w.Kind == PlatformPreflightWarningKind.Warning &&
                                        w.Text == LocalizationManager.T("PlatformUpdate.Error.NotEnoughSpace"));
        Assert.Contains(warnings, w => w.Kind == PlatformPreflightWarningKind.Warning &&
                                        w.Text == LocalizationManager.T("PlatformUpdate.Error.Signature"));
    }
}