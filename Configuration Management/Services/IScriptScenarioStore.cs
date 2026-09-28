using Configuration_Management.Models;

namespace Configuration_Management.Services;

/// <summary>
/// Хранилище сценариев запуска скриптов: каждый сценарий — отдельный JSON-файл
/// в каталоге <c><DataDir>/scripts/scenarios</c> (по образцу
/// <see cref="IBackupScenarioStore"/>).
/// </summary>
public interface IScriptScenarioStore
{
    /// <summary>Каталог хранения сценариев (в каталоге данных активного профиля).</summary>
    string ScenariosDirectory { get; }

    /// <summary>Загружает все сценарии из JSON-файлов.</summary>
    IReadOnlyList<ScriptScenario> LoadAll();

    /// <summary>Сохраняет сценарий в отдельный JSON-файл (создаёт или перезаписывает).</summary>
    void Save(ScriptScenario scenario);

    /// <summary>Удаляет сценарий по идентификатору.</summary>
    void Delete(string id);

    /// <summary>Возвращает сценарий по идентификатору или <c>null</c>.</summary>
    ScriptScenario? Get(string id);
}