#if WINDOWS
namespace Configuration_Management.ViewModels;

/// <summary>
/// Платформенные хуки моста «Пользовательские действия» (0.3.9.195) для Windows/WPF:
/// подтверждение/предупреждения через <c>_dialogs</c> (WpfDialogService) и сохранение
/// списка баз через <c>Save()</c> после записи истории запусков.
/// </summary>
public partial class MainViewModel
{
    private partial bool ConfirmCustomAction(string message, string title) => _dialogs.Confirm(message, title);

    partial void ShowCustomActionWarning(string message, string title) => _dialogs.ShowWarning(message, title);

    partial void AfterCustomActionsHistorySaved() => Save();
}
#endif