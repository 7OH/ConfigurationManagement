using Configuration_Management.Models;

namespace Configuration_Management.Services;

/// <summary>
/// Хранилище сценариев резервирования: каждый сценарий — отдельный JSON-файл
/// в каталоге <c><DataDir>/backups/scenarios</c>.
/// </summary>
public interface IBackupScenarioStore
{
    /// <summary>Каталог хранения сценариев (в каталоге данных активного профиля).</summary>
    string ScenariosDirectory { get; }

    /// <summary>Загружает все сценарии из JSON-файлов.</summary>
    IReadOnlyList<BackupScenario> LoadAll();

    /// <summary>Сохраняет сценарий в отдельный JSON-файл (создаёт или перезаписывает).</summary>
    void Save(BackupScenario scenario);

    /// <summary>Удаляет сценарий по идентификатору.</summary>
    void Delete(string id);

    /// <summary>Возвращает сценарий по идентификатору или <c>null</c>.</summary>
    BackupScenario? Get(string id);
}