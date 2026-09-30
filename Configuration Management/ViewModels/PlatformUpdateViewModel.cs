using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Configuration_Management.Localization;
using Configuration_Management.Models;
using Configuration_Management.Services;

namespace Configuration_Management.ViewModels;

/// <summary>
/// ViewModel окна «Обновление платформы 1С»: единый список установленных и доступных
/// версий технологической платформы (колонки Версия/Размер/Статус/Совместимые базы),
/// проверка каталога портала, загрузка дистрибутива и установка. Сетевые операции
/// (получение каталога, подгрузка файлов, выбор дистрибутива) выполняются через
/// <see cref="IPlatformUpdateService"/>, а загрузка файла, установка, диалоги выбора
/// файлов и чтение установленных версий — через инжектируемые делегаты: класс
/// остаётся чистым и покрывается тестами на fake-сервисах без сети и UI.
/// </summary>
public sealed class PlatformUpdateViewModel : ViewModelBase
{
    private readonly IPlatformUpdateService _service;
    private readonly IInfobaseRepository _infobaseRepository;
    private readonly Func<IReadOnlyList<string>> _loadInstalledVersions;
    private readonly Func<string, string, IProgress<double>?, CancellationToken, Task<string?>> _downloadDistribution;
    private readonly Func<string, string, string?, IProgress<string>?, CancellationToken,
        Task<(bool Success, string? ErrorKey, int ExitCode)>> _installFromZip;
    private readonly Func<string?, string?> _saveFileDialog;
    private readonly Func<string?, string?> _openFileDialog;

    // Проверка готовности к установке (этап 0.3.9.214) — всё через делегаты,
    // чтобы класс оставался чистым и тестируемым.
    private readonly Func<IReadOnlyList<string>> _loadRunningProcesses;
    private readonly Func<bool> _isAdministrator;
    private readonly Func<string, long?> _getFreeBytes;
    private readonly Func<string, bool> _hasValidSignature;
    private readonly Func<string, string, bool> _confirmDialog;
    private readonly Action<string, string, NotificationKind, NotificationEvent> _notify;
    private readonly Services.IAppLogger? _appLogger;

    private readonly StringBuilder _log = new();
    private IReadOnlyList<PlatformRelease> _availableReleases = new List<PlatformRelease>();
    private bool _isBusy;
    private double _progress;
    private string? _selectedInstallerPath;
    private PlatformUpdateRowViewModel? _selectedRow;
    private bool _installHadWarnings;

    /// <summary>Строки окна: установленные ∪ доступные версии (по убыванию).</summary>
    public ObservableCollection<PlatformUpdateRowViewModel> Rows { get; } = new();

    /// <summary>Выполняется ли сетевая или установочная операция (блокирует команды).</summary>
    public bool IsBusy
    {
        get => _isBusy;
        set
        {
            if (SetProperty(ref _isBusy, value))
                RefreshCommands();
        }
    }

    /// <summary>Общий прогресс операции (0..1).</summary>
    public double Progress
    {
        get => _progress;
        set => SetProperty(ref _progress, Math.Clamp(value, 0, 1));
    }

    /// <summary>Текст журнала операции (строки добавляются через <see cref="AppendLog"/>).</summary>
    public string LogText => _log.ToString();

    /// <summary>Выбранная строка списка (для команд «Скачать и установить»/«Только скачать»).</summary>
    public PlatformUpdateRowViewModel? SelectedRow
    {
        get => _selectedRow;
        set
        {
            if (SetProperty(ref _selectedRow, value))
                RefreshCommands();
        }
    }

    /// <summary>Путь к выбранному локальному файлу установщика (команда «Выбрать файл
    /// установщика…»); полный сценарий установки из локального файла — этап 0.3.9.214.</summary>
    public string? SelectedInstallerPath
    {
        get => _selectedInstallerPath;
        set => SetProperty(ref _selectedInstallerPath, value);
    }

    /// <summary>Команда «Проверить обновления».</summary>
    public RelayCommand CheckCommand { get; }

    /// <summary>Команда «Скачать и установить» (Windows: загрузка в %TEMP% + установщик).</summary>
    public RelayCommand DownloadAndInstallCommand { get; }

    /// <summary>Команда «Только скачать» (сохранение дистрибутива через диалог).</summary>
    public RelayCommand DownloadOnlyCommand { get; }

    /// <summary>Команда «Выбрать файл установщика…».</summary>
    public RelayCommand ChooseInstallerCommand { get; }

    /// <summary>Команда «Удалить старые версии…» — заглушка до этапа 0.3.9.215
    /// (кнопка скрыта/недоступна).</summary>
    public RelayCommand RemoveOldVersionsCommand { get; }

    /// <param name="service">Сервис каталога версий платформы (портал releases.1c.ru).</param>
    /// <param name="infobaseRepository">Репозиторий информационных баз (совместимость версий).</param>
    /// <param name="loadInstalledVersions">Читает установленные версии платформы
    /// (на Windows — <c>PlatformVersionService.FindInstalledVersionInfos</c>).</param>
    /// <param name="downloadDistribution">Загружает файл дистрибутива по прямой ссылке
    /// с прогрессом; возвращает путь сохранённого файла или null при ошибке/отмене.</param>
    /// <param name="installFromZip">Устанавливает дистрибутив из zip (Windows —
    /// <c>PlatformInstaller.InstallFromZipAsync</c>); возвращает результат установки.</param>
    /// <param name="saveFileDialog">Диалог сохранения файла: имя по умолчанию → путь
    /// или null при отмене.</param>
    /// <param name="openFileDialog">Диалог выбора файла: подсказка → путь или null при отмене.</param>
    public PlatformUpdateViewModel(
        IPlatformUpdateService service,
        IInfobaseRepository infobaseRepository,
        Func<IReadOnlyList<string>> loadInstalledVersions,
        Func<string, string, IProgress<double>?, CancellationToken, Task<string?>> downloadDistribution,
        Func<string, string, string?, IProgress<string>?, CancellationToken,
            Task<(bool Success, string? ErrorKey, int ExitCode)>> installFromZip,
        Func<string?, string?>? saveFileDialog = null,
        Func<string?, string?>? openFileDialog = null,
        Func<IReadOnlyList<string>>? loadRunningProcesses = null,
        Func<bool>? isAdministrator = null,
        Func<string, long?>? getFreeBytes = null,
        Func<string, bool>? hasValidSignature = null,
        Func<string, string, bool>? confirmDialog = null,
        Action<string, string, NotificationKind, NotificationEvent>? notify = null,
        Services.IAppLogger? appLogger = null)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _infobaseRepository = infobaseRepository ?? throw new ArgumentNullException(nameof(infobaseRepository));
        _loadInstalledVersions = loadInstalledVersions ?? throw new ArgumentNullException(nameof(loadInstalledVersions));
        _downloadDistribution = downloadDistribution ?? throw new ArgumentNullException(nameof(downloadDistribution));
        _installFromZip = installFromZip ?? throw new ArgumentNullException(nameof(installFromZip));
        _saveFileDialog = saveFileDialog ?? (_ => null);
        _openFileDialog = openFileDialog ?? (_ => null);

        // Проверка готовности к установке (этап 0.3.9.214): по умолчанию — «проблем нет»,
        // чтобы существующие вызовы (WPF-окно этапа 213 и тесты) продолжали работать.
        _loadRunningProcesses = loadRunningProcesses ?? (() => Array.Empty<string>());
        _isAdministrator = isAdministrator ?? (() => true);
        _getFreeBytes = getFreeBytes ?? (_ => null);
        _hasValidSignature = hasValidSignature ?? (_ => true);
        _confirmDialog = confirmDialog ?? ((_, _) => true);
        _notify = notify ?? ((_, _, _, _) => { });
        _appLogger = appLogger;

        CheckCommand = new RelayCommand(async () => await CheckUpdatesAsync(), () => !IsBusy);
        DownloadAndInstallCommand = new RelayCommand(
            async () => await DownloadAndInstallAsync(), () => !IsBusy && SelectedRow is not null);
        DownloadOnlyCommand = new RelayCommand(
            async () => await DownloadOnlyAsync(), () => !IsBusy && SelectedRow is not null);
        ChooseInstallerCommand = new RelayCommand(
            async () => await ChooseInstallerAsync(), () => !IsBusy);
        // Реализация удаления старых версий — этап 0.3.9.215; команда существует,
        // но неактивна (кнопка недоступна).
        RemoveOldVersionsCommand = new RelayCommand(
            () => AppendLog(LocalizationManager.T("PlatformUpdate.RemoveOld")), () => false);
    }

    /// <summary>Добавляет строку в журнал и уведомляет UI (<see cref="LogText"/>).
    /// Журнал ограничивается хвостом (защита от бесконечного роста при длинной загрузке).</summary>
    public void AppendLog(string message)
    {
        _log.AppendLine(message ?? string.Empty);

        const int maxLength = 64 * 1024;
        const int keepTail = 32 * 1024;
        if (_log.Length > maxLength)
        {
            var text = _log.ToString();
            _log.Clear();
            _log.Append(text.Substring(text.Length - keepTail));
        }

        OnPropertyChanged(nameof(LogText));
    }

    /// <summary>Читает установленные версии платформы через инжектируемый делегат.
    /// Ошибки чтения не роняют модель — пишутся в журнал, возвращается пустой список.</summary>
    public IReadOnlyList<string> LoadInstalledAsync()
    {
        try
        {
            return _loadInstalledVersions() ?? Array.Empty<string>();
        }
        catch (Exception ex)
        {
            AppendLog($"{LocalizationManager.T("PlatformUpdate.Error.Network")}: {ex.Message}");
            return Array.Empty<string>();
        }
    }

    /// <summary>Проверяет каталог версий платформы на портале 1С и перестраивает
    /// список строк (установленные ∪ доступные) с числом совместимых баз. Статус
    /// ошибки (авторизация/сеть/404) пишется в журнал ключом локализации;
    /// исключения сервиса гасятся.</summary>
    public async Task CheckUpdatesAsync()
    {
        if (IsBusy)
            return;

        IsBusy = true;
        Progress = 0;
        AppendLog(LocalizationManager.T("PlatformUpdate.Status.Checking"));
        _appLogger?.Info("Обновление платформы: получение каталога версий с портала 1С");
        try
        {
            var result = await _service.GetAvailableReleasesAsync().ConfigureAwait(false);
            if (result.Status != PortalFetchStatus.Ok)
            {
                AppendLog(LocalizationManager.T(result.ErrorKey));
                _appLogger?.Warn($"Обновление платформы: каталог не получен — {result.ErrorKey}");
                return;
            }

            _availableReleases = result.Releases ?? new List<PlatformRelease>();
            RebuildRows(LoadInstalledAsync(), _availableReleases);
            AppendLog(string.Format(
                LocalizationManager.T("PlatformUpdate.Progress.Done"),
                _availableReleases.FirstOrDefault()?.Version ?? "—"));
            _appLogger?.Info($"Обновление платформы: получено {_availableReleases.Count} версий каталога");
        }
        catch (Exception ex)
        {
            AppendLog($"{LocalizationManager.T("PlatformUpdate.Error.Network")}: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
            Progress = 1;
        }
    }

    /// <summary>«Скачать и установить» (Windows): ленивая подгрузка файлов выбранного
    /// релиза, выбор дистрибутива, загрузка во временный каталог
    /// <c>%TEMP%\cm_platformdl_<guid></c> с прогрессом, установка через инжектируемый
    /// установщик и перечитывание установленных версий. Все сетевые/установочные вызовы —
    /// через делегаты (тесты подменяют их fake).</summary>
    public async Task DownloadAndInstallAsync()
    {
        if (IsBusy || SelectedRow is null)
            return;

        var row = SelectedRow;
        IsBusy = true;
        row.IsDownloading = true;
        row.Progress = 0;
        try
        {
            if (!await EnsureReleaseFilesAsync(row).ConfigureAwait(false))
                return;

            var picked = _service.PickDistribution(row.Release!.Files);
            if (picked is null)
            {
                AppendLog(LocalizationManager.T("PlatformUpdate.Error.NoSetup"));
                return;
            }

            var targetDir = Path.Combine(Path.GetTempPath(), "cm_platformdl_" + Guid.NewGuid().ToString("N"));
            var zipPath = Path.Combine(targetDir, OneCUpdatesService.BuildTargetFileName(row.Version, picked.FileName));

            AppendLog(string.Format(LocalizationManager.T("PlatformUpdate.Progress.Download"), picked.FileName));
            var progress = new Progress<double>(v =>
            {
                row.Progress = v;
                Progress = v;
            });
            var downloaded = await _downloadDistribution(picked.Url, zipPath, progress, CancellationToken.None)
                .ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(downloaded))
            {
                AppendLog(LocalizationManager.T("PlatformUpdate.Error.Network"));
                return;
            }

            row.Progress = 1;
            Progress = 1;

            // Проверка готовности к установке (этап 0.3.9.214): занятые процессы 1С,
            // права администратора, свободное место, подпись файла. При замечаниях —
            // диалог подтверждения; отмена останавливает операцию до запуска установщика.
            _installHadWarnings = false;
            var preflight = RunPreflight(downloaded, targetDir, picked.SizeBytes);
            if (preflight.Count > 0)
            {
                foreach (var warning in preflight)
                    AppendLog(warning.Text);

                _appLogger?.Warn(
                    $"Обновление платформы: замечания перед установкой версии {row.Version} — " +
                    string.Join("; ", preflight.Select(w => w.Text)));

                var message = string.Join("\n", preflight.Select(w => w.Text)) + "\n\n" +
                    string.Format(LocalizationManager.T("PlatformUpdate.Confirm.InstallMessage"), row.Version);
                var confirmed = _confirmDialog(
                    LocalizationManager.T("PlatformUpdate.Confirm.InstallTitle"), message);
                AppendLog(confirmed
                    ? LocalizationManager.T("PlatformUpdate.Preflight.Continue")
                    : LocalizationManager.T("PlatformUpdate.Error.Cancelled"));
                if (!confirmed)
                {
                    _appLogger?.Info("Обновление платформы: установка отменена пользователем");
                    return;
                }

                _installHadWarnings = preflight.Any(w => w.Kind == PlatformPreflightWarningKind.Warning);
            }

            AppendLog(string.Format(LocalizationManager.T("PlatformUpdate.Progress.Install"), row.Version));
            _appLogger?.Info($"Обновление платформы: запуск установщика версии {row.Version}");

            var installLog = new Progress<string>(AppendLog);
            var result = await _installFromZip(downloaded, row.Version, null, installLog, CancellationToken.None)
                .ConfigureAwait(false);

            _appLogger?.Info($"Обновление платформы: установщик версии {row.Version} завершился с кодом {result.ExitCode}");

            if (!result.Success)
            {
                var errorText = string.IsNullOrWhiteSpace(result.ErrorKey)
                    ? LocalizationManager.T("PlatformUpdate.Error.Network")
                    : LocalizationManager.T(result.ErrorKey);
                AppendLog(errorText);
                NotifyError(string.Format(LocalizationManager.T("Notify.PlatformUpdateError"), errorText));
                return;
            }

            AppendLog(string.Format(LocalizationManager.T("PlatformUpdate.Progress.Done"), row.Version));
            _appLogger?.Info($"Обновление платформы: перечитывание установленных версий после установки {row.Version}");
            await RefreshInstalledAsync().ConfigureAwait(false);

            // Уведомление о результате: частичный успех (были предупреждения, например
            // не проверена подпись) — Warning, иначе — Success.
            NotifyResult(string.Format(LocalizationManager.T("Notify.PlatformUpdateDone"), row.Version));
        }
        catch (Exception ex)
        {
            AppendLog($"{LocalizationManager.T("PlatformUpdate.Error.Network")}: {ex.Message}");
            NotifyError(string.Format(
                LocalizationManager.T("Notify.PlatformUpdateError"),
                $"{LocalizationManager.T("PlatformUpdate.Error.Network")}: {ex.Message}"));
        }
        finally
        {
            row.IsDownloading = false;
            row.Progress = 1;
            IsBusy = false;
        }
    }

    /// <summary>«Только скачать»: ленивая подгрузка файлов релиза, выбор дистрибутива,
    /// диалог сохранения (инжектируемый делегат) и загрузка в указанный путь.
    /// Отмена диалога (null) — no-op с записью в журнал.</summary>
    public async Task DownloadOnlyAsync()
    {
        if (IsBusy || SelectedRow is null)
            return;

        var row = SelectedRow;
        IsBusy = true;
        try
        {
            if (!await EnsureReleaseFilesAsync(row).ConfigureAwait(false))
                return;

            var picked = _service.PickDistribution(row.Release!.Files);
            if (picked is null)
            {
                AppendLog(LocalizationManager.T("PlatformUpdate.Error.NoSetup"));
                return;
            }

            var defaultName = OneCUpdatesService.BuildTargetFileName(row.Version, picked.FileName);
            var targetPath = _saveFileDialog(defaultName);
            if (string.IsNullOrWhiteSpace(targetPath))
            {
                AppendLog(LocalizationManager.T("PlatformUpdate.Error.Cancelled"));
                return;
            }

            row.IsDownloading = true;
            row.Progress = 0;
            AppendLog(string.Format(LocalizationManager.T("PlatformUpdate.Progress.Download"), picked.FileName));

            var progress = new Progress<double>(v =>
            {
                row.Progress = v;
                Progress = v;
            });
            var saved = await _downloadDistribution(picked.Url, targetPath, progress, CancellationToken.None)
                .ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(saved))
            {
                AppendLog(LocalizationManager.T("PlatformUpdate.Error.Network"));
                NotifyError(string.Format(
                    LocalizationManager.T("Notify.PlatformUpdateError"),
                    LocalizationManager.T("PlatformUpdate.Error.Network")));
                return;
            }

            row.Progress = 1;
            Progress = 1;
            AppendLog(string.Format(LocalizationManager.T("PlatformUpdate.Progress.Done"), targetPath));
            _appLogger?.Info($"Обновление платформы: дистрибутив сохранён в «{targetPath}»");

            // Уведомление о завершении загрузки (категория Update, вид — успех).
            _notify(
                LocalizationManager.T("PlatformUpdate.WindowTitle"),
                string.Format(LocalizationManager.T("PlatformUpdate.Progress.Done"), targetPath),
                NotificationKind.Success,
                NotificationEvent.Update);
        }
        catch (Exception ex)
        {
            AppendLog($"{LocalizationManager.T("PlatformUpdate.Error.Network")}: {ex.Message}");
            NotifyError(string.Format(
                LocalizationManager.T("Notify.PlatformUpdateError"),
                $"{LocalizationManager.T("PlatformUpdate.Error.Network")}: {ex.Message}"));
        }
        finally
        {
            row.IsDownloading = false;
            IsBusy = false;
        }
    }

    /// <summary>«Выбрать файл установщика…»: инжектируемый диалог открытия файла; выбранный
    /// путь сохраняется в <see cref="SelectedInstallerPath"/>. Несуществующий путь —
    /// ошибка в журнале. Полный сценарий установки из локального файла — этап 0.3.9.214.</summary>
    public async Task ChooseInstallerAsync()
    {
        if (IsBusy)
            return;

        IsBusy = true;
        try
        {
            var path = _openFileDialog(null);
            if (string.IsNullOrWhiteSpace(path))
                return;

            if (!File.Exists(path))
            {
                AppendLog($"{LocalizationManager.T("PlatformUpdate.Error.NotFound")}: {path}");
                return;
            }

            SelectedInstallerPath = path;
            AppendLog(path);
        }
        catch (Exception ex)
        {
            AppendLog($"{LocalizationManager.T("PlatformUpdate.Error.Network")}: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Перечитывает установленные версии и перестраивает список строк по
    /// уже полученному каталогу (без повторного сетевого запроса). Вызывается после
    /// успешной установки — новая версия появляется в списке как установленная.</summary>
    public async Task RefreshInstalledAsync()
    {
        try
        {
            if (_availableReleases.Count == 0)
                return;
            RebuildRows(LoadInstalledAsync(), _availableReleases);
            AppendLog(LocalizationManager.T("PlatformUpdate.Status.Installed"));
        }
        catch (Exception ex)
        {
            AppendLog($"{LocalizationManager.T("PlatformUpdate.Error.Network")}: {ex.Message}");
        }

        await Task.CompletedTask.ConfigureAwait(false);
    }

    /// <summary>Лениво подгружает файлы дистрибутива релиза строки
    /// (<see cref="IPlatformUpdateService.LoadReleaseFilesAsync"/>).</summary>
    /// <returns>True — файлы готовы (или уже были загружены).</returns>
    private async Task<bool> EnsureReleaseFilesAsync(PlatformUpdateRowViewModel row)
    {
        if (row.Release is null)
        {
            AppendLog(LocalizationManager.T("PlatformUpdate.Error.NotFound"));
            return false;
        }

        if (row.Release.Files.Count > 0)
            return true;

        var result = await _service.LoadReleaseFilesAsync(row.Release).ConfigureAwait(false);
        if (result.Status != PortalFetchStatus.Ok)
        {
            AppendLog(LocalizationManager.T(result.ErrorKey));
            return false;
        }

        row.NotifyFilesChanged();
        return true;
    }

    /// <summary>Перестраивает список строк: сопоставление установленных и доступных
    /// версий (<see cref="PlatformUpdateMatcher.Merge"/>) + число совместимых баз.</summary>
    private void RebuildRows(IReadOnlyList<string> installed, IReadOnlyList<PlatformRelease> available)
    {
        IReadOnlyList<Infobase> bases;
        try
        {
            bases = _infobaseRepository.Load() ?? new List<Infobase>();
        }
        catch (Exception ex)
        {
            bases = new List<Infobase>();
            AppendLog($"{LocalizationManager.T("PlatformUpdate.Error.Network")}: {ex.Message}");
        }

        var matches = PlatformUpdateMatcher.Merge(installed, available);
        SelectedRow = null;
        Rows.Clear();
        foreach (var match in matches)
        {
            var release = available.FirstOrDefault(r =>
                string.Equals(r.Version, match.Version, StringComparison.OrdinalIgnoreCase));
            var row = new PlatformUpdateRowViewModel(match, release, files => _service.PickDistribution(files))
            {
                CompatibleBases = PlatformUpdateMatcher.CountCompatibleBases(match.Version, bases),
            };
            Rows.Add(row);
        }
    }

    /// <summary>
    /// Собирает замечания перед установкой через инжектируемые делегаты
    /// (процессы 1С, права администратора, свободное место на целевом диске,
    /// подпись файла) и формирует список <see cref="PlatformInstallPreflight.Check"/>.
    /// Любой сбой отдельного источника тихо деградирует в «проблем нет» —
    /// проверка не должна ронять установку из-за недоступности WMI/прав.
    /// </summary>
    private IReadOnlyList<PlatformInstallWarning> RunPreflight(string installerPath, string targetDir, long distributionSize)
    {
        IReadOnlyList<string> processes;
        try
        {
            processes = _loadRunningProcesses() ?? Array.Empty<string>();
        }
        catch
        {
            processes = Array.Empty<string>();
        }

        bool isAdmin;
        try
        {
            isAdmin = _isAdministrator();
        }
        catch
        {
            isAdmin = true;
        }

        long? freeBytes;
        try
        {
            freeBytes = _getFreeBytes(targetDir);
        }
        catch
        {
            freeBytes = null;
        }

        bool isSigned;
        try
        {
            isSigned = _hasValidSignature(installerPath);
        }
        catch
        {
            isSigned = true;
        }

        return PlatformInstallPreflight.Check(processes, isAdmin, freeBytes, distributionSize, isSigned);
    }

    /// <summary>Показывает уведомление о результате установки: при частичном успехе
    /// (были предупреждения) — Warning, иначе — Success. Категория события — Update.</summary>
    private void NotifyResult(string summary)
    {
        _notify(
            LocalizationManager.T("PlatformUpdate.WindowTitle"),
            summary,
            _installHadWarnings ? NotificationKind.Warning : NotificationKind.Success,
            NotificationEvent.Update);
    }

    /// <summary>Показывает уведомление об ошибке операции (категория Update, вид Error).</summary>
    private void NotifyError(string summary)
    {
        _notify(
            LocalizationManager.T("PlatformUpdate.WindowTitle"),
            summary,
            NotificationKind.Error,
            NotificationEvent.Update);
    }

    /// <summary>Обновляет доступность всех команд после изменения состояния.</summary>
    private void RefreshCommands()
    {
        CheckCommand.RaiseCanExecuteChanged();
        DownloadAndInstallCommand.RaiseCanExecuteChanged();
        DownloadOnlyCommand.RaiseCanExecuteChanged();
        ChooseInstallerCommand.RaiseCanExecuteChanged();
        RemoveOldVersionsCommand.RaiseCanExecuteChanged();
    }
}