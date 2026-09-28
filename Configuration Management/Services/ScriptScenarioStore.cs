using System.IO;
using System.Text.Json;
using Configuration_Management.Models;

namespace Configuration_Management.Services;

/// <summary>
/// Реализация <see cref="IScriptScenarioStore"/>: JSON-файлы сценариев запуска скриптов
/// в каталоге <c><DataDir>/scripts/scenarios</c>. Имя файла — санитизированный
/// <see cref="ScriptScenario.Id"/> с суффиксом <c>.script.json</c>, что исключает коллизии
/// имён и проблемы с кириллицей в пути. Сериализация — как в <see cref="BackupScenarioStore"/>
/// (отступы, включены свойства).
/// </summary>
public class ScriptScenarioStore : IScriptScenarioStore
{
    private readonly IProfileService? _profileService;
    private readonly string? _directoryOverride;
    private readonly JsonSerializerOptions _jsonOptions;

    private const string ScenariosSubDir = "scripts/scenarios";

    /// <param name="profileService">Профиль для определения каталога данных (может быть null).</param>
    /// <param name="directoryOverride">Явный каталог хранения вместо <c><DataDir>/scripts/scenarios</c>
    /// (используется в юнит-тестах для изоляции от реальных данных профиля).</param>
    public ScriptScenarioStore(IProfileService? profileService = null, string? directoryOverride = null)
    {
        _profileService = profileService;
        _directoryOverride = directoryOverride;
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
            if (!string.IsNullOrWhiteSpace(_directoryOverride))
                return _directoryOverride!;
            var dataDir = !string.IsNullOrWhiteSpace(_profileService?.CurrentProfileDataDirectory)
                ? _profileService!.CurrentProfileDataDirectory
                : PlatformPaths.AppDataDirectory;
            return Path.Combine(dataDir, ScenariosSubDir);
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<ScriptScenario> LoadAll()
    {
        try
        {
            if (!Directory.Exists(ScenariosDirectory))
                return Array.Empty<ScriptScenario>();

            var result = new List<ScriptScenario>();
            foreach (var file in Directory.EnumerateFiles(ScenariosDirectory, "*.script.json"))
            {
                try
                {
                    var scenario = JsonSerializer.Deserialize<ScriptScenario>(File.ReadAllText(file), _jsonOptions);
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
            return Array.Empty<ScriptScenario>();
        }
    }

    /// <inheritdoc />
    public void Save(ScriptScenario scenario)
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
    public ScriptScenario? Get(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return null;
        try
        {
            var path = FilePathFor(id);
            if (!File.Exists(path))
                return null;
            return JsonSerializer.Deserialize<ScriptScenario>(File.ReadAllText(path), _jsonOptions);
        }
        catch
        {
            return null;
        }
    }

    private string FilePathFor(string id)
        => Path.Combine(ScenariosDirectory, SanitizeId(id) + ".script.json");

    private static string SanitizeId(string id)
    {
        if (string.IsNullOrEmpty(id))
            return Guid.NewGuid().ToString("N");
        var invalid = Path.GetInvalidFileNameChars();
        var chars = id.Select(c => invalid.Contains(c) ? '_' : c).ToArray();
        return new string(chars);
    }
}