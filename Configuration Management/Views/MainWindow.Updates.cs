#if WINDOWS
using System.Windows;
using Configuration_Management.Services;

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

    private void OnItsAccountsMenuClick(object sender, RoutedEventArgs e)
        => _viewModel.OpenItsAccounts();

    /// <summary>
    /// Ручная проверка обновлений приложения из подменю «Утилиты» верхней панели (issue #279).
    /// Та же проверка, что во вкладке «О программе»: сообщает явный результат (актуальная
    /// версия / ошибка / доступно обновление) через UpdateService.
    /// </summary>
    private async void OnCheckForUpdatesMenuClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var updateService = AppServices.GetRequiredService<UpdateService>();
            await updateService.CheckForUpdatesManualAsync();
        }
        catch
        {
            // Внутренние ошибки уже показаны в UpdateService; здесь только страхуемся.
        }
    }

    /// <summary>
    /// Открывает окно «Задания по расписанию» из подменю «Утилиты» (issue #286).
    /// Окно отображается отложенно, чтобы контекстное меню успело закрыться
    /// (та же проблема и решение, что в issue #288).
    /// </summary>
    private void OnScheduledTasksMenuClick(object sender, RoutedEventArgs e)
    {
        Dispatcher.BeginInvoke(
            new System.Action(() =>
            {
                var win = new ScheduledTasksWindow();
                win.Owner = this;
                win.ShowDialog();
            }),
            System.Windows.Threading.DispatcherPriority.Input);
    }

    /// <summary>
    /// Открывает выпадающее меню «Утилиты» верхней панели по клику на её кнопке (issue #262):
    /// глобальные команды, не привязанные к конкретной базе. Начиная с 0.3.12.29 (issue #374)
    /// меню строится ДИНАМИЧЕСКИ по каталогу UtilitiesMenuCatalog + компоновке
    /// UtilitiesMenuLayout (стандартная или пользовательская из utilities_menu_layout.json),
    /// а не статическим XAML, как раньше.
    /// </summary>
    private void OnUtilitiesMenuButton_Click(object sender, RoutedEventArgs e)
    {
        // Тот же паттерн, что и у остальных меню верхней панели (OnEnterpriseMenuClick,
        // OnConfiguratorMenuClick, OnClearCacheMenuClick): раскрываем меню под кнопкой
        // (PlacementMode.Bottom) и передаём DataContext окна, чтобы Command/InputGestureText
        // пунктов резолвились, а меню позиционировалось как в Linux (issue #282).
        if (sender is not System.Windows.Controls.Button btn)
            return;

        // Меню строится заново при каждом открытии: компоновка могла измениться
        // (правка структуры в редакторе, смена настройки UtilitiesMenuEditingEnabled).
        var menu = BuildUtilitiesMenu();
        menu.PlacementTarget = btn;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        menu.DataContext = DataContext;
        btn.ContextMenu = menu;

        // Открываем меню ОТЛОЖЕННО (как OnLaunchSplitButton_Click, см. MainWindow.Events.cs):
        // при синхронном открытии прямо в обработчике клика WPF не успевает измерить и
        // отрисовать пункты меню, из-за чего на Windows показывались только иконки без
        // текста заголовков (issue #282). Отложенное открытие на приоритете Input даёт
        // меню корректно раскрыться — заголовки, иконки и команды видны.
        Dispatcher.BeginInvoke(
            new Action(() =>
            {
                if (btn.ContextMenu is not null)
                    btn.ContextMenu.IsOpen = true;
            }),
            System.Windows.Threading.DispatcherPriority.Input);
    }

    /// <summary>
    /// ПКМ по заголовку «Утилиты» → «Настройка меню „Утилиты“» (issue #374): открывает
    /// окно настроек сразу на вкладке «Меню „Утилиты“» для быстрого доступа к правке.
    /// </summary>
    private void OnUtilitiesMenuEditSettings_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SettingsWindow(_viewModel) { Owner = this };
        dialog.SelectUtilitiesMenuTab();
        dialog.ShowDialog();
    }
}
#endif