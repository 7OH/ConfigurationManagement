namespace Configuration_Management.Models;

/// <summary>
/// Файл дистрибутива конкретной версии технологической платформы 1С из каталога
/// <c>releases.1c.ru</c>. Заполняется парсером ответа <c>version_files?nick=…&ver=…</c>.
/// </summary>
public class PlatformReleaseFile
{
    /// <summary>Имя файла, например «8.3.27.2214_x64.zip».</summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>Прямая ссылка на файл (с учётом возможной query-части).</summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>Размер файла в байтах (0 — неизвестен).</summary>
    public long SizeBytes { get; set; }

    /// <summary>Разрядность дистрибутива: «x64»/«x86» или null, если не определена.</summary>
    public string? Architecture { get; set; }

    /// <summary>Тип дистрибутива по расширению файла.</summary>
    public PlatformDistributionKind Kind { get; set; }
}