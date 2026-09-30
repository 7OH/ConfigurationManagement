#if LINUX
namespace Configuration_Management.ViewModels;

/// <summary>
/// Платформенные хуки моста «Пользовательские действия» (0.3.9.195/0.3.9.196) для
/// Linux/Avalonia: подтверждение/предупреждения через <c>_dialog</c>, сохранение списка
/// баз через <c>SaveSilently()</c> после записи истории запусков и открытие окна
/// «Пользовательские действия» через <c>ShowSync</c>.
/// </summary>
public partial class MainViewModel
{
    private partial bool ConfirmCustomAction(string message, string title) => _dialog.Confirm(message, title);

    partial void ShowCustomActionWarning(string message, string title) => _dialog.ShowWarning(message, title);

    partial void AfterCustomActionsHistorySaved() => SaveSilently();

    /// <summary>
    /// Открывает окно «Пользовательские действия» (0.3.9.196) и перечитывает кэш действий
    /// после его закрытия — подменю актуализируется при следующем открытии контекстного меню.
    /// </summary>
    partial void ExecuteShowCustomActionsSettings()
    {
        var win = new Configuration_Management.CustomActionsWindow();
        win.ShowSync(OwnerWindow());
        ReloadCustomActions();
    }
}
#endif