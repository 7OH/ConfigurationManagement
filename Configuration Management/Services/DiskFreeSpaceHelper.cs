using System.Globalization;
using System.IO;

namespace Configuration_Management.Services;

/// <summary>
/// Снимок объёма диска: общий объём и свободное место в байтах.
/// </summary>
public sealed record DiskFreeInfo(long TotalBytes, long FreeBytes);

/// <summary>
/// Помощник «Свободное место на дисках» для Центра обслуживания (0.3.9.96):
/// выбор диска по пути файловой базы, форматирование значения «23,4 ГБ (12%)»
/// и правило предупреждения «свободно меньше X ГБ». Чистый .NET без
/// платформенных зависимостей (обе платформы); для unit-тестов вместо реальных
/// дисков принимается колбэк-резолвер <see cref="Func{T,TResult}"/>.
/// </summary>
public static class DiskFreeSpaceHelper
{
    /// <summary>Порог предупреждения по умолчанию, ГБ (настройка MaintenanceFreeSpaceWarningGb).</summary>
    public const int DefaultWarningGb = 10;

    private const long Gb = 1024L * 1024 * 1024;

    /// <summary>
    /// Резолвер по умолчанию: сопоставляет имя диска с реальным
    /// <see cref="DriveInfo"/> (System.IO.DriveInfo.GetDrives()). Недоступные
    /// диски и исключения — тихая деградация в null (ячейка «—»).
    /// </summary>
    public static readonly Func<string, DiskFreeInfo?> DefaultDriveResolver = ResolveDefault;

    /// <summary>
    /// Возвращает имя диска / точку монтирования для пути файловой базы
    /// (корень пути через <see cref="Path.GetPathRoot(string)"/>).
    /// Пустой путь или ошибка — null.
    /// </summary>
    public static string? ResolveDriveName(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;
        try
        {
            var root = Path.GetPathRoot(path);
            return string.IsNullOrEmpty(root) ? null : root;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Свободное место для пути: корень пути передаётся резолверу.
    /// Любое исключение резолвера — тихая деградация в null.
    /// </summary>
    public static DiskFreeInfo? TryGetInfo(string path, Func<string, DiskFreeInfo?> resolver)
    {
        var driveName = ResolveDriveName(path);
        if (driveName is null || resolver is null)
            return null;
        try
        {
            return resolver(driveName);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Текст ячейки «Свободно на диске»: «C:\ — 23,4 ГБ (12%)» /
    /// «/home — 145,2 ГБ (38%)». Некорректные данные — «—».
    /// </summary>
    public static string FormatDisplay(string driveName, DiskFreeInfo info)
    {
        if (string.IsNullOrWhiteSpace(driveName) || info is null || info.TotalBytes <= 0)
            return "—";
        var percent = (int)Math.Round((double)info.FreeBytes / info.TotalBytes * 100);
        return $"{driveName} — {FormatBytes(info.FreeBytes)} ({percent}%)";
    }

    /// <summary>
    /// Форматирует размер в байтах: «23,4 ГБ», «512 МБ», «1,2 ТБ».
    /// Десятичный разделитель — запятая, не зависит от локали ОС.
    /// Отрицательное значение — «—».
    /// </summary>
    public static string FormatBytes(long bytes)
    {
        if (bytes < 0)
            return "—";
        string unit;
        double value;
        if (bytes >= 1024L * Gb)
        {
            value = bytes / (double)(1024L * Gb);
            unit = "ТБ";
        }
        else if (bytes >= Gb)
        {
            value = bytes / (double)Gb;
            unit = "ГБ";
        }
        else if (bytes >= 1024 * 1024)
        {
            value = bytes / (double)(1024 * 1024);
            unit = "МБ";
        }
        else if (bytes >= 1024)
        {
            value = bytes / 1024.0;
            unit = "КБ";
        }
        else
        {
            value = bytes;
            unit = "Б";
        }

        // Крупные единицы (ГБ/ТБ) — один знак после запятой, мелкие — целые.
        var text = unit is "ГБ" or "ТБ"
            ? value.ToString("0.#", CultureInfo.InvariantCulture)
            : value.ToString("0", CultureInfo.InvariantCulture);
        return $"{text.Replace('.', ',')} {unit}";
    }

    /// <summary>
    /// Правило предупреждения: свободно меньше <paramref name="warningGb"/> ГБ.
    /// Порог 0 — предупреждение отключено; точное равенство порогу проблемой
    /// не считается (строго меньше).
    /// </summary>
    public static bool IsWarning(long? freeBytes, int warningGb)
    {
        if (warningGb <= 0 || !freeBytes.HasValue || freeBytes.Value < 0)
            return false;
        return freeBytes.Value < (long)warningGb * Gb;
    }

    /// <summary>
    /// Реальная реализация резолвера: перебор <see cref="DriveInfo.GetDrives()"/>.
    /// Сначала точное совпадение имени (Windows: «C:\»; Linux: «/»), затем —
    /// самая длинная точка монтирования, являющаяся префиксом пути (Linux:
    /// «/home» для пути «/home/user/base»).
    /// </summary>
    private static DiskFreeInfo? ResolveDefault(string driveName)
    {
        try
        {
            DriveInfo? bestPrefix = null;
            foreach (var drive in DriveInfo.GetDrives())
            {
                if (!drive.IsReady)
                    continue;
                if (string.Equals(drive.Name, driveName, StringComparison.OrdinalIgnoreCase))
                    return new DiskFreeInfo(drive.TotalSize, drive.AvailableFreeSpace);
                if (bestPrefix is null
                    && driveName.StartsWith(drive.Name, StringComparison.OrdinalIgnoreCase))
                    bestPrefix = drive;
            }
            return bestPrefix is null
                ? null
                : new DiskFreeInfo(bestPrefix.TotalSize, bestPrefix.AvailableFreeSpace);
        }
        catch
        {
            return null;
        }
    }
}