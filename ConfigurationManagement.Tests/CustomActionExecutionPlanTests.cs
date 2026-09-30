using System.Threading;
using System.Threading.Tasks;
using Configuration_Management.Models;
using Configuration_Management.Services;
using Configuration_Management.ViewModels;
using Xunit;

namespace ConfigurationManagement.Tests;

/// <summary>
/// Тесты моста «Пользовательские действия» (0.3.9.195, функция 7) через чистый помощник
/// <see cref="CustomActionExecutionPlan"/> (конструктор <see cref="MainViewModel"/> слишком
/// нагружен для прямого теста — допускается планом п. 4.3): выбор целей области
/// (одиночная база / мультивыделение / группа), исключение приватных целей при
/// заблокированном профиле, решение о подтверждении и запись истории запуска
/// «Действие:<имя>» с маскированной командой.
/// </summary>
public sealed class CustomActionExecutionPlanTests
{
    private static Infobase Base(string name, string group = "", bool isPrivate = false) => new()
    {
        Id = "id-" + name,
        Name = name,
        Group = group,
        IsPrivate = isPrivate,
        Connection = new ConnectionSettings
        {
            Type = ConnectionType.File,
            FilePath = @"C:\bases\" + name,
            Password = isPrivate ? "secret" : ""
        }
    };

    private static CustomAction Action(string name = "Архив", string command = "echo {ИмяБазы} {Пароль}") => new()
    {
        Name = name,
        Command = command,
        Shell = ScriptShell.Cmd,
        EscapeValues = false,
        TimeoutMs = 1000
    };

    [Fact]
    public void SelectTargets_SingleBase_UsesSelectedInfobase()
    {
        var selected = Base("Выбранная");

        var targets = CustomActionExecutionPlan.SelectTargets(
            CustomActionContext.SingleBase, selected, new[] { Base("Первая"), selected, Base("Третья") },
            null, null, canShowPrivateBases: true);

        Assert.Single(targets);
        Assert.Equal("Выбранная", targets[0].Name);
    }

    [Fact]
    public void SelectTargets_SingleBase_NullSelection_ReturnsEmpty()
    {
        var targets = CustomActionExecutionPlan.SelectTargets(
            CustomActionContext.SingleBase, null, new[] { Base("Первая"), Base("Вторая") },
            null, null, canShowPrivateBases: true);

        Assert.Empty(targets);
    }

    [Fact]
    public void SelectTargets_Batch_UsesOnlyBatchSelectedIdsInListOrder()
    {
        var first = Base("Первая");
        var second = Base("Вторая");
        var third = Base("Третья");
        var list = new[] { first, second, third };

        var targets = CustomActionExecutionPlan.SelectTargets(
            CustomActionContext.Batch, null, list, new[] { third.Id, second.Id }, null, canShowPrivateBases: true);

        // Порядок — как в списке баз, а не как в наборе Id.
        Assert.Equal(new[] { "Вторая", "Третья" }, targets.Select(b => b.Name));
    }

    [Fact]
    public void SelectTargets_Group_UsesBasesOfCurrentGroup()
    {
        var list = new[]
        {
            Base("Бухгалтерия", group: "Основные"),
            Base("Кадры", group: "Основные"),
            Base("Архив", group: "Старые")
        };

        var targets = CustomActionExecutionPlan.SelectTargets(
            CustomActionContext.Group, null, list, null, "Основные", canShowPrivateBases: true);

        Assert.Equal(new[] { "Бухгалтерия", "Кадры" }, targets.Select(b => b.Name));
    }

    [Fact]
    public void SelectTargets_Group_EmptyGroupPath_ReturnsEmpty()
    {
        var list = new[] { Base("Бухгалтерия", group: "Основные") };

        var targets = CustomActionExecutionPlan.SelectTargets(
            CustomActionContext.Group, null, list, null, "", canShowPrivateBases: true);

        Assert.Empty(targets);
    }

    [Fact]
    public void SelectTargets_PrivateTarget_ExcludedWhenProfileLocked()
    {
        var privateBase = Base("Секретная", isPrivate: true);
        var visible = Base("Открытая");

        var targets = CustomActionExecutionPlan.SelectTargets(
            CustomActionContext.SingleBase, privateBase, new[] { visible, privateBase },
            null, null, canShowPrivateBases: false);

        Assert.Empty(targets);
    }

    [Fact]
    public void SelectTargets_PrivateTarget_KeptWhenProfileUnlocked()
    {
        var privateBase = Base("Секретная", isPrivate: true);

        var targets = CustomActionExecutionPlan.SelectTargets(
            CustomActionContext.SingleBase, privateBase, new[] { privateBase },
            null, null, canShowPrivateBases: true);

        Assert.Single(targets);
        Assert.Equal("Секретная", targets[0].Name);
    }

    [Theory]
    [InlineData(false, false, false)] // глобально выключено → без подтверждения
    [InlineData(false, true, false)]  // глобально выключено, даже с флагом «без подтверждения» — диалога нет
    [InlineData(true, true, false)]   // индивидуальный флаг «без подтверждения» → диалог пропускается
    [InlineData(true, false, true)]   // глобально включено и без флага → подтверждение запрашивается
    public void ShouldConfirm_RespectsGlobalAndIndividualFlags(bool global, bool runWithoutConfirm, bool expected)
    {
        var action = ConfirmAction(runWithoutConfirm);

        Assert.Equal(expected, CustomActionExecutionPlan.ShouldConfirm(action, global));
    }

    private static CustomAction ConfirmAction(bool runWithoutConfirm) => new()
    {
        Name = "Без подтверждения",
        Command = "echo x",
        RunWithoutConfirm = runWithoutConfirm
    };

    [Fact]
    public async Task BuildHistoryEntry_WritesActionModeWithMaskedCommand()
    {
        var ib = Base("Бухгалтерия");
        ib.Connection.Password = "pass";
        var action = Action("Архив");
        var runner = new CustomActionRunner((_, _, _) => Task.FromResult(true));

        var result = await runner.RunOneAsync(action, ib, CancellationToken.None);
        var entry = CustomActionExecutionPlan.BuildHistoryEntry(runner, action, result);

        Assert.Equal("Действие:Архив", entry.Mode);
        Assert.Same(ib, entry.Infobase);
        // Полная команда с обёрткой cmd.exe и МАСКИРОВАННЫМ паролем.
        Assert.StartsWith("cmd.exe /c ", entry.Details);
        Assert.Contains("Бухгалтерия", entry.Details);
        Assert.Contains("***", entry.Details);
        Assert.DoesNotContain("pass", entry.Details);
    }
}