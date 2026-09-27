using Configuration_Management.Services;
using Xunit;

namespace ConfigurationManagement.Tests;

/// <summary>
/// Тесты разбора аргументов командной строки (<see cref="CommandLineHandler.Parse"/>):
/// формы --run/--run=, модификатор --designer, команда --list и отсутствие команд.
/// Сам обработчик (доступ к DI, запуск процессов) в тестах не гоняется.
/// </summary>
public sealed class CommandLineHandlerTests
{
    [Fact]
    public void Parse_EmptyArgs_ReturnsNull()
    {
        Assert.Null(CommandLineHandler.Parse(null));
        Assert.Null(CommandLineHandler.Parse(Array.Empty<string>()));
    }

    [Fact]
    public void Parse_RunWithName_TakesNextArgument()
    {
        var result = CommandLineHandler.Parse(new[] { "--run", "Бухгалтерия" });

        Assert.NotNull(result);
        Assert.Equal(CommandLineAction.Run, result.Value.Action);
        Assert.Equal("Бухгалтерия", result.Value.Target);
        Assert.False(result.Value.Designer);
    }

    [Fact]
    public void Parse_RunEqualsForm_ReadsValue()
    {
        var result = CommandLineHandler.Parse(new[] { "--run=Зарплата" });

        Assert.NotNull(result);
        Assert.Equal(CommandLineAction.Run, result.Value.Action);
        Assert.Equal("Зарплата", result.Value.Target);
    }

    [Fact]
    public void Parse_RunWithDesignerFlag_SetsDesigner()
    {
        var result = CommandLineHandler.Parse(new[] { "--run", "База", "--designer" });

        Assert.NotNull(result);
        Assert.Equal(CommandLineAction.Run, result.Value.Action);
        Assert.Equal("База", result.Value.Target);
        Assert.True(result.Value.Designer);
    }

    [Fact]
    public void Parse_DesignerEqualsForm_IsNotModifier()
    {
        // Форма --designer=... — команда проводника (путь), а не модификатор;
        // она обрабатывается ExplorerCommandLine и до CLI не доходит.
        var result = CommandLineHandler.Parse(new[] { "--run", "База", "--designer=C:\\ib\\1CD" });

        Assert.NotNull(result);
        Assert.False(result.Value.Designer);
    }

    [Fact]
    public void Parse_ListOnly_ReturnsList()
    {
        var result = CommandLineHandler.Parse(new[] { "--list" });

        Assert.NotNull(result);
        Assert.Equal(CommandLineAction.List, result.Value.Action);
    }

    [Fact]
    public void Parse_ListWithRun_ListWins()
    {
        // Обе команды в одном запуске: --list без цели, --run ищется, но List
        // тоже валиден. Парсер фиксирует обе; обработчик отдаёт приоритет List.
        var result = CommandLineHandler.Parse(new[] { "--list", "--run", "База" });

        Assert.NotNull(result);
        Assert.Equal(CommandLineAction.List, result.Value.Action);
    }

    [Fact]
    public void Parse_RunWithoutTarget_ReturnsNull()
    {
        Assert.Null(CommandLineHandler.Parse(new[] { "--run" }));
        Assert.Null(CommandLineHandler.Parse(new[] { "--designer" }));
    }

    [Fact]
    public void Parse_UnrelatedArgs_ReturnsNull()
    {
        Assert.Null(CommandLineHandler.Parse(new[] { "--register", @"C:\ib\base.1CD" }));
        Assert.Null(CommandLineHandler.Parse(new[] { "/RestoreIB" }));
    }

    [Fact]
    public void Parse_QuotedValueViaEquals_StripsQuotes()
    {
        var result = CommandLineHandler.Parse(new[] { "--run=\"Моя база 1\"" });

        Assert.NotNull(result);
        Assert.Equal("Моя база 1", result.Value.Target);
    }

    [Fact]
    public void Parse_BaseAlias_WorksLikeRun()
    {
        var result = CommandLineHandler.Parse(new[] { "--base", "Отчётность" });

        Assert.NotNull(result);
        Assert.Equal(CommandLineAction.Run, result.Value.Action);
        Assert.Equal("Отчётность", result.Value.Target);
    }
}
