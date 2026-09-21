using System.Text;
using Configuration_Management.Models;
using Configuration_Management.Services;
using Xunit;

namespace ConfigurationManagement.Tests;

public sealed class IbasesV8iExporterTests
{
    [Fact]
    public void ExportAndImport_UseSectionReferencesForNestedGroups()
    {
        var filePath = Path.Combine(Path.GetTempPath(), $"ibases-{Guid.NewGuid():N}.v8i");
        try
        {
            // Реальный сценарий issue #165: корректная дочерняя секция соседствует с
            // ошибочной секцией, имя которой содержит полный путь как буквальный текст.
            File.WriteAllText(filePath, """
                [Child]
                ID=old-child-id
                Folder=/Parent

                [Parent\Child]
                ID=starter-duplicate-id
                Folder=/

                [Parent]
                ID=parent-id
                Folder=/

                [Database]
                ID=database-id
                Folder=Parent\Child
                Connect=File="C:\database";
                """, Encoding.Default);

            var groups = new List<Group>
            {
                new() { Id = "parent-id", Name = "Parent" },
                new() { Id = "child-id", Name = "Child", ParentId = "parent-id" }
            };
            var infobases = new List<Infobase>
            {
                new()
                {
                    Id = "database-id",
                    Name = "Database",
                    Group = "Parent / Child",
                    Connection = new ConnectionSettings
                    {
                        Type = ConnectionType.File,
                        FilePath = @"C:\database"
                    }
                }
            };

            IbasesV8iExporter.Export(filePath, infobases, groups);
            var firstExport = File.ReadAllText(filePath, Encoding.Default);

            var headers = firstExport
                .Split('\n')
                .Select(line => line.Trim())
                .Where(line => line.StartsWith('[') && line.EndsWith(']'))
                .ToList();

            Assert.Equal(1, headers.Count(header => header == "[Child]"));
            Assert.DoesNotContain("[Parent\\Child]", headers);
            Assert.Contains("[Parent]", headers);

            var nestedSection = GetSection(firstExport, "Child");
            Assert.Contains("ID=child-id", nestedSection);
            Assert.Contains("Folder=/Parent", nestedSection);
            Assert.DoesNotContain("Connect=", nestedSection);

            var rootSection = GetSection(firstExport, "Parent");
            Assert.Contains("Folder=/", rootSection);

            var databaseSection = GetSection(firstExport, "Database");
            Assert.Contains("Folder=/Parent/Child", databaseSection);

            // Повторный экспорт не должен менять файл или возвращать альтернативную форму.
            IbasesV8iExporter.Export(filePath, infobases, groups);
            Assert.Equal(firstExport, File.ReadAllText(filePath, Encoding.Default));

            // Обратный импорт обязан восстановить внутренний полный путь и ParentId.
            var importedBases = new List<Infobase>();
            var importedGroups = new List<Group>();
            IbasesV8iImporter.Import(filePath, importedBases, importedGroups);

            var importedParent = Assert.Single(importedGroups, g => g.Name == "Parent");
            var importedChild = Assert.Single(importedGroups, g => g.Name == "Child");
            Assert.Equal(importedParent.Id, importedChild.ParentId);
            Assert.Equal("Parent / Child", Assert.Single(importedBases).Group);
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public void Export_RenamedBaseWithSameId_DoesNotCreateDuplicate()
    {
        // Сценарий issue #278: база переименована в приложении, а в файле (после
        // восстановления) лежит под старым именем с тем же ID. Экспорт не должен
        // создавать дубль — запись обновляется по ID на месте и переименовывается.
        var filePath = Path.Combine(Path.GetTempPath(), $"ibases-{Guid.NewGuid():N}.v8i");
        try
        {
            File.WriteAllText(filePath, """
                [OldBase]
                ID=dup-id
                Connect=File="C:\old";
                """, Encoding.Default);

            var groups = new List<Group>();
            var infobases = new List<Infobase>
            {
                new()
                {
                    Id = "dup-id",
                    Name = "NewBase",
                    Connection = new ConnectionSettings { Type = ConnectionType.File, FilePath = @"C:\new" }
                }
            };

            IbasesV8iExporter.Export(filePath, infobases, groups);
            var content = File.ReadAllText(filePath, Encoding.Default);

            var headers = content
                .Split('\n')
                .Select(line => line.Trim())
                .Where(line => line.StartsWith('[') && line.EndsWith(']'))
                .ToList();

            Assert.Single(headers, h => h == "[NewBase]");
            Assert.DoesNotContain("[OldBase]", headers);
            Assert.Contains("ID=dup-id", GetSection(content, "NewBase"));
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public void Import_RenamedBaseWithSameId_DoesNotCreateDuplicate()
    {
        // Сценарий issue #278 на стороне импорта: база в приложении переименована,
        // в файле — прежнее имя с тем же ID. Импорт должен обновить существующую базу
        // по ID и не добавлять дубль со старым именем.
        var filePath = Path.Combine(Path.GetTempPath(), $"ibases-{Guid.NewGuid():N}.v8i");
        try
        {
            File.WriteAllText(filePath, """
                [OldBase]
                ID=dup-id
                Connect=File="C:\old";
                """, Encoding.Default);

            var groups = new List<Group>();
            var infobases = new List<Infobase>
            {
                new()
                {
                    Id = "dup-id",
                    Name = "NewBase",
                    Connection = new ConnectionSettings { Type = ConnectionType.File, FilePath = @"C:\old" }
                }
            };

            IbasesV8iImporter.Import(filePath, infobases, groups);

            var db = Assert.Single(infobases);
            Assert.Equal("NewBase", db.Name);
            Assert.Equal("dup-id", db.Id);
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public void Export_RoundTrip_PreservesUnknownKeysAndOrder()
    {
        // Сценарий issue #277: экспорт не должен терять неизвестные ключи секции
        // (OrderInList/OrderInTree/External/WA/DisableLocalSpeechToText) и должен сохранять
        // их исходный порядок. Правка одной базы не должна удалять ключи соседних секций.
        var filePath = Path.Combine(Path.GetTempPath(), $"ibases-{Guid.NewGuid():N}.v8i");
        try
        {
            File.WriteAllText(filePath, """
                [Database]
                ID=db-id
                OrderInList=10
                Connect=File="C:\database";
                OrderInTree=3
                External=1
                WA=0
                DisableLocalSpeechToText=1

                [Untouched]
                ID=untouched-id
                Connect=File="C:\untouched";
                External=0
                CustomKey=CustomValue
                """, Encoding.Default);

            var groups = new List<Group>();
            var infobases = new List<Infobase>
            {
                new()
                {
                    Id = "db-id",
                    Name = "Database",
                    Connection = new ConnectionSettings { Type = ConnectionType.File, FilePath = @"C:\database" }
                },
                new()
                {
                    Id = "untouched-id",
                    Name = "Untouched",
                    Connection = new ConnectionSettings { Type = ConnectionType.File, FilePath = @"C:\untouched" }
                }
            };

            IbasesV8iExporter.Export(filePath, infobases, groups);
            var content = File.ReadAllText(filePath, Encoding.Default);

            var section = GetSection(content, "Database");
            Assert.Contains("OrderInList=10", section);
            Assert.Contains("OrderInTree=3", section);
            Assert.Contains("External=1", section);
            Assert.Contains("WA=0", section);
            Assert.Contains("DisableLocalSpeechToText=1", section);

            // Порядок неизвестных ключей должен быть сохранён.
            Assert.True(section.IndexOf("OrderInList=10", StringComparison.Ordinal) <
                        section.IndexOf("OrderInTree=3", StringComparison.Ordinal));
            Assert.True(section.IndexOf("OrderInTree=3", StringComparison.Ordinal) <
                        section.IndexOf("External=1", StringComparison.Ordinal));
            Assert.True(section.IndexOf("External=1", StringComparison.Ordinal) <
                        section.IndexOf("WA=0", StringComparison.Ordinal));
            Assert.True(section.IndexOf("WA=0", StringComparison.Ordinal) <
                        section.IndexOf("DisableLocalSpeechToText=1", StringComparison.Ordinal));

            // Неизвестные ключи чужой секции (не в списке приложения) тоже сохраняются.
            var untouched = GetSection(content, "Untouched");
            Assert.Contains("External=0", untouched);
            Assert.Contains("CustomKey=CustomValue", untouched);
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    private static string GetSection(string content, string name)
    {
        var marker = $"[{name}]";
        var start = content.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Section {marker} was not found.");

        var next = content.IndexOf("\n[", start + marker.Length, StringComparison.Ordinal);
        return next >= 0 ? content[start..next] : content[start..];
    }
}
