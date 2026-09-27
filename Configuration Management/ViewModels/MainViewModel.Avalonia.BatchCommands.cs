#if LINUX
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Configuration_Management.Localization;
using Configuration_Management.Models;
using Configuration_Management.Services;

namespace Configuration_Management.ViewModels;

/// <summary>
/// Пакетные операции над мультивыделением (0.3.9.90, Avalonia/Linux): назначение
/// тега, перемещение в группу, избранное, резервирование, доступность, удаление.
/// Диалоги — платформенные (Avalonia); изменения данных — через существующие
/// методы VM (теги, дерево, настройки, история запусков).
/// </summary>
public partial class MainViewModel
{
    /// <summary>Число баз в мультивыделении (для заголовка блока меню).</summary>
    public string BatchMenuTitle => string.Format(
        LocalizationManager.T("Main.BatchForSelected"),
        _batchSelectedIds.Count);

    /// <summary>Назначает введённый тег всем базам мультивыделения.</summary>
    public void AssignTagToBatch(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
            return;
        var bases = BatchSelectedInfobases.ToList();
        if (bases.Count == 0)
            return;

        var trimmed = tag!.Trim();
        var wasFiltering = IsFilterModeActive();
        foreach (var ib in bases)
        {
            if (!ib.Tags.Contains(trimmed, StringComparer.OrdinalIgnoreCase))
            {
                ib.Tags.Add(trimmed);
                ib.NotifyTagsChanged();
            }
        }
        SaveSilently();
        RebuildTagFilters();

        if (wasFiltering || IsFilterModeActive())
            ApplyFilter();
    }

    /// <summary>Добавляет все базы мультивыделения в избранное (без снятия).</summary>
    public void AddBatchToFavorites()
    {
        foreach (var ib in BatchSelectedInfobases.ToList())
        {
            if (ib.IsFavorite)
                continue;
            ib.IsFavorite = true;
            if (!_favoriteHotkeyIds.Contains(FavoriteKey(ib)) && _favoriteHotkeyIds.Count < 9)
                _favoriteHotkeyIds.Add(FavoriteKey(ib));
        }
        SyncFavoriteHotkeys();
        SaveSettingsSilently();

        if (IsFilterModeActive())
            ApplyFilter();
    }

    /// <summary>Перемещает все базы мультивыделения в выбранную группу.</summary>
    public void MoveBatchToGroup(string groupFullPath)
    {
        foreach (var ib in BatchSelectedInfobases.ToList())
            MoveInfobaseToGroup(ib, groupFullPath);
    }

    /// <summary>
    /// Выполняет сценарий резервирования для всех баз мультивыделения.
    /// Сценарий выбирается в окне «Сценарии резервирования»; выполнение идёт
    /// последовательно по выбранным базам.
    /// </summary>
    public async Task RunBatchBackupAsync()
    {
        var bases = BatchSelectedInfobases.ToList();
        if (bases.Count == 0)
            return;

        var store = AppServices.GetRequiredService<IBackupScenarioStore>();
        var scenarios = store.LoadAll();
        if (scenarios.Count == 0)
        {
            _dialog.ShowInfo(LocalizationManager.T("Backup.NoneConfigured"), LocalizationManager.T("Backup.RunTitle"));
            return;
        }

        if (scenarios.Count == 1)
        {
            foreach (var ib in bases)
                await RunBackupAsync(ib, scenarios[0]);
            return;
        }

        var win = new Configuration_Management.BackupScenariosWindow(batchBases: bases, vm: this);
        win.ShowSync(OwnerWindow());
    }

    /// <summary>Проверяет доступность только баз мультивыделения.</summary>
    public void CheckBatchAvailability()
    {
        if (_availabilityCheckRunning)
            return;

        var targets = BatchSelectedInfobases.ToList();
        if (targets.Count == 0)
        {
            ShowTemporaryStatusMessage(LocalizationManager.T("Main.ConfigListEmpty"));
            return;
        }

        _availabilityCheckRunning = true;
        IsLoading = true;
        LoadingMessage = LocalizationManager.T("Main.CheckAvailabilityLabel");
        foreach (var ib in targets)
            ib.SetChecking(true);
        RebuildTree();
        StatusBarInfo = string.Format(
            LocalizationManager.T("Main.AvailabilityProgress"), 0, targets.Count);

        _ = RunAvailabilityCheckAsync(targets);
    }

    /// <summary>
    /// Удаляет все базы мультивыделения: для каждой открывается общее окно
    /// подтверждения (<see cref="Configuration_Management.DeleteInfobaseWindow"/>).
    /// Физическое удаление каталога — по флагу окна.
    /// </summary>
    public void DeleteBatch()
    {
        var bases = BatchSelectedInfobases.ToList();
        if (bases.Count == 0)
            return;

        var changed = false;
        foreach (var ib in bases)
        {
            var dialog = new Configuration_Management.DeleteInfobaseWindow(ib);
            if (!dialog.ShowDialogSync(OwnerWindow()) || !dialog.Confirmed)
                continue;

            if (dialog.DeletePhysically)
            {
                var error = InfobaseMaintenanceService.TryDeleteFileBasePhysically(ib);
                if (error is not null)
                {
                    _dialog.ShowError(error);
                    if (!_dialog.Confirm(LocalizationManager.T("Main.ConfirmDeleteFromList")))
                        continue;
                }
            }

            _allInfobases.Remove(ib);
            if (ReferenceEquals(SelectedInfobase, ib))
                SelectedInfobase = null;
            changed = true;
        }

        if (!changed)
            return;

        SyncFavoriteHotkeys();
        SaveSilently();
        RebuildTree();
        ExportToIbasesAfterLocalChange();
    }
}
#endif