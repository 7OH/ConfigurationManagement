#if WINDOWS
using System.Windows;

namespace Configuration_Management;

/// <summary>
/// Обработчики пунктов меню функции «Проверка обновлений конфигураций 1С»
/// (функции №21/№22): «Связать с конфигурацией» и «Список типовых конфигураций».
/// Частичный класс <see cref="MainWindow"/> (Windows/WPF).
/// </summary>
public partial class MainWindow
{
    private void OnConfigUpdateLinkMenuClick(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedInfobase is not null)
            _viewModel.OpenConfigUpdateLink(_viewModel.SelectedInfobase);
    }

    private void OnConfigTypesEditMenuClick(object sender, RoutedEventArgs e)
        => _viewModel.OpenConfigTypesEdit();

    /// <summary>
    /// Открывает выпадающее меню «Утилиты» верхней панели по клику на её кнопке (issue #262):
    /// глобальные команды, не привязанные к конкретной базе.
    /// </summary>
    private void OnUtilitiesMenuButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.ContextMenu is { } menu)
        {
            menu.PlacementTarget = fe;
            menu.IsOpen = true;
        }
    }
}
#endif