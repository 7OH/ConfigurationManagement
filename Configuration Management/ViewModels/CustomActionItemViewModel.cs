using System;
using Configuration_Management.Localization;
using Configuration_Management.Models;

namespace Configuration_Management.ViewModels;

/// <summary>
/// Строка списка пользовательских действий окна настройки: отображаемые поля
/// (имя, область, превью команды, горячая клавиша) и действия (изменить/удалить).
/// Чистый .NET, используется обеими платформами.
/// </summary>
public class CustomActionItemViewModel : ViewModelBase
{
    private readonly Action<CustomActionItemViewModel> _editAction;
    private readonly Action<CustomActionItemViewModel> _deleteAction;

    public CustomActionItemViewModel(CustomAction action,
        Action<CustomActionItemViewModel>? edit = null,
        Action<CustomActionItemViewModel>? delete = null)
    {
        Action = action;
        _editAction = edit ?? (_ => { });
        _deleteAction = delete ?? (_ => { });
    }

    /// <summary>Исходная модель действия.</summary>
    public CustomAction Action { get; }

    public string Id => Action.Id;
    public string Name => Action.Name;

    /// <summary>Локализованная подпись области применения («База» / «Группа» / «База и группа»).</summary>
    public string ScopeDisplay => Action.Scope switch
    {
        CustomActionScope.Base => LocalizationManager.T("CustomAction.Scope.Base"),
        CustomActionScope.Group => LocalizationManager.T("CustomAction.Scope.Group"),
        _ => LocalizationManager.T("CustomAction.Scope.Both")
    };

    /// <summary>Превью команды одной строкой (переносы строк заменяются пробелами, обрезка).</summary>
    public string CommandPreview
    {
        get
        {
            const int maxLength = 80;
            var command = (Action.Command ?? "")
                .Replace("\r\n", " ")
                .Replace('\r', ' ')
                .Replace('\n', ' ')
                .Trim();
            return command.Length <= maxLength
                ? command
                : command.Substring(0, maxLength - 1) + "…";
        }
    }

    /// <summary>Горячая клавиша действия (опционально).</summary>
    public string Hotkey => Action.Hotkey;

    public void Edit() => _editAction(this);
    public void Delete() => _deleteAction(this);
}