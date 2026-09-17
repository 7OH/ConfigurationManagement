using Configuration_Management.Models;
using Configuration_Management.Services;
using Xunit;

namespace ConfigurationManagement.Tests;

/// <summary>
/// Тесты функциональности Этапа 10 дорожной карты StartManager:
/// режимы функциональности и параметры запуска по умолчанию из 1CLaunch.cfg.
/// </summary>
public sealed class Etap10FunctionalModeTests
{
    [Theory]
    [InlineData("User", FunctionalMode.User)]
    [InlineData("user", FunctionalMode.User)]
    [InlineData("Specialist", FunctionalMode.Specialist)]
    [InlineData("Developer", FunctionalMode.Developer)]
    [InlineData("", FunctionalMode.Specialist)]
    [InlineData("Неизвестный", FunctionalMode.Specialist)]
    public void FunctionalModes_Parse_ReturnsExpectedMode(string value, FunctionalMode expected)
    {
        Assert.Equal(expected, FunctionalModes.Parse(value));
    }

    [Theory]
    [InlineData(FunctionalMode.User, "User")]
    [InlineData(FunctionalMode.Specialist, "Specialist")]
    [InlineData(FunctionalMode.Developer, "Developer")]
    public void FunctionalModes_ToString_RoundTrips(FunctionalMode mode, string canonical)
    {
        Assert.Equal(canonical, FunctionalModes.ToString(mode));
        Assert.Equal(mode, FunctionalModes.Parse(FunctionalModes.ToString(mode)));
    }

    [Fact]
    public void FunctionalModes_IsUser_OnlyForUserMode()
    {
        Assert.True(FunctionalModes.IsUser("User"));
        Assert.True(FunctionalModes.IsUser("user"));
        Assert.False(FunctionalModes.IsUser("Specialist"));
        Assert.False(FunctionalModes.IsUser("Developer"));
        Assert.False(FunctionalModes.IsUser(null));
    }

    [Fact]
    public void AppSettings_FunctionalMode_DefaultsToSpecialist()
    {
        var settings = new AppSettings();
        Assert.Equal(FunctionalModes.Default, settings.FunctionalMode);
        Assert.False(FunctionalModes.IsUser(settings.FunctionalMode));
    }

    [Fact]
    public void AppSettings_NormalizeForLoad_RepairsNullsAndDefaultMode()
    {
        var settings = new AppSettings
        {
            FunctionalMode = "",
            LaunchConfigDefaults = null!,
            BackupTargetDirectories = null!
        };
        settings.NormalizeForLoad();

        Assert.Equal(FunctionalModes.Default, settings.FunctionalMode);
        Assert.NotNull(settings.LaunchConfigDefaults);
        Assert.NotNull(settings.BackupTargetDirectories);
    }

    [Fact]
    public void LaunchConfigDefaults_HasValues_ReportsPresence()
    {
        Assert.False(new LaunchConfigDefaults().HasValues);
        Assert.True(new LaunchConfigDefaults { ConfigPath = "usp" }.HasValues);
        Assert.True(new LaunchConfigDefaults { ConfigDir = "C:\\cfg" }.HasValues);
        Assert.True(new LaunchConfigDefaults { AppMode = "2" }.HasValues);
        Assert.True(new LaunchConfigDefaults { SelectModeOff = true }.HasValues);
    }

    [Fact]
    public void ToLaunchArguments_MapsConfiguredKeys()
    {
        var defaults = new LaunchConfigDefaults
        {
            ConfigPath = "usp",
            ConfigDir = "C:\\Конфигурации",
            AppMode = "2",
            SelectModeOff = true
        };

        var args = OneCLaunchConfigReader.ToLaunchArguments(defaults);

        Assert.Equal(4, args.Count);
        Assert.Equal("/ConfigurationPath", args[0].Key);
        Assert.Equal("usp", args[0].Value);
        Assert.Equal("/ConfigurationDir", args[1].Key);
        Assert.Equal("/AppMode", args[2].Key);
        Assert.Equal("/SelectModeOff", args[3].Key);
        Assert.False(args[3].HasValue); // флаг без значения
    }

    [Fact]
    public void ToLaunchArguments_SkipsEmptyAndDisabledFlags()
    {
        var defaults = new LaunchConfigDefaults(); // все пусто / SelectModeOff=false

        var args = OneCLaunchConfigReader.ToLaunchArguments(defaults);

        Assert.Empty(args);
    }

    [Fact]
    public void ToLaunchArguments_SelectModeOffFalse_ProducesNoFlag()
    {
        var defaults = new LaunchConfigDefaults { SelectModeOff = false, AppMode = "1" };
        var args = OneCLaunchConfigReader.ToLaunchArguments(defaults);

        var key = Assert.Single(args);
        Assert.Equal("/AppMode", key.Key);
        Assert.DoesNotContain(args, a => a.Key == "/SelectModeOff");
    }
}