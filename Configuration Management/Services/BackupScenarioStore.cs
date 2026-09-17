using System.IO;
using System.Text.Json;
using Configuration_Management.Models;

namespace Configuration_Management.Services;

/// <summary>
/// Реализация <see cref="IBackupScenarioStore"/>: JSON-файлы сценариев в каталоге
/// <c><DataDir>/backups/scenarios</c>. Имя файла — санитизированный <see cref="BackupScenario.Id"/>
/// с суффиксом <c>.scenario.json</c>, что исключает коллизии имён и проблемы с кириллицей в пути.
/// Сериализация — как в <see cref="InfobaseRepository"/> (отступы, включены свойства).
/// </summary>
public class BackupScenarioStore : IBackupScenarioStore
{
    private readonly IProfileService? _profileService;
    private readonly JsonSerializerOptions _jsonOptions;

    private const string ScenariosSubDir = "backups/scenarios";

    public BackupScenarioStore(IProfileService? profileService = null)
    {
        _profileService = profileService;
        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true
        };
    }

    /// <inheritdoc />
    public string ScenariosDirectory
    {
        get
        {
            var dataDir = !string.IsNullOrWhiteSpace(_profileService?.CurrentProfileDataDirectory)
                ? _profileService!.CurrentProfileDataDirectory
                : PlatformPaths.AppDataDirectory;
            return Path.Combine(dataDir, ScenariosSubDir);
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<BackupScenario> LoadAll()
    {
        try
        {
            if (!Directory.Exists(ScenariosDirectory))
                return Array.Empty<BackupScenario>();

            var result = new List<BackupScenario>();
            foreach (var file in Directory.EnumerateFiles(ScenariosDirectory, "*.scenario.json"))
            {
                try
                {
                    var scenario = JsonSerializer.Deserialize<BackupScenario>(File.ReadAllText(file), _jsonOptions);
                    if (scenario is not null && !string.IsNullOrWhiteSpace(scenario.Name))
                        result.Add(scenario);
                }
                catch
                {
                    // Битый/нечитаемый файл пропускаем, не роняя загрузку остальных.
                }
            }

            result.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            return result;
        }
        catch
        {
            return Array.Empty<BackupScenario>();
        }
    }

    /// <inheritdoc />
    public void Save(BackupScenario scenario)
    {
        if (scenario is null)
            return;
        if (string.IsNullOrWhiteSpace(scenario.Id))
            scenario.Id = Guid.NewGuid().ToString("N");

        Directory.CreateDirectory(ScenariosDirectory);
        var json = JsonSerializer.Serialize(scenario, _jsonOptions);
        File.WriteAllText(FilePathFor(scenario.Id), json);
    }

    /// <inheritdoc />
    public void Delete(string id)
    {
        try
        {
            var path = FilePathFor(id);
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // Удаление — не критично: файл мог быть уже удалён или занят.
        }
    }

    /// <inheritdoc />
    public BackupScenario? Get(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return null;
        try
        {
            var path = FilePathFor(id);
            if (!File.Exists(path))
                return null;
            return JsonSerializer.Deserialize<BackupScenario>(File.ReadAllText(path), _jsonOptions);
        }
        catch
        {
            return null;
        }
    }

    private string FilePathFor(string id)
        => Path.Combine(ScenariosDirectory, SanitizeId(id) + ".scenario.json");

    private static string SanitizeId(string id)
    {
        if (string.IsNullOrEmpty(id))
            return Guid.NewGuid().ToString("N");
        var invalid = Path.GetInvalidFileNameChars();
        var chars = id.Select(c => invalid.Contains(c) ? '_' : c).ToArray();
        return new string(chars);
    }
}