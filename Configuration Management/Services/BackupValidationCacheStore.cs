using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Configuration_Management.Models;

namespace Configuration_Management.Services;

/// <summary>
/// Реализация <see cref="IBackupValidationCacheStore"/>: результаты проверки резервных
/// копий (функция 8, этап 0.3.9.200) хранятся в едином читаемом JSON-файле
/// <c>backup_validation_cache.json</c> в каталоге данных профиля (рядом с
/// <c>settings.json</c>), по образцу <see cref="CustomActionsStore"/>. Файл можно
/// править вручную и переносить между установками. Запись — атомарная (временный
/// файл + замена), JSON с отступами и читаемой кириллицей
/// (<see cref="JavaScriptEncoder.UnsafeRelaxedJsonEscaping"/> — паттерн цикла 0.3.9.155).
/// Битый файл не роняет загрузку: возвращается пустой список, файл на диске не
/// трогается (пользователь сможет его поправить вручную).
/// </summary>
public sealed class BackupValidationCacheStore : IBackupValidationCacheStore
{
    private readonly IProfileService? _profileService;
    private readonly string? _directoryOverride;
    private readonly JsonSerializerOptions _jsonOptions;

    /// <summary>Имя файла кэша результатов проверки (рядом с настройками приложения).</summary>
    public const string FileName = "backup_validation_cache.json";

    /// <param name="profileService">Профиль для определения каталога данных (может быть null).</param>
    /// <param name="directoryOverride">Явный каталог хранения вместо каталога данных
    /// (используется в юнит-тестах для изоляции от реальных данных профиля).</param>
    public BackupValidationCacheStore(IProfileService? profileService = null, string? directoryOverride = null)
    {
        _profileService = profileService;
        _directoryOverride = directoryOverride;
        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            // Кириллицу и прочие не-ASCII символы пишем читаемыми UTF-8,
            // а не \uXXXX-последовательностями (паттерн цикла 0.3.9.155).
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            // Enum (BackupValidationStatus / BackupValidationDepth) хранится строкой
            // («Valid»/«Corrupt»/«Fast»/«Full»/…); чтение принимает и числа, поэтому
            // ручные/повреждённые файлы читаются без ошибок.
            Converters = { new JsonStringEnumConverter() }
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
    public IReadOnlyList<BackupValidationResult> LoadAll()
    {
        if (!File.Exists(FilePath))
            return Array.Empty<BackupValidationResult>();
        try
        {
            var json = File.ReadAllText(FilePath);
            var list = JsonSerializer.Deserialize<List<BackupValidationResult>>(json, _jsonOptions)
                       ?? new List<BackupValidationResult>();
            return Normalize(list);
        }
        catch
        {
            // Битый/нечитаемый файл не должен ронять загрузку остальных функций:
            // возвращаем пустой список, сам файл на диске не трогаем (пользователь
            // сможет его поправить вручную).
            return Array.Empty<BackupValidationResult>();
        }
    }

    /// <inheritdoc />
    public BackupValidationResult? Get(string filePath)
    {
        var key = NormalizeFilePath(filePath);
        if (key.Length == 0)
            return null;
        return LoadAll().FirstOrDefault(
            r => string.Equals(r.FilePath, key, StringComparison.OrdinalIgnoreCase));
    }

    /// <inheritdoc />
    public void Save(BackupValidationResult result)
    {
        if (result is null)
            return;
        result.FilePath = NormalizeFilePath(result.FilePath);
        if (result.FilePath.Length == 0)
            return;

        // Единый файл-список (по образцу CustomActionsStore.Save): загружаем текущие
        // записи, заменяем/добавляем по нормализованному пути и записываем целиком.
        var all = new List<BackupValidationResult>(LoadAll());
        var index = all.FindIndex(
            r => string.Equals(r.FilePath, result.FilePath, StringComparison.OrdinalIgnoreCase));
        if (index >= 0)
            all[index] = result;
        else
            all.Add(result);

        Directory.CreateDirectory(DataDirectory);
        var json = JsonSerializer.Serialize(all, _jsonOptions);

        // Атомарная запись: сначала во временный файл, затем замена целевого
        // (как InfobaseRepository.WriteAtomic / CustomConfigTypesStore.Save), чтобы
        // не оставить битый файл при сбое в середине записи.
        var tempPath = FilePath + ".tmp";
        File.WriteAllText(tempPath, json);
        File.Move(tempPath, FilePath, overwrite: true);
    }

    /// <inheritdoc />
    public void Delete(string filePath)
    {
        try
        {
            var key = NormalizeFilePath(filePath);
            if (key.Length == 0 || !File.Exists(FilePath))
                return; // нет файла/пустого пути — no-op
            var all = LoadAll();
            var remaining = all.Where(
                r => !string.Equals(r.FilePath, key, StringComparison.OrdinalIgnoreCase)).ToList();
            if (remaining.Count == all.Count)
                return; // записи нет — no-op, файл не трогаем

            Directory.CreateDirectory(DataDirectory);
            var json = JsonSerializer.Serialize(remaining, _jsonOptions);
            var tempPath = FilePath + ".tmp";
            File.WriteAllText(tempPath, json);
            File.Move(tempPath, FilePath, overwrite: true);
        }
        catch
        {
            // Удаление — не критично: файл мог быть уже удалён или занят.
        }
    }

    /// <summary>Нормализует путь файла: абсолютный (GetFullPath), с платформенными разделителями.</summary>
    public static string NormalizeFilePath(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return string.Empty;
        try
        {
            return Path.GetFullPath(filePath.Trim());
        }
        catch
        {
            // Некорректный путь (например, недопустимые символы) — не допускаем запись.
            return string.Empty;
        }
    }

    /// <summary>
    /// Приводит список к безопасному виду: отбрасывает null-элементы и записи с
    /// пустым/пробельным путём, восстанавливает null-строки из повреждённого/ручного
    /// файла, нормализует пути (в т.ч. регистр ключей для сравнения) и сортирует
    /// по пути (OrdinalIgnoreCase).
    /// </summary>
    private static List<BackupValidationResult> Normalize(IEnumerable<BackupValidationResult> results)
    {
        var result = new List<BackupValidationResult>();
        foreach (var item in results)
        {
            if (item is null)
                continue;
            item.FilePath = NormalizeFilePath(item.FilePath);
            if (item.FilePath.Length == 0)
                continue; // запись без пути бесполезна в кэше
            item.ErrorKind ??= string.Empty;
            item.ErrorDetail ??= string.Empty;
            item.Fingerprint ??= string.Empty;
            result.Add(item);
        }
        result.Sort((a, b) => string.Compare(a.FilePath, b.FilePath, StringComparison.OrdinalIgnoreCase));
        return result;
    }
}