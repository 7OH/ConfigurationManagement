using System.Text;
using Configuration_Management.Models;
using Configuration_Management.Services;
using Xunit;

namespace ConfigurationManagement.Tests;

/// <summary>
/// Тесты импорта из ibases.v8i с учётом списка удалённых пользователем пустых групп
/// (issue #327): такие группы не пересоздаются, пока под ними нет баз из файла;
/// при появлении баз группа нужна снова; непустые и обычные группы импортируются как раньше.
/// </summary>
public sealed class IbasesV8iImporterTombstoneTests
{
    [Fact]
    public void Import_TombstonedEmptyGroup_NotRecreated()
    {
        // В файле есть только секция-группа без баз и одна база в корне.
        // Путь «Old» помечен как удалённый пользователем — группа не возвращается.
        var filePath = WriteIbases("""
            [Old]
            ID=old-guid
            Folder=/

            [Base]
            ID=base-guid
            Folder=/
            Connect=File="C:\base";

            """);

        var groups = new List<Group>();
        var infobases = new List<Infobase>();

        var result = IbasesV8iImporter.Import(filePath, infobases, groups,
            new[] { "Old" });

        Assert.Equal(0, result.GroupsCreated);
        Assert.DoesNotContain(groups, g => g.Name == "Old");
        Assert.Single(infobases);
    }

    [Fact]
    public void Import_TombstonedPathWithNewBases_GroupCreated()
    {
        // Под удалённым путём в файле появилась база — группа нужна, она создаётся.
        var filePath = WriteIbases("""
            [Base]
            ID=base-guid
            Folder=/Old
            Connect=File="C:\db";

            """);

        var groups = new List<Group>();
        var infobases = new List<Infobase>();

        var result = IbasesV8iImporter.Import(filePath, infobases, groups,
            new[] { "Old" });

        Assert.Equal(1, result.GroupsCreated);
        Assert.Single(groups, g => g.Name == "Old");
        Assert.Equal("Old", Assert.Single(infobases).Group);
    }

    [Fact]
    public void Import_TombstonedPathExistingNonEmptyGroup_Stays()
    {
        // Непустая группа с тем же путём уже есть в коллекции: tombstone её не трогает,
        // импорт не создаёт дубликат и не теряет связь базы.
        var filePath = WriteIbases("""
            [Old]
            ID=old-guid
            Folder=/

            [Base]
            ID=base-guid
            Folder=/Old
            Connect=File="C:\db";

            """);

        var groups = new List<Group>
        {
            new() { Id = "g-old", Name = "Old" }
        };
        var infobases = new List<Infobase>
        {
            new()
            {
                Id = "base-guid",
                Name = "Base",
                Group = "Old",
                Connection = new ConnectionSettings { Type = ConnectionType.File, FilePath = @"C:\db" }
            }
        };

        var result = IbasesV8iImporter.Import(filePath, infobases, groups,
            new[] { "Old" });

        Assert.Equal(0, result.GroupsCreated);
        Assert.Single(groups);              // дубликат не появился
        Assert.Equal("Old", groups[0].Name);
        Assert.Equal("Old", Assert.Single(infobases).Group);
    }

    [Fact]
    public void Import_TombstonedParentBranch_SuppressesEmptyDescendants()
    {
        // Удалена вложенная группа «Parent / Old»; в файле есть только пустые секции
        // под ней и база вне группы. Родительская «Parent» создаётся, а «Old» и её
        // потомки — нет (иначе они «осиротели» бы).
        var filePath = WriteIbases("""
            [Parent]
            ID=parent-guid
            Folder=/

            [Old]
            ID=old-guid
            Folder=/Parent

            [Nested]
            ID=nested-guid
            Folder=/Parent/Old

            [Base]
            ID=base-guid
            Folder=/
            Connect=File="C:\base";

            """);

        var groups = new List<Group>();
        var infobases = new List<Infobase>();

        var result = IbasesV8iImporter.Import(filePath, infobases, groups,
            new[] { "Parent / Old" });

        Assert.Contains(groups, g => g.Name == "Parent");
        Assert.DoesNotContain(groups, g => g.Name == "Old");
        Assert.DoesNotContain(groups, g => g.Name == "Nested");
        Assert.Single(infobases);
    }

    [Fact]
    public void Import_WithoutTombstones_OrdinaryGroupsCreated()
    {
        // Регрессия: без списка удалённых групп поведение прежнее — группы создаются.
        var filePath = WriteIbases("""
            [New]
            ID=new-guid
            Folder=/

            [Base]
            ID=base-guid
            Folder=/New
            Connect=File="C:\db";

            """);

        var groups = new List<Group>();
        var infobases = new List<Infobase>();

        var result = IbasesV8iImporter.Import(filePath, infobases, groups);

        Assert.Equal(1, result.GroupsCreated);
        Assert.Single(groups, g => g.Name == "New");
        Assert.Single(infobases);
    }

    private static string WriteIbases(string content)
    {
        var filePath = Path.Combine(Path.GetTempPath(), $"ibases-tomb-{Guid.NewGuid():N}.v8i");
        File.WriteAllText(filePath, content, Encoding.Default);
        return filePath;
    }
}