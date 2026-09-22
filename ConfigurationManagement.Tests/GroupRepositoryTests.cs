using System.Text;
using System.Text.Json;
using Configuration_Management.Models;
using Configuration_Management.Services;
using Xunit;

namespace ConfigurationManagement.Tests;

/// <summary>
/// Тесты корректности сохранения и загрузки групп (issue #280): защитная нормализация
/// при записи в <see cref="InfobaseRepository.SaveGroups"/> и восстановление повреждённого
/// файла групп в <see cref="InfobaseRepository.LoadGroups"/>.
/// </summary>
public sealed class GroupRepositoryTests
{
    private const string DuplicateId = "5af46c88-6e66-4b03-be7b-f5452546dfc9";
    private const string MissingParentId = "a348fc16-02d3-4e12-974a-4b6b5ff99e75";

    /// <summary>
    /// Сохранение списка с дубликатом Id не должно записывать дубль на диск:
    /// второй дубликат получает новый Guid, дети перенаправляются на него.
    /// Нормализация исправляет и переданный список (те же объекты), поэтому
    /// рабочая коллекция и дерево групп после пересборки согласованы.
    /// </summary>
    [Fact]
    public void SaveGroups_DuplicateIds_RepairsListAndWritesUniqueIds()
    {
        using var dir = new TempDir();
        var repo = new InfobaseRepository(directory: dir.Path);

        // Точная картина из issue #280: две записи с одинаковым Id, одна — с битым
        // ParentId (родитель отсутствует), вторая — корневая.
        var groups = new List<Group>
        {
            new() { Id = DuplicateId, Name = "ДО", ParentId = MissingParentId, Color = "#DFDD08" },
            new() { Id = DuplicateId, Name = "ДО", ParentId = string.Empty, Color = "#2D6CDF" },
            new() { Id = "child-1", Name = "Подгруппа", ParentId = DuplicateId }
        };

        repo.SaveGroups(groups);

        // На диске не остаётся дублей Id.
        var onDisk = ReadGroupsFile(dir);
        Assert.Equal(onDisk.Count, onDisk.Select(g => g.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(onDisk, g => Assert.False(string.IsNullOrWhiteSpace(g.Id)));

        // Первая запись сохраняет исходный Id, вторая получает новый Guid,
        // а дети дубля перенаправляются на выжившую (корневую) группу.
        var kept = Assert.Single(onDisk, g => string.Equals(g.Id, DuplicateId, StringComparison.OrdinalIgnoreCase));
        Assert.Equal(MissingParentId, kept.ParentId);
        var repaired = Assert.Single(onDisk, g => g.Name == "ДО" && !string.Equals(g.Id, DuplicateId, StringComparison.OrdinalIgnoreCase));
        Assert.Equal(string.Empty, repaired.ParentId);
        var child = Assert.Single(onDisk, g => g.Id == "child-1");
        Assert.Equal(repaired.Id, child.ParentId);

        // Исправления видны и в переданном списке (объекты общие).
        Assert.Equal(3, groups.Count);
        Assert.Equal(kept.Id, groups[0].Id);
        Assert.Equal(repaired.Id, groups[1].Id);
        Assert.Equal(repaired.Id, groups[2].ParentId);
    }

    /// <summary>
    /// Загрузка повреждённого файла (дубль Id + битый ParentId) чинит иерархию и
    /// пересохраняет файл: уникальные непустые Id, дети дубля привязаны к существующей группе.
    /// </summary>
    [Fact]
    public void LoadGroups_DuplicateIdAndBrokenParentId_RepairsHierarchyAndResaves()
    {
        using var dir = new TempDir();

        // Файл из issue #280: первый дубль с ошибочным ParentId, второй — корневой,
        // у «ДО» есть дочерняя группа.
        var corruptedJson = $$"""
            [
              { "Id": "{{DuplicateId}}", "Name": "ДО", "ParentId": "{{MissingParentId}}", "Color": "#DFDD08" },
              { "Id": "{{DuplicateId}}", "Name": "ДО", "ParentId": "", "Color": "#2D6CDF" },
              { "Id": "child-1", "Name": "Подгруппа", "ParentId": "{{DuplicateId}}" }
            ]
            """;
        File.WriteAllText(Path.Combine(dir.Path, "groups.json"), corruptedJson);

        var repo = new InfobaseRepository(directory: dir.Path);
        var groups = repo.LoadGroups();

        // Id уникальны и непусты.
        Assert.Equal(groups.Count, groups.Select(g => g.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(groups, g => Assert.False(string.IsNullOrWhiteSpace(g.Id)));

        // Дети дубля перенаправлены: их ParentId указывает на существующую группу.
        var child = Assert.Single(groups, g => g.Id == "child-1");
        Assert.Contains(groups, g => string.Equals(g.Id, child.ParentId, StringComparison.OrdinalIgnoreCase));

        // Повреждённый файл пересохранён без дублей Id.
        var onDisk = ReadGroupsFile(dir);
        Assert.Equal(onDisk.Count, onDisk.Select(g => g.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    /// <summary>
    /// Повторное сохранение уже нормализованного списка не меняет идентификаторы групп
    /// и не трогает файл — нормализация идемпотентна.
    /// </summary>
    [Fact]
    public void SaveGroups_AlreadyNormalized_KeepsIdentifiersAndFileUnchanged()
    {
        using var dir = new TempDir();
        var repo = new InfobaseRepository(directory: dir.Path);

        var groups = new List<Group>
        {
            new() { Id = "root-1", Name = "Корень" },
            new() { Id = "child-1", Name = "Ребёнок", ParentId = "root-1" }
        };

        repo.SaveGroups(groups);
        var firstWrite = File.ReadAllText(Path.Combine(dir.Path, "groups.json"));

        repo.SaveGroups(groups);

        Assert.Equal(firstWrite, File.ReadAllText(Path.Combine(dir.Path, "groups.json")));
        Assert.Equal("root-1", groups[0].Id);
        Assert.Equal("child-1", groups[1].Id);
        Assert.Equal("root-1", groups[1].ParentId);
    }

    /// <summary>
    /// Импорт из ibases.v8i не должен создавать вторую группу с Id, который уже занят
    /// группой коллекции под другим полным путём (источник дублей из issue #280):
    /// при коллизии назначается свежий Guid.
    /// </summary>
    [Fact]
    public void Import_GroupIdCollidesWithExistingGroup_AssignsFreshId()
    {
        var filePath = Path.Combine(Path.GetTempPath(), $"ibases-{Guid.NewGuid():N}.v8i");
        try
        {
            // В приложении уже есть корневая «ДО» (dup-id), а в файле эта же папка
            // лежит внутри «Other» с тем же ID. Поиск по полному пути корневую «ДО»
            // не находит, и без защиты импортёр создал бы второй объект с Id dup-id.
            File.WriteAllText(filePath, """
                [ДО]
                ID=dup-id
                Folder=/Other

                [Other]
                ID=other-id
                Folder=/

                [База]
                ID=base-1
                Folder=Other\ДО
                Connect=File="C:\base";
                """, Encoding.Default);

            var groups = new List<Group> { new() { Id = "dup-id", Name = "ДО" } };
            var infobases = new List<Infobase>();

            IbasesV8iImporter.Import(filePath, infobases, groups);

            var ids = groups.Select(g => g.Id).ToList();
            Assert.Equal(ids.Count, ids.Distinct(StringComparer.OrdinalIgnoreCase).Count());
            Assert.Single(groups, g => string.Equals(g.Id, "dup-id", StringComparison.OrdinalIgnoreCase));

            // Вложенная «ДО» из файла получила свежий Id и корректного родителя.
            var nested = Assert.Single(groups, g => g.Name == "ДО" && !string.Equals(g.Id, "dup-id", StringComparison.OrdinalIgnoreCase));
            var other = Assert.Single(groups, g => g.Name == "Other");
            Assert.Equal(other.Id, nested.ParentId);
            Assert.Equal("Other / ДО", Assert.Single(infobases).Group);
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    private static List<Group> ReadGroupsFile(TempDir dir)
    {
        var json = File.ReadAllText(Path.Combine(dir.Path, "groups.json"));
        return JsonSerializer.Deserialize<List<Group>>(json) ?? new List<Group>();
    }

    /// <summary>Временный каталог данных репозитория, удаляемый вместе с содержимым.</summary>
    private sealed class TempDir : IDisposable
    {
        public TempDir()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"cm-groups-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Path))
                    Directory.Delete(Path, recursive: true);
            }
            catch
            {
                // Уборка временных файлов не должна ронять тест.
            }
        }
    }
}