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
    /// Открывает выпадающее меню «Утилиты» верхней панели по клику на её кнопке (issue #262):
    /// глобальные команды, не привязанные к конкретной базе.
    /// </summary>
    private void OnUtilitiesMenuButton_Click(object sender, RoutedEventArgs e)
    {
        // Тот же паттерн, что и у остальных меню верхней панели (OnEnterpriseMenuClick,
        // OnConfiguratorMenuClick, OnClearCacheMenuClick): раскрываем меню под кнопкой
        // (PlacementMode.Bottom) и передаём DataContext окна, чтобы Command/InputGestureText
        // пунктов резолвились, а меню позиционировалось и рендерилось как в Linux (issue #282).
        if (sender is not System.Windows.Controls.Button btn || btn.ContextMenu is null)
            return;
        btn.ContextMenu.PlacementTarget = btn;
        btn.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        btn.ContextMenu.DataContext = DataContext;
        btn.ContextMenu.IsOpen = true;
    }
}
#endif