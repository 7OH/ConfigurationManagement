#if LINUX
using Configuration_Management.Localization;
using Configuration_Management.Models;

namespace Configuration_Management.ViewModels;

/// <summary>
/// Платформенная часть drag&drop добавления баз из файлового менеджера
/// (0.3.9.92, Avalonia/Linux): добавление в список, сохранение, перестройка
/// дерева и уведомления.
/// </summary>
public partial class MainViewModel
{
    partial void AppendDroppedInfobase(Infobase infobase)
    {
        _allInfobases.Add(infobase);
        OnPropertyChanged(nameof(Infobases));
    }

    partial void FinalizeDroppedInfobases()
    {
        SaveSilently();
        RebuildTree();
        ExportToIbasesAfterLocalChange();
    }

    partial void NotifyDroppedInfobasesResult(string message, bool isWarning)
    {
        if (isWarning)
            _dialog.ShowWarning(message, LocalizationManager.T("Dnd.AddTitle"));
        else
            _dialog.ShowInfo(message, LocalizationManager.T("Dnd.AddTitle"));
    }

    partial void NotifyDroppedInfobasesError(string message)
    {
        _dialog.ShowError(message, LocalizationManager.T("Dnd.AddTitle"));
    }
}
#endif