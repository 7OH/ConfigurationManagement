#if WINDOWS
namespace Configuration_Management.ViewModels;

/// <summary>
/// Платформенные хуки моста «Пользовательские действия» (0.3.9.195/0.3.9.196) для
/// Windows/WPF: подтверждение/предупреждения через <c>_dialogs</c> (WpfDialogService),
/// сохранение списка баз через <c>Save()</c> после записи истории запусков и открытие
/// окна «Пользовательские действия» через <c>ShowDialog</c>.
/// </summary>
public partial class MainViewModel
{
    private bool _confirmCustomActions = true;

    /// <summary>
    /// Подтверждать выполнение пользовательских действий перед запуском (0.3.9.197):
    /// глобальная настройка окна настроек; индивидуально переопределяется флагом
    /// действия «Выполнять без подтверждения». Отложенное сохранение — как у
    /// остальных настроек главной модели (ScheduleSaveSettings).
    /// </summary>
    public bool ConfirmCustomActions
    {
        get => _confirmCustomActions;
        set
        {
            if (SetProperty(ref _confirmCustomActions, value))
                ScheduleSaveSettings();
        }
    }

    private partial bool ConfirmCustomAction(string message, string title) => _dialogs.Confirm(message, title);

    partial void ShowCustomActionWarning(string message, string title) => _dialogs.ShowWarning(message, title);

    partial void AfterCustomActionsHistorySaved() => Save();

    /// <summary>
    /// Открывает окно «Пользовательские действия» (0.3.9.196) и перечитывает кэш действий
    /// после его закрытия — подменю актуализируется при следующем открытии контекстного меню.
    /// </summary>
    partial void ExecuteShowCustomActionsSettings()
    {
        var win = new Configuration_Management.CustomActionsWindow();
        // Настоящая модальность (issue #291): владелец — главное окно, иначе повторный
        // запуск программы с ярлыка ставил главное окно поверх диалога, блокируя ввод.
        win.Owner = System.Windows.Application.Current.MainWindow;
        win.ShowDialog();
        ReloadCustomActions();
    }
}
#endif