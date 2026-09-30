using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Configuration_Management.Models;

namespace Configuration_Management.Services;

/// <summary>
/// Чистый отбор кандидатов на удаление старых версий технологической платформы 1С
/// (этап 0.3.9.215, функция «Автообновление платформы»). Класс без платформенных
/// зависимостей (обе платформы) — только правила отбора, без файловой системы,
/// сети и UI; покрывается юнит-тестами.
/// </summary>
public static class OldVersionCleaner
{
    /// <summary>
    /// Отбирает версии, которые можно предложить к удалению:
    /// <list type="bullet">
    /// <item>исключается новейшая установленная версия (численное сравнение чистой
    /// версии из <see cref="PlatformVersionInfo.Display"/> через
    /// <see cref="OneCPlatformCatalogParser.CompareVersions"/>; обе разрядности
    /// новейшего номера — «8.3.27.2214 (64)» и «8.3.27.2214 (32)» — исключаются);</item>
    /// <item>исключаются версии, на которые ссылаются базы репозитория
    /// (<see cref="Infobase.PlatformVersion"/> начинается с префикса версии —
    /// численное совпадение либо сегментный префикс, регистронезависимо;
    /// пустая версия у базы не считается);</item>
    /// <item>исключаются версии запущенных процессов: путь процесса
    /// (<paramref name="runningBinPaths"/>) сопоставляется с каталогом версии
    /// (<see cref="PlatformVersionInfo.Path"/>) префиксом каталога — процесс
    /// запущен из bin этой версии.</item>
    /// </list>
    /// Результат отсортирован по убыванию версии. Пустой вход — пустой список
    /// без исключений.
    /// </summary>
    /// <param name="installed">Установленные версии платформы (Display + путь каталога).</param>
    /// <param name="bases">Базы репозитория (совместимость по <see cref="Infobase.PlatformVersion"/>).</param>
    /// <param name="runningBinPaths">Пути к исполняемым файлам запущенных процессов 1С
    /// (например «C:\Program Files\1cv8\8.3.27.1688\bin\1cv8c.exe»).</param>
    /// <returns>Кандидаты на удаление, отсортированные по убыванию версии.</returns>
    public static List<PlatformVersionInfo> SelectCandidates(
        IReadOnlyList<PlatformVersionInfo> installed,
        IReadOnlyList<Infobase> bases,
        IReadOnlyList<string> runningBinPaths)
    {
        var versions = (installed ?? Array.Empty<PlatformVersionInfo>())
            .Where(v => v is not null && !string.IsNullOrWhiteSpace(v.Display))
            .ToList();
        if (versions.Count == 0)
            return new List<PlatformVersionInfo>();

        // Новейшая установленная версия: максимальный числовой номер Display.
        var newestDisplay = versions[0].Display;
        foreach (var version in versions.Skip(1))
        {
            if (OneCPlatformCatalogParser.CompareVersions(version.Display, newestDisplay) > 0)
                newestDisplay = version.Display;
        }

        var result = new List<PlatformVersionInfo>(versions.Count);
        foreach (var version in versions)
        {
            if (OneCPlatformCatalogParser.CompareVersions(version.Display, newestDisplay) == 0)
                continue; // новейшая установленная (любая разрядность этого номера)

            if (IsReferencedByBase(version.Display, bases))
                continue; // на версию ссылается хотя бы одна база

            if (IsRunningFromDirectory(version.Path, runningBinPaths))
                continue; // из этой версии запущен процесс 1С

            result.Add(version);
        }

        result.Sort((a, b) => OneCPlatformCatalogParser.CompareVersions(b.Display, a.Display));
        return result;
    }

    /// <summary>
    /// Чистая версия из Display: суффикс разрядности «(64)»/«(32)» отбрасывается
    /// («8.3.27.2214 (64)» → «8.3.27.2214»). Строка без суффикса возвращается как есть.
    /// </summary>
    public static string CleanVersion(string? display)
    {
        var value = (display ?? string.Empty).Trim();
        var end = value.LastIndexOf(')');
        var start = value.LastIndexOf('(');
        if (end > start && start >= 0)
        {
            var arch = value.Substring(start + 1, end - start - 1).Trim();
            if (arch is "64" or "32")
                return value.Substring(0, start).Trim();
        }

        return value;
    }

    /// <summary>
    /// Извлекает путь исполняемого файла из командной строки процесса (первый токен
    /// с учётом обрамляющих кавычек): «"C:\…\1cv8c.exe" /F …» → «C:\…\1cv8c.exe»,
    /// «/opt/1cv8/8.3.27.2214/bin/1cv8c /F …» → «/opt/1cv8/8.3.27.2214/bin/1cv8c».
    /// null — командная строка пуста или кавычка не закрыта.
    /// </summary>
    public static string? ExtractExecutablePath(string? commandLine)
    {
        var line = (commandLine ?? string.Empty).TrimStart();
        if (line.Length == 0)
            return null;

        if (line[0] == '"')
        {
            var end = line.IndexOf('"', 1);
            return end < 0 ? null : line.Substring(1, end - 1).Trim();
        }

        var space = line.IndexOf(' ');
        var tab = line.IndexOf('\t');
        var cut = space < 0 ? tab : (tab < 0 ? space : Math.Min(space, tab));
        return cut < 0 ? line.Trim() : line.Substring(0, cut).Trim();
    }

    /// <summary>Ссылается ли хотя бы одна база на указанную версию платформы:
    /// численное равенство (учитывает суффикс разрядности базы) либо база задана
    /// частичной версией-префиксом, охватывающей кандидата («8.3.26» → «8.3.26.1890»).</summary>
    private static bool IsReferencedByBase(string versionDisplay, IReadOnlyList<Infobase>? bases)
    {
        var candidateVersion = CleanVersion(versionDisplay);
        if (candidateVersion.Length == 0)
            return false;

        foreach (var infobase in bases ?? Array.Empty<Infobase>())
        {
            var baseVersion = (infobase?.PlatformVersion ?? string.Empty).Trim();
            if (baseVersion.Length == 0)
                continue;

            // Численное совпадение: «8.3.27.1688» и «8.3.27.1688 (64)» — одна версия.
            if (OneCPlatformCatalogParser.CompareVersions(baseVersion, candidateVersion) == 0)
                return true;

            // База задана префиксом версии («8.3.27») — охватывает всё семейство.
            if (MatchesVersionPrefix(candidateVersion, baseVersion))
                return true;
        }

        return false;
    }

    /// <summary>Запущен ли процесс из каталога версии: путь процесса начинается
    /// с каталога версии (регистронезависимо, с учётом границы пути).</summary>
    private static bool IsRunningFromDirectory(string? versionDirectory, IReadOnlyList<string>? runningBinPaths)
    {
        var dir = (versionDirectory ?? string.Empty).Trim().TrimEnd(
            Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (dir.Length == 0)
            return false;

        foreach (var binPath in runningBinPaths ?? Array.Empty<string>())
        {
            var processPath = (binPath ?? string.Empty).Trim();
            if (processPath.Length == 0)
                continue;

            if (processPath.Equals(dir, StringComparison.OrdinalIgnoreCase))
                return true;

            // Граница пути: следующий символ — разделитель каталогов, иначе
            // «8.3.27.1688» поймал бы и «8.3.27.16882» (ложное срабатывание).
            if (StartsWithBoundary(processPath, dir + Path.DirectorySeparatorChar))
                return true;
            if (Path.AltDirectorySeparatorChar != Path.DirectorySeparatorChar &&
                StartsWithBoundary(processPath, dir + Path.AltDirectorySeparatorChar))
                return true;
        }

        return false;
    }

    private static bool StartsWithBoundary(string processPath, string prefix)
        => processPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);

    /// <summary>Начинается ли фактическая версия с указанного префикса по сегментам
    /// («8.3.27.1688» соответствует «8.3.27», но не «8.5» или «8.3.2»). Паттерн
    /// приватного хелпера <c>PlatformUpdateMatcher.MatchesVersionPrefix</c>.</summary>
    private static bool MatchesVersionPrefix(string version, string prefix)
    {
        var vParts = (version ?? string.Empty).Split('.');
        var pParts = (prefix ?? string.Empty).Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (vParts.Length < pParts.Length)
            return false;

        for (var i = 0; i < pParts.Length; i++)
        {
            if (!string.Equals(vParts[i].Trim(), pParts[i].Trim(), StringComparison.OrdinalIgnoreCase))
                return false;
        }

        return true;
    }
}