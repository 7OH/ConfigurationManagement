using Configuration_Management.Models;
using Configuration_Management.ViewModels;
using Xunit;

namespace ConfigurationManagement.Tests;

/// <summary>
/// Тесты формы редактирования сценария запуска скрипта
/// (<see cref="ScriptScenarioEditViewModel"/>, issue #308): перенос полей через
/// <see cref="ScriptScenarioEditViewModel.ApplyTo"/> с сохранением Id (редактирование
/// не плодит копию), генерация Id у нового сценария и перенос свойства
/// <see cref="ScriptScenario.HideWindow"/>.
/// </summary>
public sealed class ScriptScenarioEditViewModelTests
{
    [Fact]
    public void ApplyTo_ExistingScenario_PreservesIdAndUpdatesFields()
    {
        var scenario = new ScriptScenario
        {
            Id = "existing-id",
            Name = "Старое имя",
            FilePath = "old.bat",
            Parameters = new List<string> { "%name%" },
            HideWindow = true
        };
        var vm = new ScriptScenarioEditViewModel(scenario);

        // Пользователь правит поля формы.
        vm.Name = "  Новое имя  ";
        vm.FilePath = "new.bat";
        vm.ParametersText = "/S:%connection.server%\n%connection.password%";
        vm.HideWindow = false;

        vm.ApplyTo(scenario);

        // Id НЕ меняется (issue #308: перезапись без дублей).
        Assert.Equal("existing-id", scenario.Id);
        Assert.Equal("Новое имя", scenario.Name);
        Assert.Equal("new.bat", scenario.FilePath);
        Assert.Equal(new[] { "/S:%connection.server%", "%connection.password%" }, scenario.Parameters);
        Assert.False(scenario.HideWindow);
    }

    [Fact]
    public void ApplyTo_TransfersWorkingDirectory()
    {
        // Issue #308, п.7: «Папка запуска» переносится из формы в сценарий.
        var scenario = new ScriptScenario { Id = "wd-id", Name = "Имя", FilePath = "tool.bat" };
        var vm = new ScriptScenarioEditViewModel(scenario);
        vm.WorkingDirectory = @"C:\reports\output";

        vm.ApplyTo(scenario);

        Assert.Equal(@"C:\reports\output", scenario.WorkingDirectory);
    }

    [Fact]
    public void Constructor_FromScenario_FillsWorkingDirectory()
    {
        var scenario = new ScriptScenario
        {
            Name = "С папкой",
            FilePath = "x.bat",
            WorkingDirectory = @"/opt/scripts"
        };

        var vm = new ScriptScenarioEditViewModel(scenario);

        Assert.Equal(@"/opt/scripts", vm.WorkingDirectory);
    }

    [Fact]
    public void Constructor_NullScenario_WorkingDirectoryDefaultsToEmpty()
    {
        var vm = new ScriptScenarioEditViewModel(null);

        Assert.Equal("", vm.WorkingDirectory);
    }

    [Fact]
    public void ApplyTo_NewScenario_AssignsId()
    {
        var created = new ScriptScenario();
        var vm = new ScriptScenarioEditViewModel(null);
        vm.Name = "Новый";
        vm.FilePath = "tool.bat";
        vm.ParametersText = "%name%";
        vm.HideWindow = false;

        vm.ApplyTo(created);

        Assert.False(string.IsNullOrWhiteSpace(created.Id));
        Assert.Equal("Новый", created.Name);
        Assert.Equal("tool.bat", created.FilePath);
        Assert.Equal(new[] { "%name%" }, created.Parameters);
        Assert.False(created.HideWindow);
    }

    [Fact]
    public void Constructor_FromScenario_FillsHideWindow()
    {
        var scenario = new ScriptScenario
        {
            Name = "Скрытый",
            FilePath = "x.bat",
            HideWindow = false
        };

        var vm = new ScriptScenarioEditViewModel(scenario);

        Assert.False(vm.HideWindow);
        Assert.Equal("Скрытый", vm.Name);
    }

    [Fact]
    public void Constructor_NullScenario_HideWindowDefaultsToTrue()
    {
        var vm = new ScriptScenarioEditViewModel(null);

        Assert.True(vm.HideWindow);
    }

    [Fact]
    public void ApplyTo_EmptyParametersText_ClearsParameters()
    {
        var scenario = new ScriptScenario
        {
            Name = "Имя",
            FilePath = "tool.bat",
            Parameters = new List<string> { "%name%", "old" }
        };
        var vm = new ScriptScenarioEditViewModel(scenario);
        vm.ParametersText = "";

        vm.ApplyTo(scenario);

        Assert.Empty(scenario.Parameters);
    }

    [Fact]
    public void Validate_RequiresNameAndFilePath()
    {
        var vm = new ScriptScenarioEditViewModel(null);

        Assert.Equal("Script.NameRequired", vm.Validate());

        vm.Name = "Имя";
        Assert.Equal("Script.FilePathRequired", vm.Validate());

        vm.FilePath = "tool.bat";
        Assert.Null(vm.Validate());
    }

    [Fact]
    public void AvailableTokens_ContainPasswordTokens()
    {
        // Локализация новых токенов пароля (issue #308).
        var tokens = ScriptScenarioEditViewModel.AvailableTokens
            .Select(t => t.Token)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.Contains("%connection.password%", tokens);
        Assert.Contains("%password%", tokens);
    }
}