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

        // Колонка прогресса скрыта, пока нет активной проверки/загрузки (issue #267).
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        UpdateProgressColumnVisibility();

        // Закрытие окна по Esc (issue #264): единообразно с Avalonia-базой ModalWindowBase.
        PreviewKeyDown += OnWindow_PreviewKeyDown;

        // Копирование строки по Ctrl+C включает значение ссылки (issue #267): иначе при
        // штатном копировании DataGrid ссылка не попадает в буфер, хотя интуитивно хочется
        // скопировать значение ячейки.
        RowsGrid.PreviewKeyDown += OnRowsGrid_PreviewKeyDown;
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

    /// <summary>
    /// Копирует выделенную строку в буфер обмена, включая значение колонки «Ссылка»
    /// (issue #267). Колонка ссылки — шаблонная (TextBlock с обрезанием текста), поэтому
    /// штатное копирование DataGrid могло не включать её; здесь явно формируем строку
    /// с полным значением Url.
    /// </summary>
    private void OnRowsGrid_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.C || Keyboard.Modifiers != ModifierKeys.Control
            || RowsGrid.SelectedItem is not ActualReleaseRowViewModel row)
            return;

        var statusConv = new UpdateStatusToTextConverter();
        var status = statusConv.Convert(row.Status, typeof(string), null, CultureInfo.CurrentCulture) as string ?? string.Empty;
        var line = $"{row.Name}\t{row.LatestVersion}\t{status}\t{row.Url}";
        Clipboard.SetText(line);
        e.Handled = true;
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
                    AddRow(new ActualReleaseRowViewModel(config, null, OnCheckRow, OnDownloadRow));
                }
                else
                {
                    foreach (var edition in editions)
                        AddRow(new ActualReleaseRowViewModel(config, edition, OnCheckRow, OnDownloadRow));
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Error("Ошибка построения списка отслеживаемых конфигураций", ex);
        }
    }

    /// <summary>Добавляет строку и следит за её состоянием, чтобы скрывать/показывать
    /// колонку прогресса (issue #267), пока нет ни одной активной операции.</summary>
    private void AddRow(ActualReleaseRowViewModel row)
    {
        row.PropertyChanged += OnRowPropertyChanged;
        _viewModel.Rows.Add(row);
    }

    /// <summary>Реакция на изменение состояния пакетной проверки (видимость колонки прогресса).</summary>
    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ActualReleasesViewModel.IsCheckingAll))
            UpdateProgressColumnVisibility();
    }

    /// <summary>Реакция на начало/конец проверки или загрузки строки (видимость колонки прогресса).</summary>
    private void OnRowPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ActualReleaseRowViewModel.IsChecking)
            || e.PropertyName == nameof(ActualReleaseRowViewModel.IsDownloading))
            UpdateProgressColumnVisibility();
    }

    /// <summary>Показывает колонку прогресса, только когда есть активная операция (issue #267).</summary>
    private void UpdateProgressColumnVisibility()
    {
        if (ProgressColumn is null)
            return;
        var active = _viewModel.IsCheckingAll
            || _viewModel.Rows.Any(r => r.IsChecking || r.IsDownloading);
        ProgressColumn.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
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