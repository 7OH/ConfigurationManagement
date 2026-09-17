#if LINUX
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using Configuration_Management.Localization;
using Configuration_Management.Models;
using Configuration_Management.Services;

namespace Configuration_Management.ViewModels;

/// <summary>
/// Команды и методы функций «Сценарии резервирования» (№16) и «Список выгрузок / восстановление»
/// (№18) для Avalonia/Linux. Частичный класс <see cref="MainViewModel"/>.
/// </summary>
public partial class MainViewModel
{
    private ICommand? _showBackupScenariosCommand;
    private ICommand? _runBackupScenarioCommand;
    private ICommand? _showExportsListCommand;

    /// <summary>Команда открытия окна «Сценарии резервирования».</summary>
    public ICommand ShowBackupScenariosCommand =>
        _showBackupScenariosCommand ??= new RelayCommand(ExecuteShowBackupScenarios);

    /// <summary>Команда выполнения сценария резервирования для выбранной ИБ (Ctrl+Shift+F5).</summary>
    public ICommand RunBackupScenarioCommand =>
        _runBackupScenarioCommand ??= new RelayCommand(
            ExecuteRunBackupScenario,
            () => SelectedInfobase is not null);

    /// <summary>Команда открытия окна «Список выгрузок» (Ctrl+Shift+F7).</summary>
    public ICommand ShowExportsListCommand =>
        _showExportsListCommand ??= new RelayCommand(ExecuteShowExportsList);

    private void ExecuteShowBackupScenarios()
    {
        var win = new Configuration_Management.BackupScenariosWindow(SelectedInfobase, this);
        win.ShowSync(OwnerWindow());
    }

    private void ExecuteShowExportsList()
    {
        var win = new Configuration_Management.ExportsListWindow(SelectedInfobase, this);
        win.ShowSync(OwnerWindow());
    }

    /// <summary>Выполняет сценарий резервирования: одиночный — сразу, несколько — выбор в окне.</summary>
    private async void ExecuteRunBackupScenario()
    {
        var infobase = SelectedInfobase;
        if (infobase is null)
        {
            _dialog.ShowWarning(LocalizationManager.T("Backup.NoBaseSelected"), LocalizationManager.T("Backup.RunTitle"));
            return;
        }

        var store = AppServices.GetRequiredService<IBackupScenarioStore>();
        var scenarios = store.LoadAll();
        if (scenarios.Count == 0)
        {
            _dialog.ShowInfo(LocalizationManager.T("Backup.NoneConfigured"), LocalizationManager.T("Backup.RunTitle"));
            return;
        }

        if (scenarios.Count == 1)
        {
            await RunBackupAsync(infobase, scenarios[0]);
            return;
        }

        var win = new Configuration_Management.BackupScenariosWindow(infobase, this);
        win.ShowSync(OwnerWindow());
    }

    /// <summary>Выполняет сценарий резервирования и показывает результат.</summary>
    public async Task RunBackupAsync(Infobase infobase, BackupScenario scenario)
    {
        var service = AppServices.GetRequiredService<IBackupService>();
        var result = await service.RunAsync(infobase, scenario);
        if (result.Success)
        {
            infobase.AddLaunchHistory("Backup:" + scenario.Name, string.Join("; ", result.CreatedFiles));
            SaveSilently();
            _dialog.ShowInfo(
                string.Format(LocalizationManager.T("Backup.Done"), string.Join("\n", result.CreatedFiles)),
                LocalizationManager.T("Backup.RunTitle"));
        }
        else
        {
            _dialog.ShowError(result.ErrorMessage ?? LocalizationManager.T("Backup.Failed"), LocalizationManager.T("Backup.RunTitle"));
        }
    }

    /// <summary>
    /// Восстанавливает данные ИБ из файла выгрузки. Для ZIP/RAR сначала распаковывает во
    /// временный каталог и берёт первый .dt/.cf. Требует подтверждения.
    /// </summary>
    public async Task RestoreAsync(Infobase infobase, BackupExportItem item)
    {
        if (infobase is null || item is null)
            return;

        var archive = AppServices.GetRequiredService<IArchiveService>();
        var dtPath = item.FilePath;

        try
        {
            if (item.Format is BackupFormat.Zip or BackupFormat.Rar)
            {
                var tempDir = Path.Combine(Path.GetTempPath(), "cm_restore_" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(tempDir);
                if (!archive.ExtractArchive(item.FilePath, tempDir))
                {
                    _dialog.ShowError(LocalizationManager.T("Restore.ExtractFailed"), LocalizationManager.T("Restore.Title"));
                    return;
                }

                var found = Directory.EnumerateFiles(tempDir, "*.dt", SearchOption.AllDirectories).FirstOrDefault()
                            ?? Directory.EnumerateFiles(tempDir, "*.cf", SearchOption.AllDirectories).FirstOrDefault();
                if (found is null)
                {
                    _dialog.ShowError(LocalizationManager.T("Restore.NoDumpInArchive"), LocalizationManager.T("Restore.Title"));
                    return;
                }
                dtPath = found;
            }

            if (!_dialog.Confirm(
                string.Format(LocalizationManager.T("Restore.Confirm"), infobase.Name, dtPath),
                LocalizationManager.T("Restore.Title")))
                return;

            var service = AppServices.GetRequiredService<IBackupService>();
            var result = await service.RestoreAsync(infobase, dtPath);
            if (result.Success)
            {
                infobase.AddLaunchHistory("RestoreIB", dtPath);
                SaveSilently();
                _dialog.ShowInfo(LocalizationManager.T("Restore.Done"), LocalizationManager.T("Restore.Title"));
            }
            else
            {
                _dialog.ShowError(result.ErrorMessage ?? LocalizationManager.T("Restore.Failed"), LocalizationManager.T("Restore.Title"));
            }
        }
        finally
        {
            if (!string.Equals(dtPath, item.FilePath, System.StringComparison.OrdinalIgnoreCase))
            {
                var tempDir = Path.GetDirectoryName(dtPath);
                if (!string.IsNullOrEmpty(tempDir))
                {
                    try { Directory.Delete(tempDir, true); } catch { }
                }
            }
        }
    }
}
#endif