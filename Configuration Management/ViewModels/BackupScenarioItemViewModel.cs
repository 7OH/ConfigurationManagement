using Configuration_Management.Models;

namespace Configuration_Management.ViewModels;

/// <summary>
/// Строка списка сценариев резервирования: отображаемые поля и действия
/// (выполнить/изменить/удалить). Чистый .NET, используется обеими платформами.
/// </summary>
public class BackupScenarioItemViewModel : ViewModelBase
{
    private readonly Action<BackupScenarioItemViewModel> _runAction;
    private readonly Action<BackupScenarioItemViewModel> _editAction;
    private readonly Action<BackupScenarioItemViewModel> _deleteAction;

    public BackupScenarioItemViewModel(BackupScenario scenario,
        Action<BackupScenarioItemViewModel>? run = null,
        Action<BackupScenarioItemViewModel>? edit = null,
        Action<BackupScenarioItemViewModel>? delete = null)
    {
        Scenario = scenario;
        _runAction = run ?? (_ => { });
        _editAction = edit ?? (_ => { });
        _deleteAction = delete ?? (_ => { });
    }

    /// <summary>Исходная модель сценария.</summary>
    public BackupScenario Scenario { get; }

    public string Id => Scenario.Id;
    public string Name => Scenario.Name;
    public string FormatText => FormatName(Scenario.Format);

    /// <summary>Краткое описание каталогов назначения (через «; »).</summary>
    public string TargetSummary =>
        Scenario.TargetDirectories is { Count: > 0 }
            ? string.Join("; ", Scenario.TargetDirectories)
            : "";

    public void Run() => _runAction(this);
    public void Edit() => _editAction(this);
    public void Delete() => _deleteAction(this);

    /// <summary>Человекочитаемое имя формата.</summary>
    public static string FormatName(BackupFormat format) => format switch
    {
        BackupFormat.Cf => "CF",
        BackupFormat.Zip => "ZIP",
        BackupFormat.Rar => "RAR",
        _ => "DT"
    };
}