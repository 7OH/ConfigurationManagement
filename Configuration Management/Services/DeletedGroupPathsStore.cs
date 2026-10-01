using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using Configuration_Management.Models;

namespace Configuration_Management.Services;

/// <summary>
/// Хранилище полных путей пустых групп, удалённых пользователем вручную
/// (issue #327). Синхронизация/импорт из ibases.v8i не должна возвращать такие
/// группы обратно: пока под путём нет баз из файла, группа не пересоздаётся.
/// Пути хранятся в отдельном читаемом JSON-файле <c>deleted_groups.json</c>
/// в каталоге данных приложения (рядом с <c>settings.json</c>) — его можно
/// поправить или удалить вручную, чтобы «вернуть» группы.
/// </summary>
public interface IDeletedGroupPathsStore
{
    /// <summary>Полный путь к файлу хранилища.</summary>
    string FilePath { get; }

    /// <summary>Читает список удалённых путей (каноническая форма «Родитель / Дочерняя»).</summary>
    IReadOnlyList<string> Load();

    /// <summary>Добавляет путь удалённой пустой группы и сохраняет файл (идемпотентно).</summary>
    void Add(string groupPath);

    /// <summary>Очищает список (удаляет файл): группы начнут возвращаться при импорте.</summary>
    void Clear();
}

/// <summary>
/// Реализация <see cref="IDeletedGroupPathsStore"/>: файл <c>deleted_groups.json</c>
/// в каталоге данных профиля/приложения. Запись — атомарная (временный файл + замена),
/// JSON с отступами и читаемой кириллицей (<see cref="JavaScriptEncoder.UnsafeRelaxedJsonEscaping"/>,
/// паттерн цикла 0.3.9.155). Битый файл не роняет приложение — возвращается пустой список.
/// </summary>
public sealed class DeletedGroupPathsStore : IDeletedGroupPathsStore
{
    /// <summary>Имя файла удалённых пустых групп (рядом с настройками приложения).</summary>
    public const string FileName = "deleted_groups.json";

    private readonly IProfileService? _profileService;
    private readonly string? _directoryOverride;
    private readonly JsonSerializerOptions _jsonOptions;

    /// <param name="profileService">Профиль для определения каталога данных (может быть null).</param>
    /// <param name="directoryOverride">Явный каталог хранения вместо каталога данных
    /// (используется в юнит-тестах для изоляции от реальных данных профиля).</param>
    public DeletedGroupPathsStore(IProfileService? profileService = null, string? directoryOverride = null)
    {
        _profileService = profileService;
        _directoryOverride = directoryOverride;
        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            // Кириллицу и прочие не-ASCII символы пишем читаемыми UTF-8,
            // а не \uXXXX-последовательностями (паттерн цикла 0.3.9.155).
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };
    }

    /// <summary>Каталог данных: явный (тесты) → каталог активного профиля → общий каталог приложения.</summary>
    private string DataDirectory =>
        !string.IsNullOrWhiteSpace(_directoryOverride)
            ? _directoryOverride!
            : (!string.IsNullOrWhiteSpace(_profileService?.CurrentProfileDataDirectory)
                ? _profileService!.CurrentProfileDataDirectory
                : PlatformPaths.AppDataDirectory);

    /// <inheritdoc />
    public string FilePath => Path.Combine(DataDirectory, FileName);

    /// <inheritdoc />
    public IReadOnlyList<string> Load()
    {
        try
        {
            if (!File.Exists(FilePath))
                return Array.Empty<string>();

            var json = File.ReadAllText(FilePath);
            var list = JsonSerializer.Deserialize<List<string>>(json, _jsonOptions)
                       ?? new List<string>();
            return Normalize(list);
        }
        catch
        {
            // Битый/нечитаемый файл не должен ронять загрузку: возвращаем пустой
            // список, сам файл не трогаем (пользователь сможет поправить вручную).
            return Array.Empty<string>();
        }
    }

    /// <inheritdoc />
    public void Add(string groupPath)
    {
        var normalized = Normalize(new List<string>(Load()) { groupPath ?? string.Empty });
        if (normalized.Count == 0)
            return;

        Directory.CreateDirectory(DataDirectory);
        var json = JsonSerializer.Serialize(normalized, _jsonOptions);

        // Атомарная запись: сначала во временный файл, затем замена целевого
        // (как CustomConfigTypesStore), чтобы не оставить битый файл при сбое.
        var tempPath = FilePath + ".tmp";
        File.WriteAllText(tempPath, json);
        File.Move(tempPath, FilePath, overwrite: true);
    }

    /// <inheritdoc />
    public void Clear()
    {
        try
        {
            if (File.Exists(FilePath))
                File.Delete(FilePath);
        }
        catch
        {
            // Очистка не критична: файл переживёт и будет перезаписан при следующем Add.
        }
    }

    /// <summary>Приводит пути к каноническому виду: обрезка, удаление пустых, без дублей.</summary>
    private static IReadOnlyList<string> Normalize(IEnumerable<string> paths)
        => paths
            .Select(p => (p ?? string.Empty).Trim())
            .Where(p => p.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(p => p, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
}