using System.IO;
using System.Linq;
using System.Text.Json;
using Configuration_Management.Models;

namespace Configuration_Management.Services;

/// <summary>
/// Реализация <see cref="IScheduledTaskStore"/>: JSON-файлы заданий в каталоге
/// <c><DataDir>/schedules</c>. Имя файла — санитизированное имя задания с
/// суффиксом-идентификатором и окончанием <c>.task.json</c>, например
/// <c>Резервная копия УТ.19fb08b20a2b434588e6d857479c50f0.task.json</c>
/// (issue #366, комментарий 7OH: «имена у файлов в папке schedules — сделать как
/// с папкой scenarios — с читаемым человеком началом»; паттерн <see cref="ScriptScenarioStore"/>,
/// issue #344): в папке сразу видно, какое задание лежит в файле, коллизии имён
/// исключены суффиксом-Id, поиск по Id детерминирован. Старые файлы вида
/// <c><id>.task.json</c> читаются без изменений и мигрируют на новое имя при
/// первом сохранении задания. Сериализация — как в <see cref="InfobaseRepository"/>
/// (отступы, включены свойства).
/// </summary>
public class ScheduledTaskStore : IScheduledTaskStore
{
    private readonly IProfileService? _profileService;
    private readonly string? _directoryOverride;
    private readonly JsonSerializerOptions _jsonOptions;

    private const string TasksSubDir = "schedules";

    /// <param name="profileService">Профиль для определения каталога данных (может быть null).</param>
    /// <param name="directoryOverride">Явный каталог хранения вместо <c><DataDir>/schedules</c>
    /// (используется в юнит-тестах для изоляции от реальных данных профиля).</param>
    public ScheduledTaskStore(IProfileService? profileService = null, string? directoryOverride = null)
    {
        _profileService = profileService;
        _directoryOverride = directoryOverride;
        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            // Кириллицу и прочие не-ASCII символы пишем читаемыми UTF-8,
            // а не \uXXXX-последовательностями (issue #320).
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };
    }

    /// <inheritdoc />
    public string TasksDirectory
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(_directoryOverride))
                return _directoryOverride!;
            var dataDir = !string.IsNullOrWhiteSpace(_profileService?.CurrentProfileDataDirectory)
                ? _profileService!.CurrentProfileDataDirectory
                : PlatformPaths.AppDataDirectory;
            return Path.Combine(dataDir, TasksSubDir);
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<ScheduledTask> LoadAll()
    {
        try
        {
            if (!Directory.Exists(TasksDirectory))
                return Array.Empty<ScheduledTask>();

            var result = new List<ScheduledTask>();
            var seenIds = new HashSet<string>(System.StringComparer.Ordinal);
            foreach (var file in Directory.EnumerateFiles(TasksDirectory, "*.task.json"))
            {
                try
                {
                    var task = JsonSerializer.Deserialize<ScheduledTask>(File.ReadAllText(file), _jsonOptions);
                    if (task is null || string.IsNullOrWhiteSpace(task.Name))
                        continue;

                    // Имя файла — источник локализации (issue #366): id из имени имеет
                    // приоритет над содержимым JSON, чтобы Get(id)/Delete(id) находили файл
                    // независимо от содержимого (в т.ч. при ручном копировании/переименовании).
                    var fileId = FileNameToId(Path.GetFileName(file));
                    if (!string.IsNullOrEmpty(fileId))
                        task.Id = fileId;

                    // Дедупликация по Id — защита от битых копий/краш-состояний, когда
                    // одно задание случайно записано в два файла.
                    if (seenIds.Add(task.Id))
                        result.Add(task);
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
            return Array.Empty<ScheduledTask>();
        }
    }

    /// <inheritdoc />
    public void Save(ScheduledTask task)
    {
        if (task is null)
            return;
        if (string.IsNullOrWhiteSpace(task.Id))
            task.Id = System.Guid.NewGuid().ToString("N");

        Directory.CreateDirectory(TasksDirectory);

        // Переименование/миграция формата (issue #366): перед записью удаляем прежние
        // файлы задания по Id (старый формат <id>.task.json и любой Name.<id>.task.json) —
        // иначе при смене имени остался бы «осиротевший» файл и в LoadAll появился дубль.
        DeleteFilesById(task.Id);

        var json = JsonSerializer.Serialize(task, _jsonOptions);
        File.WriteAllText(FilePathFor(task.Id, task.Name), json);
    }

    /// <inheritdoc />
    public void Delete(string id)
    {
        try
        {
            DeleteFilesById(id ?? string.Empty);
        }
        catch
        {
            // Удаление — не критично: файл мог быть уже удалён или занят.
        }
    }

    /// <inheritdoc />
    public ScheduledTask? Get(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return null;
        try
        {
            var file = FindFileById(id);
            if (file is null)
                return null;
            var task = JsonSerializer.Deserialize<ScheduledTask>(File.ReadAllText(file), _jsonOptions);
            if (task is null)
                return null;
            // Имя файла — источник локализации (см. LoadAll).
            var fileId = FileNameToId(Path.GetFileName(file));
            if (!string.IsNullOrEmpty(fileId))
                task.Id = fileId;
            return task;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Имя файла нового формата: <c><Имя>.<Id>.task.json</c> (issue #366).
    /// При пустом/полностью санитизированном имени возвращается прежний формат
    /// <c><Id>.task.json</c> — обратная совместимость и запасной вариант.
    /// </summary>
    private string FilePathFor(string id, string? name = null)
    {
        var sanitizedName = SanitizeName(name);
        return string.IsNullOrEmpty(sanitizedName)
            ? Path.Combine(TasksDirectory, SanitizeId(id) + ".task.json")
            : Path.Combine(TasksDirectory, sanitizedName + "." + SanitizeId(id) + ".task.json");
    }

    /// <summary>Находит файл задания по Id в обоих форматах: старом и новом.</summary>
    private string? FindFileById(string id)
    {
        var sanitized = SanitizeId(id);
        var exact = Path.Combine(TasksDirectory, sanitized + ".task.json");
        if (File.Exists(exact))
            return exact;
        // Новый формат: Name.<id>.task.json (Name может содержать точки).
        return Directory.Exists(TasksDirectory)
            ? Directory.EnumerateFiles(TasksDirectory, "*." + sanitized + ".task.json").FirstOrDefault()
            : null;
    }

    /// <summary>Удаляет все файлы задания по Id: старый формат и любые <c>Name.<id>.task.json</c>.</summary>
    private void DeleteFilesById(string id)
    {
        var sanitized = SanitizeId(id);
        var exact = Path.Combine(TasksDirectory, sanitized + ".task.json");
        if (File.Exists(exact))
            File.Delete(exact);
        if (Directory.Exists(TasksDirectory))
        {
            foreach (var file in Directory.EnumerateFiles(TasksDirectory, "*." + sanitized + ".task.json"))
                File.Delete(file);
        }
    }

    /// <summary>
    /// Извлекает id из имени файла (issue #366): для нового формата
    /// <c>Name.<id>.task.json</c> берётся последний сегмент имени без суффикса
    /// (точки в имени допустимы), для старого <c><id>.task.json</c> — весь
    /// сегмент до суффикса. Суффикс <c>.task.json</c> срезается целиком: отдельный
    /// вызов <see cref="Path.GetFileNameWithoutExtension"/> оставил бы «.task».
    /// </summary>
    internal static string? FileNameToId(string fileName)
    {
        const string suffix = ".task.json";
        if (string.IsNullOrEmpty(fileName)
            || !fileName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            return null;
        var basePart = fileName[..^suffix.Length]; // «Name.<id>» или «<id>»
        var dot = basePart.LastIndexOf('.');
        return dot < 0 ? basePart : basePart[(dot + 1)..];
    }

    /// <summary>
    /// Санитизация имени для файла (паттерн <see cref="ScriptScenarioStore"/>, issue #344):
    /// недопустимые символы → «_», обрезка до безопасной длины, зарезервированные имена
    /// устройств Windows и ведущая точка (скрытый файл на Linux) получают префикс «_».
    /// Кириллица сохраняется — она валидна на NTFS (UTF-16) и Linux (UTF-8).
    /// Возвращает пустую строку, если от имени ничего не осталось (тогда используется
    /// формат файла без имени).
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

        // Скрытые файлы на Linux начинаются с точки — имя с точки «спрячет» задание.
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
            return System.Guid.NewGuid().ToString("N");
        var invalid = Path.GetInvalidFileNameChars();
        var chars = id.Select(c => invalid.Contains(c) ? '_' : c).ToArray();
        return new string(chars);
    }
}