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
    public void Import_MatchById_UpdatesNameFromFile_WithoutDuplicates()
    {
        // Сценарий issue #278 на стороне импорта: база в приложении переименована,
        // в файле — прежнее имя с тем же ID. Импорт сопоставляет базу по ID 1С и
        // возвращает имя из файла («информация приезжает обратно»), не создавая дубль
        // и не теряя настройки подключения.
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
            Assert.Equal("OldBase", db.Name); // имя из файла вернулось в приложение
            Assert.Equal("dup-id", db.Id);
            Assert.Equal(@"C:\old", db.Connection.FilePath);
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public void Export_DeletionKeepsEntryWithMatchingId()
    {
        // Сценарий issue #278 на стороне удаления: секцию файла, чей ID 1С есть в
        // приложении, нельзя удалять только потому, что её имя не совпадает ни с одним
        // именем базы приложения. Запись, обновлённая по ID на шаге записи, переименована
        // (старое имя не остаётся дублем), а прочие секции с тем же ID сохраняются.
        var filePath = Path.Combine(Path.GetTempPath(), $"ibases-{Guid.NewGuid():N}.v8i");
        try
        {
            File.WriteAllText(filePath, """
                [OldBase]
                ID=same-id
                Connect=File="C:\old";

                [LegacySection]
                ID=same-id
                Connect=File="C:\legacy";
                """, Encoding.Default);

            var groups = new List<Group>();
            var infobases = new List<Infobase>
            {
                new()
                {
                    Id = "same-id",
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

            // Запись, обновлённая по ID, переименована — старого имени нет, дубля имён нет.
            Assert.Single(headers, h => h == "[NewBase]");
            Assert.DoesNotContain("[LegacySection]", headers);

            // Секция файла с ID из приложения (но другим именем) не удалена.
            Assert.Contains("[OldBase]", headers);
            Assert.Contains("ID=same-id", GetSection(content, "OldBase"));
            Assert.Equal(2, headers.Count);
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public void Sync_RenameRestore_ReturnsNameFromFile_NoDuplicates()
    {
        // Полный сценарий issue #278: база переименована в приложении («Б» → «Б 2»),
        // сохранена (экспорт), затем файл восстановлен вручную до старого состояния.
        // Повторная синхронизация (импорт) обязана вернуть имя из файла без дублей.
        var filePath = Path.Combine(Path.GetTempPath(), $"ibases-{Guid.NewGuid():N}.v8i");
        try
        {
            File.WriteAllText(filePath, """
                [Б]
                ID=base-id
                Connect=File="C:\base";
                """, Encoding.Default);

            var groups = new List<Group>();
            var infobases = new List<Infobase>
            {
                new()
                {
                    Id = "base-id",
                    Name = "Б 2",
                    Connection = new ConnectionSettings { Type = ConnectionType.File, FilePath = @"C:\base" }
                }
            };

            // 1. Сохранение (экспорт) после переименования — файл получает новое имя.
            IbasesV8iExporter.Export(filePath, infobases, groups);
            var exported = File.ReadAllText(filePath, Encoding.Default);
            Assert.Contains("[Б 2]", exported);
            Assert.DoesNotContain("[Б]", exported);
            Assert.Single(exported.Split('\n').Select(l => l.Trim()), l => l.StartsWith('[') && l.EndsWith(']'));

            // 2. Пользователь вручную восстанавливает файл до старого состояния.
            File.WriteAllText(filePath, """
                [Б]
                ID=base-id
                Connect=File="C:\base";
                """, Encoding.Default);

            // 3. Синхронизация (импорт) — имя в приложении возвращается к имени из файла.
            IbasesV8iImporter.Import(filePath, infobases, groups);

            var db = Assert.Single(infobases);
            Assert.Equal("Б", db.Name);
            Assert.Equal("base-id", db.Id);
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public void Sync_RepeatedExportImport_DoesNotCreateDuplicates()
    {
        // Повторная синхронизация (экспорт/импорт несколько раз подряд) не должна
        // плодить дубли ни в приложении, ни в файле ibases.v8i.
        var filePath = Path.Combine(Path.GetTempPath(), $"ibases-{Guid.NewGuid():N}.v8i");
        try
        {
            File.WriteAllText(filePath, """
                [Database]
                ID=db-id
                Connect=File="C:\database";
                """, Encoding.Default);

            var groups = new List<Group>();
            var infobases = new List<Infobase>
            {
                new()
                {
                    Id = "db-id",
                    Name = "Database",
                    Connection = new ConnectionSettings { Type = ConnectionType.File, FilePath = @"C:\database" }
                }
            };

            for (var i = 0; i < 3; i++)
            {
                IbasesV8iExporter.Export(filePath, infobases, groups);
                IbasesV8iImporter.Import(filePath, infobases, groups);
            }

            var db = Assert.Single(infobases);
            Assert.Equal("Database", db.Name);
            Assert.Equal("db-id", db.Id);

            var content = File.ReadAllText(filePath, Encoding.Default);
            var headers = content
                .Split('\n')
                .Select(line => line.Trim())
                .Where(line => line.StartsWith('[') && line.EndsWith(']'))
                .ToList();
            Assert.Single(headers, h => h == "[Database]");
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

    [Fact]
    public void Export_RoundTrip_PreservesKeyOrderIncludingKnownKeys()
    {
        // Сценарий issue #277: известные и неизвестные ключи перемешаны внутри секции.
        // Экспорт не должен пересобирать секцию в каноническом порядке — значения
        // обновляются на своих местах, а исходный порядок строк сохраняется. Отсутствующий
        // ключ (DefaultApp) дописывается в конец секции. Повторный экспорт идемпотентен.
        var filePath = Path.Combine(Path.GetTempPath(), $"ibases-{Guid.NewGuid():N}.v8i");
        try
        {
            File.WriteAllText(filePath, """
                [Database]
                OrderInList=10
                ID=db-id
                Connect=File="C:\database";
                Version=8.3.27.1688
                OrderInTree=3
                App=ThinClient
                WA=0
                """, Encoding.Default);

            var groups = new List<Group>();
            var infobases = new List<Infobase>
            {
                new()
                {
                    Id = "db-id",
                    Name = "Database",
                    Connection = new ConnectionSettings { Type = ConnectionType.File, FilePath = @"C:\database" },
                    PlatformVersion = "8.3.27.1688",
                    LaunchMode = "Тонкий клиент"
                }
            };

            IbasesV8iExporter.Export(filePath, infobases, groups);
            var content = File.ReadAllText(filePath, Encoding.Default);

            var lines = GetSectionLines(content, "Database");
            Assert.Equal(new[]
            {
                "[Database]",
                "OrderInList=10",
                "ID=db-id",
                "Connect=File=\"C:\\database\";",
                "Version=8.3.27.1688",
                "OrderInTree=3",
                "App=ThinClient",
                "WA=0",
                "DefaultApp=ThinClient"
            }, lines);

            // Повторный экспорт не должен менять файл.
            IbasesV8iExporter.Export(filePath, infobases, groups);
            Assert.Equal(content, File.ReadAllText(filePath, Encoding.Default));
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public void Export_UpdateChangesOnlyNeededLines()
    {
        // Сценарий issue #277: изменение одного поля (путь к файловой базе) должно изменить
        // только строку Connect НА ЕЁ МЕСТЕ — остальные строки секции (включая неизвестные
        // ключи) не переставляются и не теряются, порядок секций файла сохраняется.
        var filePath = Path.Combine(Path.GetTempPath(), $"ibases-{Guid.NewGuid():N}.v8i");
        try
        {
            File.WriteAllText(filePath, """
                [Base1]
                ID=id1
                OrderInList=1
                Connect=File="C:\old";
                Version=8.3.27.1688
                Custom=Value

                [Base2]
                ID=id2
                Connect=File="C:\two";
                """, Encoding.Default);

            var groups = new List<Group>();
            var infobases = new List<Infobase>
            {
                new()
                {
                    Id = "id1",
                    Name = "Base1",
                    Connection = new ConnectionSettings { Type = ConnectionType.File, FilePath = @"C:\new" },
                    PlatformVersion = "8.3.27.1688"
                },
                new()
                {
                    Id = "id2",
                    Name = "Base2",
                    Connection = new ConnectionSettings { Type = ConnectionType.File, FilePath = @"C:\two" }
                }
            };

            IbasesV8iExporter.Export(filePath, infobases, groups);
            var content = File.ReadAllText(filePath, Encoding.Default);

            // Порядок секций файла сохранён.
            Assert.True(content.IndexOf("[Base1]", StringComparison.Ordinal) <
                        content.IndexOf("[Base2]", StringComparison.Ordinal));

            // У Base1 изменилась только строка Connect (на своём месте), остальные строки
            // (в т.ч. пользовательский ключ Custom) сохранены; новые ключи дописаны в конец.
            var base1 = GetSectionLines(content, "Base1");
            Assert.Equal(new[]
            {
                "[Base1]",
                "ID=id1",
                "OrderInList=1",
                "Connect=File=\"C:\\new\";",
                "Version=8.3.27.1688",
                "Custom=Value",
                "App=Auto",
                "DefaultApp=Auto"
            }, base1);

            // Строка Connect обновлена между OrderInList и Version (не перенесена в конец).
            var section = GetSection(content, "Base1");
            Assert.True(section.IndexOf("OrderInList=1", StringComparison.Ordinal) <
                        section.IndexOf("Connect=File=\"C:\\new\";", StringComparison.Ordinal));
            Assert.True(section.IndexOf("Connect=File=\"C:\\new\";", StringComparison.Ordinal) <
                        section.IndexOf("Version=", StringComparison.Ordinal));

            // У Base2 значение Connect не изменилось.
            var base2 = GetSectionLines(content, "Base2");
            Assert.Contains("Connect=File=\"C:\\two\";", base2);
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public void Export_PreservesSectionOrderAndBlankLines()
    {
        // Сценарий issue #277: порядок секций файла и пустые строки (внутри секции и
        // между секциями) должны сохраняться при пересохранении.
        var filePath = Path.Combine(Path.GetTempPath(), $"ibases-{Guid.NewGuid():N}.v8i");
        try
        {
            File.WriteAllText(filePath, """
                [Alpha]
                ID=a1
                Connect=File="C:\alpha";

                Custom=KeepMe

                [Bravo]
                ID=b1
                Connect=File="C:\bravo";
                """, Encoding.Default);

            var groups = new List<Group>();
            var infobases = new List<Infobase>
            {
                new()
                {
                    Id = "a1",
                    Name = "Alpha",
                    Connection = new ConnectionSettings { Type = ConnectionType.File, FilePath = @"C:\alpha" }
                },
                new()
                {
                    Id = "b1",
                    Name = "Bravo",
                    Connection = new ConnectionSettings { Type = ConnectionType.File, FilePath = @"C:\bravo" }
                }
            };

            IbasesV8iExporter.Export(filePath, infobases, groups);
            var content = File.ReadAllText(filePath, Encoding.Default);

            // Порядок секций файла сохранён.
            Assert.True(content.IndexOf("[Alpha]", StringComparison.Ordinal) <
                        content.IndexOf("[Bravo]", StringComparison.Ordinal));

            // Пустая строка внутри секции Alpha сохранилась между Connect и Custom.
            var alpha = GetSectionLines(content, "Alpha");
            Assert.Equal(new[]
            {
                "[Alpha]",
                "ID=a1",
                "Connect=File=\"C:\\alpha\";",
                "",
                "Custom=KeepMe",
                "App=Auto",
                "DefaultApp=Auto"
            }, alpha);

            // Между секциями — ровно одна пустая строка (разделитель).
            Assert.True(content.Contains(
                "DefaultApp=Auto" + Environment.NewLine + Environment.NewLine + "[Bravo]",
                StringComparison.Ordinal));
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

    /// <summary>Возвращает строки секции (заголовок включён) без переводов строк; пустые строки сохраняются.</summary>
    private static List<string> GetSectionLines(string content, string name)
    {
        var lines = GetSection(content, name)
            .Split('\n')
            .Select(line => line.TrimEnd('\r'))
            .ToList();
        while (lines.Count > 0 && lines[^1].Length == 0)
            lines.RemoveAt(lines.Count - 1);
        return lines;
    }
}
