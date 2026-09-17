using System.Threading.Tasks;
using Configuration_Management.Models;

namespace Configuration_Management.Services;

/// <summary>
/// Оркестратор сценариев резервирования и восстановления информационных баз.
/// Выполняет пакетные операции конфигуратора (выгрузку .dt/.cf, восстановление /RestoreIB),
/// при необходимости пакует результат в архив (ZIP/RAR) и копирует в каталоги назначения.
/// </summary>
public interface IBackupService
{
    /// <summary>Выполняет сценарий резервирования для указанной ИБ.</summary>
    Task<BackupRunResult> RunAsync(Infobase infobase, BackupScenario scenario);

    /// <summary>Восстанавливает данные ИБ из файла выгрузки (.dt) через /RestoreIB.</summary>
    Task<BackupRunResult> RestoreAsync(Infobase infobase, string filePath, BackupCredential? credential = null);
}