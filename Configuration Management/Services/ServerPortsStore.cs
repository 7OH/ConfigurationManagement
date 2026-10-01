using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using Configuration_Management.Models;

namespace Configuration_Management.Services;

/// <summary>
/// Сохранённые порты сервисов 1С для конкретного сервера (issue #335): кластер,
/// rac/монитор (агент) и хранилище конфигурации. 0 — «не задан» (используется порт
/// по умолчанию из <see cref="OneCPorts"/>).
/// </summary>
public sealed record ServerPortsSettings(int Cluster, int Agent, int Repository)
{
    /// <summary>Пустая запись (все порты по умолчанию).</summary>
    public static readonly ServerPortsSettings Empty = new(0, 0, 0);
}

/// <summary>
/// Хранилище портов серверов 1С в разрезе сервера (issue #335): окно «Диагностика
/// подключения» запоминает порт на каждый сервис (кластер, rac/монитор, хранилище)
/// и подставляет сохранённый порт при смене сервера в списке.
/// </summary>
public interface IServerPortsStore
{
    /// <summary>Читает все сохранённые записи «сервер → порты» (ключ — имя сервера без порта).</summary>
    IReadOnlyDictionary<string, ServerPortsSettings> Load();

    /// <summary>Сохраняет порты для сервера (добавляет или обновляет запись).</summary>
    void Save(string server, ServerPortsSettings ports);
}

/// <summary>
/// Реализация <see cref="IServerPortsStore"/>: файл <c>server_ports.json</c> в каталоге
/// данных профиля/приложения (рядом с <c>settings.json</c>). Запись — атомарная
/// (временный файл + замена), JSON с отступами и читаемой кириллицей (паттерн цикла
/// 0.3.9.155). Битый файл не роняет приложение — возвращается пустой словарь.
/// </summary>
public sealed class ServerPortsStore : IServerPortsStore
{
    /// <summary>Имя файла портов серверов (рядом с настройками приложения).</summary>
    public const string FileName = "server_ports.json";

    private readonly IProfileService? _profileService;
    private readonly string? _directoryOverride;
    private readonly JsonSerializerOptions _jsonOptions;

    /// <param name="profileService">Профиль для определения каталога данных (может быть null).</param>
    /// <param name="directoryOverride">Явный каталог хранения вместо каталога данных
    /// (используется в юнит-тестах для изоляции от реальных данных профиля).</param>
    public ServerPortsStore(IProfileService? profileService = null, string? directoryOverride = null)
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

    /// <summary>Полный путь к файлу хранилища.</summary>
    public string FilePath => Path.Combine(DataDirectory, FileName);

    /// <inheritdoc />
    public IReadOnlyDictionary<string, ServerPortsSettings> Load()
    {
        try
        {
            if (!File.Exists(FilePath))
                return new Dictionary<string, ServerPortsSettings>();

            var json = File.ReadAllText(FilePath);
            var raw = JsonSerializer.Deserialize<Dictionary<string, ServerPortsSettings>>(json, _jsonOptions);
            return Normalize(raw);
        }
        catch
        {
            // Битый/нечитаемый файл не должен ронять загрузку: возвращаем пустой
            // словарь, сам файл не трогаем (пользователь сможет поправить вручную).
            return new Dictionary<string, ServerPortsSettings>();
        }
    }

    /// <inheritdoc />
    public void Save(string server, ServerPortsSettings ports)
    {
        var key = (server ?? string.Empty).Trim();
        if (key.Length == 0)
            return;

        // Объединяем с уже сохранёнными записями: Save обновляет один сервер,
        // остальные серверы файла сохраняются нетронутыми.
        var all = new Dictionary<string, ServerPortsSettings>(
            Normalize(ReadRaw()), StringComparer.OrdinalIgnoreCase);
        all[key] = Sanitize(ports ?? ServerPortsSettings.Empty);

        Directory.CreateDirectory(DataDirectory);
        var json = JsonSerializer.Serialize(all, _jsonOptions);

        // Атомарная запись: сначала во временный файл, затем замена целевого
        // (как CustomConfigTypesStore), чтобы не оставить битый файл при сбое.
        var tempPath = FilePath + ".tmp";
        File.WriteAllText(tempPath, json);
        File.Move(tempPath, FilePath, overwrite: true);
    }

    /// <summary>Читает сырой словарь из файла (пустой при отсутствии/битости файла).</summary>
    private Dictionary<string, ServerPortsSettings>? ReadRaw()
    {
        try
        {
            if (!File.Exists(FilePath))
                return null;
            return JsonSerializer.Deserialize<Dictionary<string, ServerPortsSettings>>(
                File.ReadAllText(FilePath), _jsonOptions);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Приводит словарь к каноническому виду: ключи — серверы без пробелов,
    /// без дублей (регистронезависимо), непустые; битые значения заменяются пустыми.</summary>
    private static IReadOnlyDictionary<string, ServerPortsSettings> Normalize(
        Dictionary<string, ServerPortsSettings>? source)
    {
        var result = new Dictionary<string, ServerPortsSettings>(StringComparer.OrdinalIgnoreCase);
        if (source is null)
            return result;

        foreach (var (key, value) in source)
        {
            var server = (key ?? string.Empty).Trim();
            if (server.Length == 0)
                continue;

            var ports = value ?? ServerPortsSettings.Empty;
            result[server] = Sanitize(ports);
        }
        return result;
    }

    /// <summary>Ограничивает порты допустимым диапазоном (0 — не задан).</summary>
    private static ServerPortsSettings Sanitize(ServerPortsSettings ports)
        => new(Clamp(ports.Cluster), Clamp(ports.Agent), Clamp(ports.Repository));

    private static int Clamp(int port) => port is >= 1 and <= 65535 ? port : 0;
}