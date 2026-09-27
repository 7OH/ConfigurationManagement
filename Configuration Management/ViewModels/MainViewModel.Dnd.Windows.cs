#if WINDOWS
using Configuration_Management.Localization;
using Configuration_Management.Models;

namespace Configuration_Management.ViewModels;

/// <summary>
/// Платформенная часть drag&drop добавления баз из проводника (0.3.9.92, WPF):
/// добавление в ObservableCollection, сохранение, перестройка дерева и уведомления.
/// </summary>
public partial class MainViewModel
{
    partial void AppendDroppedInfobase(Infobase infobase)
    {
        Infobases.Add(infobase);
    }

    partial void FinalizeDroppedInfobases()
    {
        Save();
        RebuildGroupTree();
        ExportToIbasesAfterLocalChange();
    }

    partial void NotifyDroppedInfobasesResult(string message, bool isWarning)
    {
        if (isWarning)
            _dialogs.ShowWarning(message, LocalizationManager.T("Dnd.AddTitle"));
        else
            _dialogs.ShowInfo(message, LocalizationManager.T("Dnd.AddTitle"));
    }

    partial void NotifyDroppedInfobasesError(string message)
    {
        _dialogs.ShowError(message, LocalizationManager.T("Dnd.AddTitle"));
    }
}
#endif