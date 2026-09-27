using System.IO;
using System.Linq;

namespace Configuration_Management.Services;

/// <summary>
/// Чистая логика ротации резервных копий (0.3.9.86): по каталогу назначения
/// выбирает файлы с заданным расширением, имя которых начинается с префикса
/// базы (безопасная часть шаблона имени), сортирует по времени последней
/// записи по убыванию и удаляет копии за пределами лимита количества
/// (<paramref name="keepCount"/>) и старше заданного возраста
/// (<paramref name="deleteOlderThanDays"/>). Используется и Windows, и Linux.
/// </summary>
public static class BackupRotation
{
    /// <summary>
    /// Применяет правила ротации к каталогу назначения.
    /// </summary>
    /// <param name="directory">Каталог назначения резервных копий.</param>
    /// <param name="prefix">
    /// Префикс имени файла (обычно <see cref="Models.BackupScenario.BasePrefix"/> —
    /// безопасная часть шаблона, не зависящая от имени ИБ). Пустая строка
    /// соответствует любому файлу с расширением <paramref name="extension"/>.
    /// </param>
    /// <param name="extension">Расширение файла (с точкой или без, например «.dt»).</param>
    /// <param name="keepCount">Сколько последних копий хранить; ≤ 0 — хранить все.</param>
    /// <param name="deleteOlderThanDays">Удалять копии старше N дней; ≤ 0 — не удалять по возрасту.</param>
    /// <returns>Пути фактически удалённых файлов (в порядке возрастания «давности»).</returns>
    public static IReadOnlyList<string> Apply(
        string directory, string prefix, string extension, int keepCount, int deleteOlderThanDays)
    {
        var deleted = new List<string>();
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            return deleted;

        var ext = NormalizeExtension(extension);
        var normalizedPrefix = prefix?.Trim() ?? string.Empty;

        var files = Directory.EnumerateFiles(directory, "*" + ext)
            .Where(path =>
                string.IsNullOrEmpty(normalizedPrefix)
                || Path.GetFileName(path).StartsWith(normalizedPrefix, StringComparison.OrdinalIgnoreCase))
            .Select(path => new FileInfo(path))
            .OrderByDescending(f => SafeLastWriteUtc(f))
            .ToList();

        // Возрастной порог: старше порога — кандидаты на удаление независимо от лимита.
        DateTime? ageCutoff = deleteOlderThanDays > 0
            ? DateTime.UtcNow - TimeSpan.FromDays(deleteOlderThanDays)
            : null;

        // Проходим от самой старой копии к свежей: всё, что за пределами лимита
        // количества И/ИЛИ старше порога возраста, удаляется.
        for (var i = files.Count - 1; i >= 0; i--)
        {
            var file = files[i];
            var overKeepLimit = keepCount > 0 && i >= keepCount;
            var tooOld = ageCutoff.HasValue && SafeLastWriteUtc(file) < ageCutoff.Value;
            if (!overKeepLimit && !tooOld)
                continue;

            try
            {
                file.Delete();
                deleted.Add(file.FullName);
            }
            catch (IOException)
            {
                // Файл занят или только что удалён — пропускаем, это не ошибка сценария.
            }
            catch (UnauthorizedAccessException)
            {
                // Нет прав на удаление — пропускаем.
            }
        }

        return deleted;
    }

    /// <summary>Время последней записи файла в UTC; при ошибке — минимальное.</summary>
    private static DateTime SafeLastWriteUtc(FileInfo file)
    {
        try { return file.LastWriteTimeUtc; }
        catch (IOException) { return DateTime.MinValue; }
        catch (UnauthorizedAccessException) { return DateTime.MinValue; }
    }

    /// <summary>Нормализует расширение к виду «.ext» (без изменений, если точка уже есть).</summary>
    private static string NormalizeExtension(string extension)
    {
        var ext = (extension ?? string.Empty).Trim();
        if (ext.Length == 0)
            return ".*";
        return ext[0] == '.' ? ext : "." + ext;
    }
}