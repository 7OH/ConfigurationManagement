using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Configuration_Management.Services;

/// <summary>
/// Определение файловой информационной базы 1С по пути, перетащенному в главное
/// окно из проводника (Windows) / файлового менеджера (Linux) — функция №2
/// «Drag & drop добавление баз» (0.3.9.92). Чистый .NET без платформенных
/// зависимостей: покрывается unit-тестами.
/// </summary>
public static class DroppedBaseDetector
{
    /// <summary>Имя файла данных файловой ИБ 1С.</summary>
    public const string BaseDataFileName = "1Cv8.1CD";

    /// <summary>
    /// Возвращает путь файловой ИБ для одного перетащенного пути или null,
    /// если путь не является файловой базой:
    /// <list type="bullet">
    /// <item>файл с именем <see cref="BaseDataFileName"/> → родительский каталог (путь базы);</item>
    /// <item>каталог, внутри которого есть <see cref="BaseDataFileName"/> → сам каталог;</item>
    /// <item>всё остальное (файл другого имени, каталог без файла данных, серверная/веб-база) → null.</item>
    /// </list>
    /// Ошибки доступа и несуществующие пути считаются невалидными.
    /// </summary>
    public static string? ResolveBasePath(string? droppedPath)
    {
        if (string.IsNullOrWhiteSpace(droppedPath))
            return null;

        var path = droppedPath.Trim();
        try
        {
            // Файл 1Cv8.1CD — путь базы это его родительский каталог.
            if (File.Exists(path))
            {
                if (string.Equals(Path.GetFileName(path), BaseDataFileName, StringComparison.OrdinalIgnoreCase))
                    return Path.GetDirectoryName(path);
                return null;
            }

            // Каталог принимается только если внутри есть файл данных базы.
            if (Directory.Exists(path))
                return IsFileBaseDirectory(path) ? path : null;

            return null;
        }
        catch (Exception ex) when (ex is IOException
                                   or UnauthorizedAccessException
                                   or ArgumentException
                                   or NotSupportedException
                                   or System.Security.SecurityException)
        {
            return null;
        }
    }

    /// <summary>
    /// Фильтрует список перетащенных путей: возвращает пути файловых ИБ
    /// (см. <see cref="ResolveBasePath"/>). Порядок исходного списка сохраняется.
    /// </summary>
    public static IReadOnlyList<string> ResolveBasePaths(IEnumerable<string>? droppedPaths)
    {
        if (droppedPaths is null)
            return Array.Empty<string>();

        var result = new List<string>();
        foreach (var path in droppedPaths)
        {
            var basePath = ResolveBasePath(path);
            if (!string.IsNullOrWhiteSpace(basePath))
                result.Add(basePath);
        }

        return result;
    }

    /// <summary>Есть ли среди перетащенных путей хотя бы один путь файловой ИБ.</summary>
    public static bool HasAnyBasePath(IEnumerable<string>? droppedPaths) =>
        droppedPaths is not null && ResolveBasePaths(droppedPaths).Count > 0;

    /// <summary>Является ли каталог файловой ИБ (внутри есть <see cref="BaseDataFileName"/>).</summary>
    public static bool IsFileBaseDirectory(string directoryPath)
    {
        try
        {
            return !string.IsNullOrWhiteSpace(directoryPath)
                   && Directory.Exists(directoryPath)
                   && File.Exists(Path.Combine(directoryPath, BaseDataFileName));
        }
        catch (Exception ex) when (ex is IOException
                                   or UnauthorizedAccessException
                                   or ArgumentException
                                   or NotSupportedException)
        {
            return false;
        }
    }

    /// <summary>
    /// Нормализация пути для сравнения на дубликаты: обрезка пробелов и всех
    /// хвостовых разделителей каталогов (без учёта регистра сравнивает вызывающий).
    /// Корень («C:\», «/») не схлопывается в пустую строку.
    /// </summary>
    public static string NormalizePathForCompare(string? path)
    {
        var trimmed = (path ?? string.Empty).Trim();
        while (trimmed.Length > 0
               && (trimmed.EndsWith(Path.DirectorySeparatorChar)
                   || trimmed.EndsWith(Path.AltDirectorySeparatorChar)))
        {
            // Не трогаем корень: у «C:\» и «/» разделитель является частью пути.
            if (trimmed.Length == 1 || (trimmed.Length == 3 && trimmed[1] == ':'))
                break;
            trimmed = trimmed[..^1];
        }

        return trimmed;
    }

    /// <summary>Один и тот же путь базы? Регистр и хвостовые разделители игнорируются.</summary>
    public static bool AreSameBasePath(string? a, string? b) =>
        string.Equals(NormalizePathForCompare(a), NormalizePathForCompare(b),
            StringComparison.OrdinalIgnoreCase);
}