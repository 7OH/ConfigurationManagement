using Configuration_Management.Models;

namespace Configuration_Management.Services;

public interface IIbasesSyncService
{
    /// <param name="deletedEmptyGroupPaths">Полные пути пустых групп, удалённых пользователем
    /// вручную: такие группы не пересоздаются при импорте, пока под ними нет баз из файла
    /// (issue #327). Может быть null — поведение как раньше.</param>
    IbasesImportResult Import(string filePath, IList<Infobase> infobases, IList<Group> groups,
        IReadOnlyCollection<string>? deletedEmptyGroupPaths = null);

    /// <param name="removeMissingGroups">Удалять ли из файла пустые секции-группы, которых
    /// нет в группах приложения (полная двусторонняя синхронизация, issue #327).
    /// По умолчанию false — группы файла не трогаются.</param>
    IbasesExportResult Export(string filePath, IEnumerable<Infobase> infobases, IEnumerable<Group> groups,
        bool removeMissingGroups = false);
}

public sealed class IbasesSyncService : IIbasesSyncService
{
    public IbasesImportResult Import(string filePath, IList<Infobase> infobases, IList<Group> groups,
        IReadOnlyCollection<string>? deletedEmptyGroupPaths = null) =>
        IbasesV8iImporter.Import(filePath, infobases, groups, deletedEmptyGroupPaths);

    public IbasesExportResult Export(string filePath, IEnumerable<Infobase> infobases, IEnumerable<Group> groups,
        bool removeMissingGroups = false) =>
        IbasesV8iExporter.Export(filePath, infobases, groups, removeMissingGroups);
}
