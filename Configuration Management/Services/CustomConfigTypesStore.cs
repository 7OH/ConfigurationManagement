using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using Configuration_Management.Models;

namespace Configuration_Management.Services;

/// <summary>
/// Реализация <see cref="ICustomConfigTypesStore"/>: пользовательские типовые конфигурации
/// 1С хранятся в отдельном читаемом JSON-файле <c>custom_config_types.json</c> в каталоге
/// данных приложения (рядом с <c>settings.json</c>). Файл можно править вручную и передавать
/// другим (issue #321). Запись — атомарная (временный файл + замена), JSON с отступами и
/// читаемой кириллицей (<see cref="JavaScriptEncoder.UnsafeRelaxedJsonEscaping"/> — паттерн
/// цикла 0.3.9.155). При первом обращении, когда файла ещё нет, пользовательские конфигурации
/// переносятся из старого хранилища <see cref="AppSettings.CustomConfigTypes"/> — миграция
/// идемпотентна (файл создаётся один раз) и не трогает настройки (обратная совместимость).
/// </summary>
public sealed class CustomConfigTypesStore : ICustomConfigTypesStore
{
    private readonly IInfobaseRepository? _repository;
    private readonly IProfileService? _profileService;
    private readonly string? _directoryOverride;
    private readonly JsonSerializerOptions _jsonOptions;

    /// <summary>Имя файла пользовательских конфигураций (рядом с настройками приложения).</summary>
    public const string FileName = "custom_config_types.json";

    /// <param name="repository">Репозиторий для чтения старых настроек при миграции (может быть null).</param>
    /// <param name="profileService">Профиль для определения каталога данных (может быть null).</param>
    /// <param name="directoryOverride">Явный каталог хранения вместо каталога данных
    /// (используется в юнит-тестах для изоляции от реальных данных профиля).</param>
    public CustomConfigTypesStore(
        IInfobaseRepository? repository = null,
        IProfileService? profileService = null,
        string? directoryOverride = null)
    {
        _repository = repository;
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
    public IReadOnlyList<OneCConfigType> Load()
    {
        if (File.Exists(FilePath))
            return ReadFile();
        // Файла ещё нет — переносим пользовательские конфигурации из старого хранилища
        // (AppSettings.CustomConfigTypes). Миграция идемпотентна: файл создаётся один раз,
        // при последующих запусках читается только файл, настройки не изменяются.
        return MigrateFromSettings();
    }

    /// <inheritdoc />
    public void Save(IReadOnlyCollection<OneCConfigType> types)
    {
        var normalized = Normalize(types ?? new List<OneCConfigType>());
        Directory.CreateDirectory(DataDirectory);
        var json = JsonSerializer.Serialize(normalized, _jsonOptions);

        // Атомарная запись: сначала во временный файл, затем замена целевого
        // (как InfobaseRepository.WriteAtomic), чтобы не оставить битый файл
        // при сбое в середине записи.
        var tempPath = FilePath + ".tmp";
        File.WriteAllText(tempPath, json);
        File.Move(tempPath, FilePath, overwrite: true);
    }

    /// <inheritdoc />
    public IReadOnlyList<OneCConfigType> LoadAll()
    {
        var result = new List<OneCConfigType>(BuiltInConfigTypes.All.Count + 8);
        result.AddRange(BuiltInConfigTypes.All);
        result.AddRange(Load());
        return result;
    }

    private IReadOnlyList<OneCConfigType> ReadFile()
    {
        try
        {
            var json = File.ReadAllText(FilePath);
            var list = JsonSerializer.Deserialize<List<OneCConfigType>>(json, _jsonOptions)
                       ?? new List<OneCConfigType>();
            return Normalize(list);
        }
        catch
        {
            // Битый/нечитаемый файл не должен ронять загрузку остальных функций:
            // возвращаем пустой список, сам файл на диске не трогаем (пользователь
            // сможет его поправить вручную).
            return new List<OneCConfigType>();
        }
    }

    private IReadOnlyList<OneCConfigType> MigrateFromSettings()
    {
        try
        {
            var settings = _repository?.LoadSettings();
            var legacy = settings?.CustomConfigTypes ?? new List<OneCConfigType>();
            if (legacy.Count == 0)
                return new List<OneCConfigType>();

            var normalized = Normalize(legacy);
            Save(normalized);
            return normalized;
        }
        catch
        {
            // Ошибка чтения старых настроек не должна блокировать окно «Типовые конфигурации».
            return new List<OneCConfigType>();
        }
    }

    /// <summary>
    /// Приводит список к безопасному виду: отбрасывает null-элементы, снимает флаг
    /// <see cref="OneCConfigType.IsBuiltIn"/> (в файле живут только пользовательские
    /// конфигурации — предопределённый набор нельзя подменить файлом), восстанавливает
    /// null-строки из повреждённого/ручного файла.
    /// </summary>
    private static List<OneCConfigType> Normalize(IEnumerable<OneCConfigType> types)
    {
        var result = new List<OneCConfigType>();
        foreach (var type in types)
        {
            if (type is null)
                continue;
            type.IsBuiltIn = false;
            type.Code ??= string.Empty;
            type.Name ??= string.Empty;
            type.UrlCode ??= string.Empty;
            type.Nick ??= string.Empty;
            type.Editions ??= new List<OneCConfigEdition>();
            foreach (var edition in type.Editions)
            {
                edition.Name ??= string.Empty;
                edition.Red ??= string.Empty;
                edition.SubRed ??= string.Empty;
                edition.UrlOverride ??= string.Empty;
            }
            result.Add(type);
        }
        return result;
    }
}