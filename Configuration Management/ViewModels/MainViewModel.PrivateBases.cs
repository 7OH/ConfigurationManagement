using System.Windows.Input;
using Configuration_Management.Services;

namespace Configuration_Management.ViewModels;

/// <summary>
/// Команда разблокировки приватных баз (0.3.9.85) для Windows/WPF.
/// Частичный класс <see cref="MainViewModel"/>. Пункт «Открыть приватные базы»
/// живёт в подменю «Утилиты» верхней панели.
/// </summary>
public partial class MainViewModel
{
    private ICommand? _unlockPrivateBasesCommand;

    /// <summary>
    /// Команда «Открыть приватные базы»: разблокировка паролем активного профиля.
    /// Если у профиля пароля нет — окно предлагает его задать (после чего приватные
    /// базы сразу разблокируются). При отмене состояние не меняется.
    /// </summary>
    public ICommand UnlockPrivateBasesCommand =>
        _unlockPrivateBasesCommand ??= new RelayCommand(ExecuteUnlockPrivateBases);

    private void ExecuteUnlockPrivateBases()
    {
        var profileService = AppServices.GetRequiredService<IProfileService>();
        var setupMode = profileService.CurrentProfile?.HasPassword != true;

        var win = new Configuration_Management.PrivateBasesUnlockWindow(setupMode)
        {
            Owner = System.Windows.Application.Current.MainWindow
        };
        win.ShowDialog();

        // Успех: пароль задан/введён верно — окно само сохранило пароль и взвело флаг.
        // Отмена — приватные базы остаются скрытыми.
        if (!win.PasswordSet && !win.Unlocked)
            return;

        RefreshAfterPrivateUnlock();
    }

    /// <summary>
    /// Пересобирает все представления, в которых могли появиться приватные базы:
    /// дерево/список и панель тегов (теги приватных баз возвращаются). Меню трея
    /// Windows пересобирается при каждом открытии (MainWindow.Tray.cs), поэтому
    /// отдельного уведомления ему не нужно.
    /// </summary>
    private void RefreshAfterPrivateUnlock()
    {
        RebuildGroupTree();
        RefreshTagFilterItems();
    }
}