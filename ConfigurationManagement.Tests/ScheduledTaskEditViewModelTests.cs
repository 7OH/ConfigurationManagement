using System.Collections.Generic;
using Configuration_Management.ViewModels;
using Xunit;

namespace ConfigurationManagement.Tests;

/// <summary>
/// Тесты формы задания по расписанию (issue #372): поиск пункта списка по значению
/// для установки <c>SelectedItem</c> (закрытый ModernComboBox показывал ключ вместо названия).
/// </summary>
public sealed class ScheduledTaskEditViewModelTests
{
    private static IReadOnlyList<ScheduledTaskOption> Options() => new[]
    {
        new ScheduledTaskOption("Backup", "Резервная копия"),
        new ScheduledTaskOption("UpdateConfig", "Обновление конфигурации"),
        new ScheduledTaskOption("CheckIntegrity", "Проверка целостности"),
    };

    [Fact]
    public void FindOption_KnownValue_ReturnsOption()
    {
        var option = ScheduledTaskEditViewModel.FindOption(Options(), "UpdateConfig");

        Assert.NotNull(option);
        Assert.Equal("UpdateConfig", option!.Value);
        Assert.Equal("Обновление конфигурации", option.Text);
    }

    [Fact]
    public void FindOption_UnknownValue_ReturnsNull()
    {
        Assert.Null(ScheduledTaskEditViewModel.FindOption(Options(), "Unknown"));
    }

    [Fact]
    public void FindOption_NullOrEmptyValue_ReturnsNull()
    {
        Assert.Null(ScheduledTaskEditViewModel.FindOption(Options(), null));
        Assert.Null(ScheduledTaskEditViewModel.FindOption(Options(), string.Empty));
    }

    [Fact]
    public void FindOption_NullOptions_ReturnsNull()
    {
        Assert.Null(ScheduledTaskEditViewModel.FindOption(null, "Backup"));
    }

    [Fact]
    public void FindOption_IsCaseSensitiveByValue()
    {
        // Значения — стабильные ключи (Backup), регистр учитывается.
        Assert.Null(ScheduledTaskEditViewModel.FindOption(Options(), "backup"));
    }
}
