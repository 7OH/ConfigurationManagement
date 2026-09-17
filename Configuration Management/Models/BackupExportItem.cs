using System.IO;

namespace Configuration_Management.Models;

/// <summary>
/// Строка «Списка выгрузок»: один найденный файл резервной копии (.dt/.cf/.zip/.rar)
/// в каталогах назначения сценариев или в настраиваемых каталогах выгрузки.
/// </summary>
public class BackupExportItem
{
    /// <summary>Полный путь к файлу.</summary>
    public string FilePath { get; set; } = "";

    /// <summary>Имя файла без каталога.</summary>
    public string FileName => Path.GetFileName(FilePath);

    /// <summary>Каталог размещения файла.</summary>
    public string Directory => Path.GetDirectoryName(FilePath) ?? "";

    /// <summary>Формат файла (по расширению).</summary>
    public BackupFormat Format { get; set; }

    /// <summary>Размер файла в байтах.</summary>
    public long SizeBytes { get; set; }

    /// <summary>Дата и время последней записи файла.</summary>
    public DateTime LastWriteTime { get; set; }

    /// <summary>Имя сценария, создавшего файл (если известно).</summary>
    public string? SourceScenarioName { get; set; }

    /// <summary>Размер файла в человекочитаемом виде (Б/КБ/МБ/ГБ).</summary>
    public string SizeText => FormatSize(SizeBytes);

    /// <summary>Дата последней записи в локальном формате.</summary>
    public string LastWriteText => LastWriteTime.ToString("dd.MM.yyyy HH:mm");

    private static string FormatSize(long bytes)
    {
        string[] units = { "Б", "КБ", "МБ", "ГБ" };
        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return unit == 0 ? $"{bytes} {units[0]}" : $"{value:0.#} {units[unit]}";
    }
}