namespace Configuration_Management.Models;

/// <summary>
/// Контейнер для экспорта/импорта данных приложения:
/// список информационных баз и список групп с цветами.
/// </summary>
public class InfobaseExportData
{
    /// <summary>
    /// Версия формата файла (1 — базы + группы). Файлы старых версий без этого
    /// поля импортируются как и раньше (в поле останется 0).
    /// </summary>
    public int Version { get; set; }

    /// <summary>Список информационных баз.</summary>
    public List<Infobase> Infobases { get; set; } = new();

    /// <summary>Список групп информационных баз (с цветами).</summary>
    public List<Group> Groups { get; set; } = new();
}