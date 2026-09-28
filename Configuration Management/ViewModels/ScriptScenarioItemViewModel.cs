using Configuration_Management.Models;

namespace Configuration_Management.ViewModels;

/// <summary>
/// Строка списка сценариев запуска скриптов: отображаемые поля и действия
/// (изменить/удалить). Чистый .NET, используется обеими платформами.
/// </summary>
public class ScriptScenarioItemViewModel : ViewModelBase
{
    private readonly Action<ScriptScenarioItemViewModel> _editAction;
    private readonly Action<ScriptScenarioItemViewModel> _deleteAction;

    public ScriptScenarioItemViewModel(ScriptScenario scenario,
        Action<ScriptScenarioItemViewModel>? edit = null,
        Action<ScriptScenarioItemViewModel>? delete = null)
    {
        Scenario = scenario;
        _editAction = edit ?? (_ => { });
        _deleteAction = delete ?? (_ => { });
    }

    /// <summary>Исходная модель сценария.</summary>
    public ScriptScenario Scenario { get; }

    public string Id => Scenario.Id;
    public string Name => Scenario.Name;
    public string FilePath => Scenario.FilePath;

    /// <summary>Краткое описание параметров (через «; »), для подписи строки списка.</summary>
    public string ParametersSummary =>
        Scenario.Parameters is { Count: > 0 }
            ? string.Join("; ", Scenario.Parameters)
            : "";

    public void Edit() => _editAction(this);
    public void Delete() => _deleteAction(this);
}