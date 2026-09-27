using System;
using System.IO;
using System.Linq;

namespace Configuration_Management.Services;

/// <summary>
/// Чистая логика клонирования файловой ИБ (функция «Дублировать базу»):
/// имя клона, целевой каталог рядом с исходным, рекурсивное копирование.
/// Не зависит от платформы и UI — покрыта юнит-тестами.
/// </summary>
public static class InfobaseCloneHelper
{
    /// <summary>Предлагаемое имя клона: «&lt;Имя&gt; — Копия».</summary>
    public static string ProposeCloneName(string sourceName)
    {
        var name = (sourceName ?? "").Trim();
        return name.Length == 0 ? "Копия" : $"{name} — Копия";
    }

    /// <summary>
    /// Каталог клона: рядом с каталогом источника, с именем по названию базы
    /// (недопустимые символы имени файла заменяются на «_»). Если каталог занят —
    /// суффикс « 2», « 3», … Пустое имя или источник без каталога дают null.
    /// </summary>
    public static string? BuildTargetDirectory(string? sourceDir, string newName)
    {
        if (string.IsNullOrWhiteSpace(sourceDir) || !Directory.Exists(sourceDir))
            return null;

        var safe = SanitizeFileName(newName);
        if (safe.Length == 0)
            return null;

        var parent = Directory.GetParent(sourceDir)?.FullName;
        if (string.IsNullOrEmpty(parent))
            return null;

        var candidate = Path.Combine(parent, safe);
        var suffix = 2;
        while (Directory.Exists(candidate) || File.Exists(candidate))
        {
            candidate = Path.Combine(parent, $"{safe} {suffix}");
            suffix++;
        }

        return candidate;
    }

    /// <summary>
    /// Рекурсивно копирует каталог ИБ (файлы и подкаталоги). Возвращает false,
    /// если источник исчез, цель уже существует или копирование не удалось —
    /// исключения не бросает и частичную цель не удаляет (решение за вызывающим).
    /// </summary>
    public static bool CopyDirectory(string sourceDir, string targetDir)
    {
        try
        {
            var src = Path.GetFullPath(sourceDir);
            var dst = Path.GetFullPath(targetDir);
            if (!Directory.Exists(src) || Directory.Exists(dst))
                return false;

            Directory.CreateDirectory(dst);

            foreach (var file in Directory.EnumerateFiles(src, "*", SearchOption.AllDirectories))
            {
                var relative = file[src.Length..].TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                var destination = Path.Combine(dst, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(file, destination, overwrite: false);
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Размер каталога в байтах по всем файлам; при ошибке — −1.</summary>
    public static long GetDirectorySize(string dir)
    {
        try
        {
            return Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)
                .Sum(f =>
                {
                    try { return new FileInfo(f).Length; }
                    catch { return 0L; }
                });
        }
        catch
        {
            return -1;
        }
    }

    private static string SanitizeFileName(string value)
    {
        if (string.IsNullOrEmpty(value))
            return "";
        var invalid = Path.GetInvalidFileNameChars();
        var chars = value.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray();
        return new string(chars).Trim();
    }
}
