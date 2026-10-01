#if LINUX
using System.Linq;
using Avalonia.Controls;
using Configuration_Management.Services;
using Configuration_Management.ViewModels;

namespace Configuration_Management
{
    /// <summary>
    /// Учётные данные ИТС в окне настроек (Avalonia/Linux, issue #333): выбор записи
    /// справочника <c>its_accounts.json</c> (первый пункт — «Основная») и кнопка открытия
    /// окна справочника. Частичный класс <see cref="SettingsWindow"/>.
    /// </summary>
    public partial class SettingsWindow
    {
        private ComboBox? _itsAccountsCombo;

        /// <summary>Перестраивает ComboBox выбора учётной записи ИТС (первый пункт — «Основная»).</summary>
        private void ReloadItsAccountsCombo()
        {
            if (_itsAccountsCombo is null)
                return;

            var items = ItsAccountSelectionBuilder.Build(
                AppServices.GetRequiredService<IItsAccountsStore>());
            _itsAccountsCombo.ItemsSource = items;
            var index = ItsAccountSelectionBuilder.IndexOf(items, _viewModel.ItsAccountId);
            _itsAccountsCombo.SelectedIndex = index >= 0 ? index : 0;
        }

        /// <summary>Открывает окно справочника учётных записей ИТС; после закрытия обновляет список.</summary>
        private void OnItsAccountsManageClick()
        {
            var win = new ItsAccountsWindow();
            win.ShowSync(this);
            _viewModel.RefreshItsAccounts();
            ReloadItsAccountsCombo();
        }

        /// <summary>Применяет выбранную учётную запись к настройкам при сохранении окна.</summary>
        private void ApplyItsAccountSelection()
        {
            if (_itsAccountsCombo?.SelectedItem is ItsAccountSelectionItem selection)
                _viewModel.ItsAccountId = selection.Id ?? "";
        }
    }
}
#endif