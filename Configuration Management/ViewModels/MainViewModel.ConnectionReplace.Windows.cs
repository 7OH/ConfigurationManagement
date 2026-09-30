#if WINDOWS
namespace Configuration_Management.ViewModels;

/// <summary>
/// Windows-реализация платформенного хука моста массовой замены строк подключения
/// (0.3.9.189, функция 6): отложенное сохранение списка баз, пересборка дерева групп
/// и выгрузка ibases.v8i после применения/отката (см. <see cref="MainViewModel.AfterConnectionReplaceCommitted"/>).
/// </summary>
public partial class MainViewModel
{
    partial void AfterConnectionReplaceCommitted()
    {
        ScheduleSave();
        RebuildGroupTree();
        ExportToIbasesAfterLocalChange();
    }
}
#endif