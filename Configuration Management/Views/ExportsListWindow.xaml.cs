#if WINDOWS
using System.Diagnostics;
using System.IO;
using System.Windows;
using Configuration_Management.Localization;
using Configuration_Management.Models;
using Configuration_Management.Services;
using Configuration_Management.ViewModels;

namespace Configuration_Management;

/// <summary>
/// Окно «Список выгрузок» (Windows/WPF): обзор созданных файлов резервных копий
/// и восстановление данных в выбранную ИБ (в т.ч. через /RestoreIB).
/// </summary>
public partial class ExportsListWindow : Window
{
    private readonly ExportsListViewModel _vm;
    private readonly Infobase? _infobase;
    private readonly MainViewModel? _mainVm;
    private readonly IDialogService _dialogs;

    /// <param name="infobase">ИБ, в которую выполняется восстановление (может быть null).</param>
    /// <param name="mainVm">MainViewModel для операции восстановления (может быть null).</param>
    public ExportsListWindow(Infobase? infobase = null, MainViewModel? mainVm = null)
    {
        InitializeComponent();
        _infobase = infobase;
        _mainVm = mainVm;
        _dialogs = AppServices.GetRequiredService<IDialogService>();
        _vm = new ExportsListViewModel(
            AppServices.GetRequiredService<IBackupScenarioStore>(),
            AppServices.GetRequiredService<IInfobaseRepository>(),
            AppServices.GetRequiredService<IAppLogger>());
        DataContext = _vm;
        ExportsList.ItemsSource = _vm.Items;

        Title = T("Restore.Title");
        RefreshButton.Content = T("Common.Refresh");
        RestoreButton.Content = T("Restore.Title");
        OpenFolderButton.Content = T("Restore.OpenFolder");
        ColFileName.Header = T("Restore.FileColumn");
        ColDirectory.Header = T("Restore.DirectoryColumn");
        ColSize.Header = T("Restore.SizeColumn");
        ColDate.Header = T("Restore.DateColumn");
        RestoreButton.IsEnabled = _infobase is not null && _mainVm is not null;

        _vm.Refresh();
    }

    private static string T(string key) => LocalizationManager.T(key);

    private BackupExportItem? Selected => ExportsList.SelectedItem as BackupExportItem;

    private void ExportsList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        // Активность «Восстановить» зависит и от выбранного файла, и от наличия ИБ.
        RestoreButton.IsEnabled = Selected is not null && _infobase is not null && _mainVm is not null;
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => _vm.Refresh();

    private async void Restore_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } item || _infobase is null || _mainVm is null)
            return;
        await _mainVm.RestoreAsync(_infobase, item);
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        var dir = Selected is { } sel ? sel.Directory : null;
        if (string.IsNullOrWhiteSpace(dir))
        {
            _dialogs.ShowInfo(T("Restore.SelectItem"), T("Restore.OpenFolder"));
            return;
        }
        try
        {
            Process.Start(new ProcessStartInfo { FileName = dir, UseShellExecute = true });
        }
        catch (System.Exception ex)
        {
            _dialogs.ShowError(ex.Message, T("Restore.OpenFolder"));
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
#endif