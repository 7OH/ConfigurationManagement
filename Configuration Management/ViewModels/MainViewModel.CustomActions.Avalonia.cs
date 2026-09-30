#if LINUX
namespace Configuration_Management.ViewModels;

/// <summary>
/// Платформенные хуки моста «Пользовательские действия» (0.3.9.195) для Linux/Avalonia:
/// подтверждение/предупреждения через <c>_dialog</c> и сохранение списка баз через
/// <c>SaveSilently()</c> после записи истории запусков.
/// </summary>
public partial class MainViewModel
{
    private partial bool ConfirmCustomAction(string message, string title) => _dialog.Confirm(message, title);

    partial void ShowCustomActionWarning(string message, string title) => _dialog.ShowWarning(message, title);

    partial void AfterCustomActionsHistorySaved() => SaveSilently();
}
#endif