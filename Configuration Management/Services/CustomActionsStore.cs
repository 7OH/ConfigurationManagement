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
/// Реализация <see cref="ICustomActionsStore"/>: пользовательские действия контекстного меню
/// (функция 7, цикл 0.3.9.193–0.3.9.199) хранятся в едином читаемом JSON-файле
/// <c>custom_actions.json</c> в каталоге данных профиля (рядом с <c>settings.json</c>), по
/// образцу <see cref="CustomConfigTypesStore"/>. Файл можно править вручную и переносить
/// между установками. Запись — атомарная (временный файл + замена), JSON с отступами и
/// читаемой кириллицей (<see cref="JavaScriptEncoder.UnsafeRelaxedJsonEscaping"/> — паттерн
/// цикла 0.3.9.155). Битый файл не роняет загрузку: возвращается пустой список, файл на
/// диске не трогается (пользователь сможет его поправить вручную).
/// </summary>
public sealed class CustomActionsStore : ICustomActionsStore
{
    private readonly IProfileService? _profileService;
    private readonly string? _directoryOverride;
    private readonly JsonSerializerOptions _jsonOptions;

    /// <summary>Имя файла пользовательских действий (рядом с настройками приложения).</summary>
    public const string FileName = "custom_actions.json";

    /// <param name="profileService">Профиль для определения каталога данных (может быть null).</param>
    /// <param name="directoryOverride">Явный каталог хранения вместо каталога данных
    /// (используется в юнит-тестах для изоляции от реальных данных профиля).</param>
    public CustomActionsStore(IProfileService? profileService = null, string? directoryOverride = null)
    {
        _profileService = profileService;
        _directoryOverride = directoryOverride;
        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            // Кириллицу и прочие не-ASCII символы пишем читаемыми UTF-8,
            // а не \uXXXX-последовательностями (паттерн цикла 0.3.9.155).
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            // Enum (ScriptShell / CustomActionScope) хранится строкой («Auto»/«Both»/…);
            // чтение принимает и числа, поэтому ручные/повреждённые файлы читаются без ошибок.
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
    public IReadOnlyList<CustomAction> LoadAll()
    {
        if (!File.Exists(FilePath))
            return Array.Empty<CustomAction>();
        try
        {
            var json = File.ReadAllText(FilePath);
            var list = JsonSerializer.Deserialize<List<CustomAction>>(json, _jsonOptions)
                       ?? new List<CustomAction>();
            return Normalize(list);
        }
        catch
        {
            // Битый/нечитаемый файл не должен ронять загрузку остальных функций:
            // возвращаем пустой список, сам файл на диске не трогаем (пользователь
            // сможет его поправить вручную).
            return Array.Empty<CustomAction>();
        }
    }

    /// <inheritdoc />
    public void Save(CustomAction action)
    {
        if (action is null)
            return;
        if (string.IsNullOrWhiteSpace(action.Id))
            action.Id = Guid.NewGuid().ToString("N");

        // Единый файл-список (по образцу CustomConfigTypesStore.Save): загружаем текущие
        // записи, заменяем/добавляем по Id и записываем список целиком.
        var all = new List<CustomAction>(LoadAll());
        var index = all.FindIndex(a => string.Equals(a.Id, action.Id, StringComparison.Ordinal));
        if (index >= 0)
            all[index] = action;
        else
            all.Add(action);

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
    public void Delete(string id)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(id) || !File.Exists(FilePath))
                return; // нет файла/пустого Id — no-op
            var all = LoadAll();
            var remaining = all.Where(a => !string.Equals(a.Id, id, StringComparison.Ordinal)).ToList();
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

    /// <inheritdoc />
    public CustomAction? Get(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return null;
        return LoadAll().FirstOrDefault(a => string.Equals(a.Id, id, StringComparison.Ordinal));
    }

    /// <summary>
    /// Приводит список к безопасному виду: отбрасывает null-элементы и записи с пустым/
    /// пробельным именем (в меню они бесполезны), восстанавливает null-строки из
    /// повреждённого/ручного файла, нормализует таймаут (<c><= 0</c> → значение по
    /// умолчанию <see cref="ExternalCommandRunner.DefaultPreCommandTimeoutMs"/>) и сортирует
    /// по имени (OrdinalIgnoreCase).
    /// </summary>
    private static List<CustomAction> Normalize(IEnumerable<CustomAction> actions)
    {
        var result = new List<CustomAction>();
        foreach (var action in actions)
        {
            if (action is null)
                continue;
            action.Id ??= Guid.NewGuid().ToString("N");
            action.Name ??= string.Empty;
            action.Command ??= string.Empty;
            action.Hotkey ??= string.Empty;
            action.WorkingDirectory ??= string.Empty;
            if (action.TimeoutMs <= 0)
                action.TimeoutMs = ExternalCommandRunner.DefaultPreCommandTimeoutMs;
            if (string.IsNullOrWhiteSpace(action.Name))
                continue;
            result.Add(action);
        }
        result.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        return result;
    }
}