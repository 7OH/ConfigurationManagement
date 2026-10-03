using System.IO;
using System.Linq;
using System.Text.Json;
using Configuration_Management.Models;

namespace Configuration_Management.Services;

/// <summary>
/// Реализация <see cref="IScriptScenarioStore"/>: JSON-файлы сценариев запуска скриптов
/// в каталоге <c><DataDir>/scripts/scenarios</c>. Имя файла — санитизированное имя
/// сценария с суффиксом-идентификатором и окончанием <c>.script.json</c>, например
/// <c>Calc.19fb08b20a2b434588e6d857479c50f0.script.json</c> (issue #344): в папке сразу
/// видно, какой сценарий лежит в файле, коллизии имён исключены суффиксом-Id, поиск по Id
/// детерминирован (паттерн <c>*.<id>.script.json</c>). Старые файлы вида
/// <c><id>.script.json</c> читаются без изменений и мигрируют на новое имя при первом
/// сохранении сценария. Сериализация — как в <see cref="BackupScenarioStore"/>
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
            WriteIndented = true,
            // Кириллицу и прочие не-ASCII символы пишем читаемыми UTF-8,
            // а не \uXXXX-последовательностями (issue #320).
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            // Enum (например, ScriptScenario.Shell — issue #308, п.9) хранится строкой
            // («Auto»/«PowerShell»/…); чтение принимает и числа, поэтому старые файлы
            // и значения по умолчанию мигрируют без ошибок.
            Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
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
            var seenIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var file in Directory.EnumerateFiles(ScenariosDirectory, "*.script.json"))
            {
                try
                {
                    var scenario = JsonSerializer.Deserialize<ScriptScenario>(File.ReadAllText(file), _jsonOptions);
                    if (scenario is null || string.IsNullOrWhiteSpace(scenario.Name))
                        continue;

                    // Имя файла — источник локализации (issue #344): id из имени имеет
                    // приоритет над содержимым JSON, чтобы Get(id)/Delete(id) находили файл
                    // независимо от содержимого (в т.ч. при ручном копировании/переименовании).
                    var fileId = FileNameToId(Path.GetFileName(file));
                    if (!string.IsNullOrEmpty(fileId))
                        scenario.Id = fileId;

                    // Дедупликация по Id — защита от битых копий/краш-состояний, когда
                    // один сценарий случайно записан в два файла.
                    if (seenIds.Add(scenario.Id))
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

        // Переименование/миграция формата (issue #344): перед записью удаляем прежние
        // файлы сценария по Id (старый формат <id>.script.json и любой Name.<id>.script.json) —
        // иначе при смене имени остался бы «осиротевший» файл и в LoadAll появился дубль.
        DeleteFilesById(scenario.Id);

        var json = JsonSerializer.Serialize(scenario, _jsonOptions);
        File.WriteAllText(FilePathFor(scenario.Id, scenario.Name), json);
    }

    /// <inheritdoc />
    public void Delete(string id)
    {
        try
        {
            DeleteFilesById(id);
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
            var file = FindFileById(id);
            if (file is null)
                return null;
            var scenario = JsonSerializer.Deserialize<ScriptScenario>(File.ReadAllText(file), _jsonOptions);
            if (scenario is null)
                return null;
            // Имя файла — источник локализации (см. LoadAll).
            var fileId = FileNameToId(Path.GetFileName(file));
            if (!string.IsNullOrEmpty(fileId))
                scenario.Id = fileId;
            return scenario;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Имя файла нового формата: <c><Имя>.<Id>.script.json</c> (issue #344).
    /// При пустом/полностью санитизированном имени возвращается прежний формат
    /// <c><Id>.script.json</c> — обратная совместимость и запасной вариант.
    /// </summary>
    private string FilePathFor(string id, string? name = null)
    {
        var sanitizedName = SanitizeName(name);
        return string.IsNullOrEmpty(sanitizedName)
            ? Path.Combine(ScenariosDirectory, SanitizeId(id) + ".script.json")
            : Path.Combine(ScenariosDirectory, sanitizedName + "." + SanitizeId(id) + ".script.json");
    }

    /// <summary>Находит файл сценария по Id в обоих форматах: старом и новом.</summary>
    private string? FindFileById(string id)
    {
        var sanitized = SanitizeId(id);
        var exact = Path.Combine(ScenariosDirectory, sanitized + ".script.json");
        if (File.Exists(exact))
            return exact;
        // Новый формат: Name.<id>.script.json (Name может содержать точки).
        return Directory.Exists(ScenariosDirectory)
            ? Directory.EnumerateFiles(ScenariosDirectory, "*." + sanitized + ".script.json").FirstOrDefault()
            : null;
    }

    /// <summary>Удаляет все файлы сценария по Id: старый формат и любые <c>Name.<id>.script.json</c>.</summary>
    private void DeleteFilesById(string id)
    {
        var sanitized = SanitizeId(id);
        var exact = Path.Combine(ScenariosDirectory, sanitized + ".script.json");
        if (File.Exists(exact))
            File.Delete(exact);
        if (Directory.Exists(ScenariosDirectory))
        {
            foreach (var file in Directory.EnumerateFiles(ScenariosDirectory, "*." + sanitized + ".script.json"))
                File.Delete(file);
        }
    }

    /// <summary>
    /// Извлекает id из имени файла (issue #344): для нового формата
    /// <c>Name.<id>.script.json</c> берётся последний сегмент имени без суффикса
    /// (точки в имени допустимы), для старого <c><id>.script.json</c> — весь
    /// сегмент до суффикса. Суффикс <c>.script.json</c> срезается целиком: отдельный
    /// вызов <see cref="Path.GetFileNameWithoutExtension"/> оставил бы «.script».
    /// </summary>
    private static string? FileNameToId(string fileName)
    {
        const string suffix = ".script.json";
        if (string.IsNullOrEmpty(fileName)
            || !fileName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            return null;
        var basePart = fileName[..^suffix.Length]; // «Name.<id>» или «<id>»
        var dot = basePart.LastIndexOf('.');
        return dot < 0 ? basePart : basePart[(dot + 1)..];
    }

    /// <summary>
    /// Санитизация имени для файла (issue #344): недопустимые символы → «_», обрезка до
    /// безопасной длины, зарезервированные имена устройств Windows и ведущая точка
    /// (скрытый файл на Linux) получают префикс «_». Кириллица сохраняется — она валидна
    /// на NTFS (UTF-16) и Linux (UTF-8). Возвращает пустую строку, если от имени ничего
    /// не осталось (тогда используется формат файла без имени).
    /// </summary>
    internal static string SanitizeName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "";

        var invalid = Path.GetInvalidFileNameChars();
        var chars = name.Select(c => invalid.Contains(c) ? '_' : c).ToArray();
        var result = new string(chars).Trim();
        result = result.TrimEnd('.');

        // Обрезка: 120 символов имени + точка + 32 символа id + суффикс гарантированно
        // укладываются в лимит длины пути (255 байт на NTFS, 260 символов Win32-пути).
        if (result.Length > MaxNameSegmentLength)
            result = result[..MaxNameSegmentLength];

        if (result.Length == 0)
            return "";

        // Скрытые файлы на Linux начинаются с точки — имя с точки «спрячет» сценарий.
        if (result[0] == '.')
            result = "_" + result;

        // Зарезервированные имена устройств Windows запрещены и с расширением
        // («CON.foo» тоже недопустим) — проверяем по сегменту до первой точки.
        var baseName = result.Split('.')[0];
        if (IsReservedDeviceName(baseName))
            result = "_" + result;

        return result;
    }

    private const int MaxNameSegmentLength = 120;

    private static readonly string[] ReservedDeviceNames =
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    private static bool IsReservedDeviceName(string value)
    {
        foreach (var r in ReservedDeviceNames)
            if (string.Equals(r, value, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    private static string SanitizeId(string id)
    {
        if (string.IsNullOrEmpty(id))
            return Guid.NewGuid().ToString("N");
        var invalid = Path.GetInvalidFileNameChars();
        var chars = id.Select(c => invalid.Contains(c) ? '_' : c).ToArray();
        return new string(chars);
    }
}