#if WINDOWS
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using Configuration_Management.Localization;
using Configuration_Management.Models;
using Configuration_Management.Services;
using Configuration_Management.ViewModels;

namespace Configuration_Management;

/// <summary>
/// Окно «Актуальные релизы» (горячая клавиша Alt+F9): список отслеживаемых типовых конфигураций
/// 1С, пакетная проверка наличия новых релизов и загрузка дистрибутива. Сетевые операции
/// выполняются в фоновых потоках (Task.Run); результаты применяются к строкам в UI-потоке.
/// </summary>
public partial class ActualReleasesWindow : Window
{
    private readonly IOneCUpdatesService _updates = AppServices.GetRequiredService<IOneCUpdatesService>();
    private readonly IInfobaseRepository _repository = AppServices.GetRequiredService<IInfobaseRepository>();
    private readonly IAppLogger _logger = AppServices.GetRequiredService<IAppLogger>();
    private readonly IDialogService _dialogs = AppServices.GetRequiredService<IDialogService>();

    private readonly ActualReleasesViewModel _viewModel;

    /// <summary>
    /// Открывает окно «Актуальные релизы».
    /// </summary>
    public ActualReleasesWindow()
    {
        InitializeComponent();

        _viewModel = new ActualReleasesViewModel(OnCheckAll);
        DataContext = _viewModel;
        RowsGrid.ItemsSource = _viewModel.Rows;

        BuildRows();
        _viewModel.RefreshCommands();

        // Закрытие окна по Esc (issue #264): единообразно с Avalonia-базой ModalWindowBase.
        PreviewKeyDown += OnWindow_PreviewKeyDown;
    }

    /// <summary>Закрывает окно по Esc без модификаторов (issue #264).</summary>
    private void OnWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && Keyboard.Modifiers == ModifierKeys.None)
        {
            e.Handled = true;
            Close();
        }
    }

    /// <summary>Формирует строки из отслеживаемых конфигураций (предопределённых и пользовательских).</summary>
    private void BuildRows()
    {
        try
        {
            var settings = _repository.LoadSettings();
            var custom = settings.CustomConfigTypes ?? new List<OneCConfigType>();

            var all = BuiltInConfigTypes.All.Concat(custom).ToList();
            foreach (var config in all)
            {
                if (!config.IsTracked)
                    continue;

                var editions = config.Editions;
                if (editions.Count == 0)
                {
                    _viewModel.Rows.Add(new ActualReleaseRowViewModel(config, null, OnCheckRow, OnDownloadRow));
                }
                else
                {
                    foreach (var edition in editions)
                        _viewModel.Rows.Add(new ActualReleaseRowViewModel(config, edition, OnCheckRow, OnDownloadRow));
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Error("Ошибка построения списка отслеживаемых конфигураций", ex);
        }
    }

    /// <summary>Выполняет точечную проверку одной строки.</summary>
    private async void OnCheckRow(ActualReleaseRowViewModel row)
    {
        if (row.IsChecking || row.IsDownloading)
            return;

        row.IsChecking = true;
        row.Error = string.Empty;
        try
        {
            var url = _updates.BuildUpdateUrl(row.Config, row.Edition, null);
            var result = await Task.Run(() =>
                _updates.CheckForUpdatesAsync(row.Name, string.Empty, url));
            row.ApplyResult(result);
        }
        catch (Exception ex)
        {
            _logger.Error($"Ошибка проверки конфигурации «{row.Name}»", ex);
            row.Status = ConfigUpdateStatus.Failed;
            row.Error = LocalizationManager.T("Updates.NetworkError");
        }
        finally
        {
            row.IsChecking = false;
        }
    }

    /// <summary>Выполняет пакетную проверку всех отслеживаемых конфигураций последовательно.</summary>
    private async void OnCheckAll()
    {
        if (_viewModel.IsCheckingAll)
            return;

        _viewModel.IsCheckingAll = true;
        _viewModel.RefreshCommands();
        try
        {
            foreach (var row in _viewModel.Rows.ToList())
            {
                if (row.IsDownloading)
                    continue;

                row.IsChecking = true;
                row.Error = string.Empty;
                try
                {
                    var url = _updates.BuildUpdateUrl(row.Config, row.Edition, null);
                    var result = await Task.Run(() =>
                        _updates.CheckForUpdatesAsync(row.Name, string.Empty, url));
                    row.ApplyResult(result);
                }
                catch (Exception ex)
                {
                    _logger.Error($"Ошибка пакетной проверки конфигурации «{row.Name}»", ex);
                    row.Status = ConfigUpdateStatus.Failed;
                    row.Error = LocalizationManager.T("Updates.NetworkError");
                }
                finally
                {
                    row.IsChecking = false;
                }
            }
        }
        finally
        {
            _viewModel.IsCheckingAll = false;
            _viewModel.RefreshCommands();
        }
    }

    /// <summary>Загружает дистрибутив обновления по ссылке каталога релизов строки.</summary>
    private async void OnDownloadRow(ActualReleaseRowViewModel row)
    {
        if (!row.CanDownload || string.IsNullOrWhiteSpace(row.Url))
            return;

        var targetPath = _dialogs.SaveFileDialog(
            LocalizationManager.T("Updates.Download"),
            BuildDownloadFileName(row),
            "Архивы (*.zip;*.rar;*.7z)|*.zip;*.rar;*.7z|Все файлы (*.*)|*.*",
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));

        if (string.IsNullOrWhiteSpace(targetPath))
            return;

        row.IsDownloading = true;
        row.Progress = 0;
        try
        {
            var progress = new Progress<double>(v => row.Progress = Math.Clamp(v, 0, 1));
            var savedPath = await Task.Run(() =>
                _updates.DownloadUpdateAsync(row.Url, targetPath, progress));

            if (string.IsNullOrWhiteSpace(savedPath))
            {
                _dialogs.ShowWarning(LocalizationManager.T("Updates.NetworkError"),
                    LocalizationManager.T("Updates.ActualReleasesTitle"));
            }
            else
            {
                row.Progress = 1;
                _dialogs.ShowInfo(string.Format(LocalizationManager.T("Updates.Loaded"), savedPath),
                    LocalizationManager.T("Updates.ActualReleasesTitle"));
            }
        }
        catch (Exception ex)
        {
            _logger.Error($"Ошибка загрузки обновления «{row.Name}»", ex);
            _dialogs.ShowError(LocalizationManager.T("Updates.NetworkError"),
                LocalizationManager.T("Updates.ActualReleasesTitle"));
        }
        finally
        {
            row.IsDownloading = false;
        }
    }

    private static string BuildDownloadFileName(ActualReleaseRowViewModel row)
    {
        var baseName = SanitizeFileName(row.Name);
        var latest = string.IsNullOrWhiteSpace(row.LatestVersion) ? "update" : row.LatestVersion;
        return $"{baseName}_{latest}.zip";
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return string.Concat(name.Select(ch => invalid.Contains(ch) ? '_' : ch));
    }

    private void OnClose_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}

/// <summary>Преобразует статус проверки в локализованный текст для колонки «Статус».</summary>
public sealed class UpdateStatusToTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var status = value is ConfigUpdateStatus s ? s : ConfigUpdateStatus.Unknown;
        return status switch
        {
            ConfigUpdateStatus.UpToDate => LocalizationManager.T("Updates.UpToDate"),
            ConfigUpdateStatus.NewerAvailable => LocalizationManager.T("Updates.NewerAvailable"),
            ConfigUpdateStatus.Unavailable => LocalizationManager.T("Updates.Unavailable"),
            ConfigUpdateStatus.Failed => LocalizationManager.T("Updates.CheckFailed"),
            _ => LocalizationManager.T("Updates.Status"),
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;
}
#endif