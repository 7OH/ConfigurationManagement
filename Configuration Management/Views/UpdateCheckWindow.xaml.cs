#if WINDOWS
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using Configuration_Management.Localization;
using Configuration_Management.Models;
using Configuration_Management.Services;
using Configuration_Management.ViewModels;

namespace Configuration_Management;

/// <summary>
/// Окно проверки обновлений конфигурации для выбранной информационной базы (горячая клавиша F9).
/// Показывает имя базы, текущую версию конфигурации, последнюю версию из каталога релизов 1С,
/// адрес каталога и статус проверки; подсвечивает наличие нового релиза и позволяет скачать
/// дистрибутив с индикатором прогресса. Сетевые операции выполняются в фоновом потоке.
/// </summary>
public partial class UpdateCheckWindow : Window
{
    private readonly IOneCUpdatesService _updates = AppServices.GetRequiredService<IOneCUpdatesService>();
    private readonly IInfobaseRepository _repository = AppServices.GetRequiredService<IInfobaseRepository>();
    private readonly ICustomConfigTypesStore _store = AppServices.GetRequiredService<ICustomConfigTypesStore>();
    private readonly IAppLogger _logger = AppServices.GetRequiredService<IAppLogger>();
    private readonly IDialogService _dialogs = AppServices.GetRequiredService<IDialogService>();

    private readonly Infobase _infobase;
    private readonly UpdateCheckRowViewModel _row;
    private CancellationTokenSource? _cts;

    /// <param name="infobase">Информационная база, для которой выполняется проверка обновлений.</param>
    public UpdateCheckWindow(Infobase infobase)
    {
        _infobase = infobase ?? throw new ArgumentNullException(nameof(infobase));
        _row = new UpdateCheckRowViewModel(infobase, OnDownloadRow);

        InitializeComponent();
        DataContext = _row;

        BaseNameText.Text = _row.Name;
        CurrentVersionText.Text = string.IsNullOrWhiteSpace(_row.CurrentVersion)
            ? "—"
            : _row.CurrentVersion;

        _row.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(_row.Progress))
                UpdateProgressDisplay();
        };

        Loaded += async (_, _) => await RunCheckAsync();
    }

    /// <summary>Запускает сетевую проверку наличия обновлений для связанной конфигурации.</summary>
    private async Task RunCheckAsync()
    {
        if (_row.IsChecking)
            return;

        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        _row.IsChecking = true;
        CheckButton.IsEnabled = false;
        ErrorText.Text = string.Empty;

        try
        {
            var config = FindLinkedConfig();
            // Явное связывание (UpdateConfigCode) — приоритет. Если связи нет, но свойства
            // конфигурации базы определены (вкладка «Платформа»), строим каталог релизов из них:
            // типовая конфигурация подбирается автоматически по имени (issue #323).
            if (config is null && !string.IsNullOrWhiteSpace(_infobase.ConfigurationName))
                config = ConfigTypeMatcher.FindByInfobaseName(_store.LoadAll(), _infobase.ConfigurationName);

            var url = _updates.BuildUpdateUrl(config, config?.DefaultEdition, _infobase.UpdateUrlOverride,
                _infobase.UpdateUrlSegment);
            _row.Url = url;

            if (string.IsNullOrWhiteSpace(url))
            {
                // Понятное пояснение вместо сухого «не задан адрес»: база не связана с типовой
                // конфигурацией либо у конфигурации нет сегмента/ника каталога релизов (issue #323).
                _row.Status = ConfigUpdateStatus.Failed;
                _row.Error = EmptyUrlMessage();
            }
            else
            {
                var configName = config?.Name ?? _row.Name;
                var currentVersion = _row.CurrentVersion;
                var result = await Task.Run(
                    () => _updates.CheckForUpdatesAsync(configName, currentVersion, url, token), token);
                _row.ApplyResult(result);
                SaveUpdateCache(result);
            }
        }
        catch (OperationCanceledException)
        {
            _row.Status = ConfigUpdateStatus.Failed;
            _row.Error = LocalizationManager.T("Updates.Cancelled");
        }
        catch (Exception ex)
        {
            _logger.Error($"Ошибка проверки обновлений конфигурации базы «{_row.Name}»", ex);
            _row.Status = ConfigUpdateStatus.Failed;
            _row.Error = LocalizationManager.T("Updates.NetworkError");
        }
        finally
        {
            _row.IsChecking = false;
            CheckButton.IsEnabled = true;
            UpdateStatusDisplay();
        }
    }

    /// <summary>Находит типовую конфигурацию, связанную с базой (по коду связи).
    /// Общий список типовых = встроенные + пользовательские из файла custom_config_types.json
    /// (единый загрузчик <see cref="ICustomConfigTypesStore"/> — issue #321).</summary>
    private OneCConfigType? FindLinkedConfig()
    {
        var code = _infobase.UpdateConfigCode;
        if (string.IsNullOrWhiteSpace(code))
            return null;

        return _store.LoadAll().FirstOrDefault(c =>
            string.Equals(c.Code, code, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Обновляет блок статуса, ошибок и деталей (версия/URL/кнопка «Скачать») после проверки.</summary>
    private void UpdateStatusDisplay()
    {
        StatusText.Text = StatusTextLocalized(_row.Status);

        var brush = FindResource("TextPrimaryBrush") as Brush ?? Brushes.Gray;
        if (_row.HasNewer)
            brush = FindResource("AccentBrush") as Brush ?? Brushes.Green;
        else if (_row.Status == ConfigUpdateStatus.Failed)
            brush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0xFF, 0xEF, 0x44, 0x44));
        StatusText.Foreground = brush;

        // Раньше «Последняя версия» и «Ссылка на каталог релизов» не обновлялись после проверки,
        // поэтому пользователь не видел результат работы окна (issue #323).
        LatestVersionText.Text = string.IsNullOrWhiteSpace(_row.LatestVersion)
            ? "—"
            : _row.LatestVersion;
        UrlText.Text = string.IsNullOrWhiteSpace(_row.Url) ? "—" : _row.Url;
        DownloadButton.IsEnabled = _row.CanDownload;

        // Error может быть ключом локализации (Updates.*) либо свободным текстом («HTTP 500»):
        // ключ переводим, свободный текст LocalizationManager.T() вернёт как есть.
        ErrorText.Text = _row.Status == ConfigUpdateStatus.Failed ? LocalizeError(_row.Error) : string.Empty;
    }

    /// <summary>Локализует текст ошибки: ключи «Updates.*» переводит, остальное возвращает без изменений.</summary>
    private static string LocalizeError(string error)
    {
        if (string.IsNullOrWhiteSpace(error))
            return string.Empty;
        return LocalizationManager.T(error);
    }

    /// <summary>
    /// Пояснение при пустом адресе каталога релизов: различает «база не связана», «у конфигурации
    /// нет ника на releases.1c.ru» и «нет адреса» (issue #323).
    /// </summary>
    private string EmptyUrlMessage()
    {
        if (!string.IsNullOrWhiteSpace(_infobase.UpdateConfigCode))
        {
            var config = FindLinkedConfig();
            if (config is not null && string.IsNullOrWhiteSpace(config.Nick))
                return string.Format(LocalizationManager.T("Updates.NoNick"), config.Name);
            return LocalizationManager.T("Updates.NoUrl");
        }

        if (!string.IsNullOrWhiteSpace(_infobase.ConfigurationName))
        {
            var config = ConfigTypeMatcher.FindByInfobaseName(_store.LoadAll(), _infobase.ConfigurationName);
            if (config is not null && string.IsNullOrWhiteSpace(config.Nick))
                return string.Format(LocalizationManager.T("Updates.NoNick"), config.Name);
        }
        return LocalizationManager.T("Updates.NoLink");
    }

    /// <summary>
    /// Сохраняет результат успешной проверки в <see cref="AppSettings.UpdateCheckCache"/>
    /// (по коду связи базы), чтобы колонка «Обновление» в Центре обслуживания показывала
    /// последний результат. Ошибки/отмена в кэш не пишутся.
    /// </summary>
    private void SaveUpdateCache(ConfigUpdateCheckResult result)
    {
        if (!result.Succeeded || string.IsNullOrWhiteSpace(_infobase.UpdateConfigCode))
            return;
        try
        {
            var settings = _repository.LoadSettings();
            settings.UpdateCheckCache ??= new System.Collections.Generic.Dictionary<string, Models.ConfigUpdateCheckResult>();
            settings.UpdateCheckCache[_infobase.UpdateConfigCode] = result;
            _repository.SaveSettings(settings);
        }
        catch (Exception ex)
        {
            _logger.Warn("Не удалось сохранить результат проверки обновлений в кэш: " + ex.Message);
        }
    }

    private void UpdateProgressDisplay()
    {
        if (_row.IsDownloading)
        {
            ProgressPanel.Visibility = Visibility.Visible;
            ProgressText.Text = $"{Math.Round(_row.Progress * 100, 0):0} %";
        }
        else
        {
            ProgressPanel.Visibility = Visibility.Collapsed;
        }
    }

    private async void OnCheckClick(object sender, RoutedEventArgs e)
    {
        await RunCheckAsync();
    }

    /// <summary>Загружает дистрибутив обновления по ссылке каталога релизов (кнопка «Скачать»).</summary>
    private async void OnDownloadRow(UpdateCheckRowViewModel row)
    {
        if (!row.CanDownload || string.IsNullOrWhiteSpace(row.Url))
            return;

        var fileName = BuildDownloadFileName(row);
        var targetPath = _dialogs.SaveFileDialog(
            LocalizationManager.T("Updates.Download"),
            fileName,
            "Архивы (*.zip;*.rar;*.7z)|*.zip;*.rar;*.7z|Все файлы (*.*)|*.*",
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));

        if (string.IsNullOrWhiteSpace(targetPath))
            return;

        row.IsDownloading = true;
        row.Progress = 0;
        UpdateProgressDisplay();
        _cts?.Cancel();

        try
        {
            var progress = new Progress<double>(v => row.Progress = Math.Clamp(v, 0, 1));
            var savedPath = await Task.Run(() =>
                _updates.DownloadUpdateAsync(row.Url, targetPath, progress, CancellationToken.None));

            if (string.IsNullOrWhiteSpace(savedPath))
            {
                _dialogs.ShowWarning(LocalizationManager.T("Updates.NetworkError"),
                    LocalizationManager.T("Updates.CheckTitle"));
            }
            else
            {
                row.Progress = 1;
                UpdateProgressDisplay();
                _dialogs.ShowInfo(string.Format(LocalizationManager.T("Updates.Loaded"), savedPath),
                    LocalizationManager.T("Updates.CheckTitle"));
            }
        }
        catch (Exception ex)
        {
            _logger.Error($"Ошибка загрузки обновления конфигурации «{row.Name}»", ex);
            _dialogs.ShowError(LocalizationManager.T("Updates.NetworkError"),
                LocalizationManager.T("Updates.CheckTitle"));
        }
        finally
        {
            row.IsDownloading = false;
            UpdateProgressDisplay();
        }
    }

    private static string BuildDownloadFileName(UpdateCheckRowViewModel row)
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

    private static string StatusTextLocalized(ConfigUpdateStatus status)
    {
        return status switch
        {
            ConfigUpdateStatus.UpToDate => LocalizationManager.T("Updates.UpToDate"),
            ConfigUpdateStatus.NewerAvailable => LocalizationManager.T("Updates.NewerAvailable"),
            ConfigUpdateStatus.Unavailable => LocalizationManager.T("Updates.Unavailable"),
            ConfigUpdateStatus.Failed => LocalizationManager.T("Updates.CheckFailed"),
            _ => LocalizationManager.T("Updates.Status"),
        };
    }

    private void OnClose_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
#endif