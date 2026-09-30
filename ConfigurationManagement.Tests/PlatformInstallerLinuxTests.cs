#if LINUX
using Configuration_Management.Models;
#endif
using Configuration_Management.Services;
using Xunit;

namespace ConfigurationManagement.Tests;

/// <summary>
/// Тесты установки дистрибутива платформы 1С на Linux (этап 0.3.9.212):
/// чистые команды sudo (<see cref="PlatformInstallerCommands"/>) живут в файле
/// без директив условной компиляции и проверяются на любом прогоне (в т.ч. на
/// Windows); человекочитаемая инструкция
/// (<see cref="PlatformInstaller.BuildInstallInstruction"/>) существует только
/// в Linux-сборке (#if LINUX) — соответствующие тесты обёрнуты в #if LINUX.
/// </summary>
public sealed class PlatformInstallerLinuxTests
{
    // --- DetectPackageType ---

    [Theory]
    [InlineData("8.3.27.2214_x64.deb", PackageType.Dpkg)]
    [InlineData("8.3.27.2214_x64.rpm", PackageType.Rpm)]
    [InlineData("8.3.27.2214_x64.tar.gz", PackageType.TarGz)]
    [InlineData("8.3.27.2214_x64.tgz", PackageType.TarGz)]
    [InlineData("readme.txt", PackageType.Other)]
    [InlineData("8.3.27.2214_x64.zip", PackageType.Other)]
    [InlineData("", PackageType.Other)]
    [InlineData("   ", PackageType.Other)]
    [InlineData(null, PackageType.Other)]
    public void DetectPackageType_ClassifiesByExtension(string? fileName, PackageType expected)
    {
        Assert.Equal(expected, PlatformInstallerCommands.DetectPackageType(fileName));
    }

    [Theory]
    [InlineData("8.3.27.2214_x64.DEB", PackageType.Dpkg)]
    [InlineData("8.3.27.2214_X64.RPM", PackageType.Rpm)]
    [InlineData("8.3.27.2214_x64.TAR.GZ", PackageType.TarGz)]
    [InlineData("8.3.27.2214_x64.TGZ", PackageType.TarGz)]
    public void DetectPackageType_IsCaseInsensitive(string fileName, PackageType expected)
    {
        Assert.Equal(expected, PlatformInstallerCommands.DetectPackageType(fileName));
    }

    // --- BuildSudoInstallCommand ---

    [Fact]
    public void BuildSudoInstallCommand_Deb_UsesDpkg()
    {
        Assert.Equal(
            "sudo dpkg -i '/tmp/8.3.27.2214_x64.deb'",
            PlatformInstallerCommands.BuildSudoInstallCommand("/tmp/8.3.27.2214_x64.deb"));
    }

    [Fact]
    public void BuildSudoInstallCommand_Rpm_UsesDnf()
    {
        Assert.Equal(
            "sudo dnf install -y '/tmp/8.3.27.2214_x64.rpm'",
            PlatformInstallerCommands.BuildSudoInstallCommand("/tmp/8.3.27.2214_x64.rpm"));
    }

    [Fact]
    public void BuildSudoInstallCommand_EscapesSpacesAndApostrophe()
    {
        // Пробелы остаются внутри одинарных кавычек; внутренний апостроф
        // экранируется как '\'' (закрыть кавычку, литеральный апостроф, открыть).
        var result = PlatformInstallerCommands.BuildSudoInstallCommand("/home/user/Дистрибутив 8.3/a'b.deb");

        Assert.Equal("sudo dpkg -i '/home/user/Дистрибутив 8.3/a'\\''b.deb'", result);
        Assert.Contains("a'\\''b", result);
    }

    // --- BuildSudoUninstallCommand ---

    [Fact]
    public void BuildSudoUninstallCommand_UsesDpkgWithVersion()
    {
        Assert.Equal(
            "sudo dpkg -r 1c-enterprise83-8.3.27.2214",
            PlatformInstallerCommands.BuildSudoUninstallCommand("8.3.27.2214"));
    }

    // --- BuildInstallInstruction (Linux-only) ---

#if LINUX
    [Theory]
    [InlineData("8.3.27.2214_x64.deb")]
    [InlineData("8.3.27.2214_x64.rpm")]
    public void BuildInstallInstruction_ContainsCommandAndRootTerminalMention(string fileName)
    {
        var instruction = PlatformInstaller.BuildInstallInstruction(
            new PlatformReleaseFile { FileName = fileName });

        Assert.Contains("терминал", instruction);
        Assert.Contains("root", instruction);
        Assert.Contains("sudo", instruction);
    }

    [Fact]
    public void BuildInstallInstruction_TarGz_ContainsSteps()
    {
        var instruction = PlatformInstaller.BuildInstallInstruction(
            new PlatformReleaseFile { FileName = "8.3.27.2214_x64.tar.gz" });

        Assert.Contains("./install", instruction);
        Assert.Contains("tar -xzf", instruction);
        Assert.Contains("root", instruction);
    }
#endif
}