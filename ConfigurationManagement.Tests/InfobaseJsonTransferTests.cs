using Configuration_Management.Models;
using Configuration_Management.Services;
using Xunit;

namespace ConfigurationManagement.Tests;

/// <summary>
/// Тесты чистого JSON-переноса списка баз (0.3.9.122): полный round-trip
/// сериализации/десериализации снимка, ключ дубликата по строке подключения,
/// планирование «добавляющего» импорта и слияние групп с сохранением иерархии.
/// </summary>
public sealed class InfobaseJsonTransferTests
{
    private static Infobase MakeFileBase(string name, string path, string id = "")
    {
        return new Infobase
        {
            Id = id,
            Name = name,
            Group = "Группа 1",
            SortOrder = 10,
            Connection = new ConnectionSettings { Type = ConnectionType.File, FilePath = path }
        };
    }

    private static Infobase MakeClientServerBase(string name, string server, string database, string id = "")
    {
        return new Infobase
        {
            Id = id,
            Name = name,
            Connection = new ConnectionSettings
            {
                Type = ConnectionType.ClientServer,
                Server = server,
                DatabaseName = database
            }
        };
    }

    [Fact]
    public void RoundTrip_SerializeDeserialize_PreservesAllFields()
    {
        var infobase = new Infobase
        {
            Id = "b1",
            Name = "Бухгалтерия",
            Group = "Отдел / Финансы",
            SortOrder = 25,
            IsFavorite = true,
            FavoriteHotkeyNumber = 3,
            IsPinned = true,
            IsPrivate = true,
            Description = "Рабочая база",
            Tags = { "бухгалтерия", "prod" },
            PlatformVersion = "8.3.27.1644",
            Architecture = "64",
            ConfigurationName = "Бухгалтерия предприятия",
            ConfigurationVersion = "3.0.142.32",
            LaunchMode = "Тонкий клиент",
            LaunchParameters = "/DisableStartupMessages",
            ExternalProcessingPath = @"C:\epf\report.epf",
            ExternalProcessingData = "data",
            PreLaunchCommand = "echo pre",
            PostLaunchCommand = "echo post",
            DefaultLaunchMode = "Enterprise",
            DoubleClickAction = "Enterprise",
            UpdateConfigCode = "БП",
            ClientType = "Тонкий",
            ManualSizeBytes = 12345,
            LastBackupUtc = new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc),
            Connection = new ConnectionSettings
            {
                Type = ConnectionType.File,
                FilePath = @"D:\bases\Бухгалтерия"
            },
            Repository = new RepositorySettings
            {
                Server = "repo-server",
                RepositoryName = "Buh",
                User = "dev",
                Password = "secret"
            },
            EnterpriseAuth = new InfobaseAuthSettings { User = "enterprise_user", Password = "p1" },
            ConfiguratorAuth = new InfobaseAuthSettings { User = "configurator_user", Password = "p2" },
            ConfiguratorUseEnterpriseAuth = true
        };

        var groupParent = new Group { Id = "g1", Name = "Отдел", Description = "Родитель" };
        var groupChild = new Group
        {
            Id = "g2",
            Name = "Финансы",
            ParentId = "g1",
            Color = "#123456",
            IconColor = "#FFFFFF",
            Icon = "IconDatabase"
        };

        var snapshot = InfobaseJsonTransfer.BuildSnapshot(new[] { infobase }, new[] { groupParent, groupChild });
        var json = InfobaseJsonTransfer.Serialize(snapshot);
        var restored = InfobaseJsonTransfer.Deserialize(json);

        Assert.NotNull(restored);
        Assert.Equal(InfobaseListSnapshot.CurrentVersion, restored!.Version);
        Assert.Single(restored.Infobases);
        Assert.Equal(2, restored.Groups.Count);

        var ib = restored.Infobases[0];
        Assert.Equal("b1", ib.Id);
        Assert.Equal("Бухгалтерия", ib.Name);
        Assert.Equal("Отдел / Финансы", ib.Group);
        Assert.Equal(25, ib.SortOrder);
        Assert.True(ib.IsFavorite);
        Assert.Equal(3, ib.FavoriteHotkeyNumber);
        Assert.True(ib.IsPinned);
        Assert.True(ib.IsPrivate);
        Assert.Equal("Рабочая база", ib.Description);
        Assert.Equal(new[] { "бухгалтерия", "prod" }, ib.Tags);
        Assert.Equal("8.3.27.1644", ib.PlatformVersion);
        Assert.Equal("64", ib.Architecture);
        Assert.Equal("Бухгалтерия предприятия", ib.ConfigurationName);
        Assert.Equal("3.0.142.32", ib.ConfigurationVersion);
        Assert.Equal("Тонкий клиент", ib.LaunchMode);
        Assert.Equal("/DisableStartupMessages", ib.LaunchParameters);
        Assert.Equal(@"C:\epf\report.epf", ib.ExternalProcessingPath);
        Assert.Equal("data", ib.ExternalProcessingData);
        Assert.Equal("echo pre", ib.PreLaunchCommand);
        Assert.Equal("echo post", ib.PostLaunchCommand);
        Assert.Equal("Enterprise", ib.DefaultLaunchMode);
        Assert.Equal("БП", ib.UpdateConfigCode);
        Assert.Equal(12345, ib.ManualSizeBytes);
        Assert.Equal(new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc), ib.LastBackupUtc);
        Assert.Equal(ConnectionType.File, ib.Connection.Type);
        Assert.Equal(@"D:\bases\Бухгалтерия", ib.Connection.FilePath);
        Assert.Equal("repo-server", ib.Repository.Server);
        Assert.Equal("Buh", ib.Repository.RepositoryName);
        Assert.Equal("dev", ib.Repository.User);
        Assert.Equal("secret", ib.Repository.Password);
        Assert.Equal("enterprise_user", ib.EnterpriseAuth?.User);
        Assert.Equal("p1", ib.EnterpriseAuth?.Password);
        Assert.Equal("configurator_user", ib.ConfiguratorAuth?.User);
        Assert.True(ib.ConfiguratorUseEnterpriseAuth);

        var g = restored.Groups.Single(x => x.Id == "g2");
        Assert.Equal("Финансы", g.Name);
        Assert.Equal("g1", g.ParentId);
        Assert.Equal("#123456", g.Color);
        Assert.Equal("IconDatabase", g.Icon);
    }

    [Fact]
    public void Serialize_JsonContainsKeyStateFields()
    {
        var infobase = new Infobase
        {
            Name = "База",
            IsFavorite = true,
            FavoriteHotkeyNumber = 5,
            IsPinned = true,
            IsPrivate = true,
            PreLaunchCommand = "pre",
            Connection = new ConnectionSettings { Type = ConnectionType.WebServer, WebUrl = "http://srv/base" }
        };

        var json = InfobaseJsonTransfer.Serialize(InfobaseJsonTransfer.BuildSnapshot(new[] { infobase }, Array.Empty<Group>()));

        Assert.Contains("\"IsFavorite\": true", json);
        Assert.Contains("\"FavoriteHotkeyNumber\": 5", json);
        Assert.Contains("\"IsPinned\": true", json);
        Assert.Contains("\"IsPrivate\": true", json);
        Assert.Contains("\"PreLaunchCommand\": \"pre\"", json);
        // URL веб-публикации попадает в сериализованную строку подключения (кавычки
        // в JSON экранируются, поэтому проверяем сам URL).
        Assert.Contains("http://srv/base", json);
    }

    [Fact]
    public void ConnectionKey_NormalizesByConnectionString_IgnoringCase()
    {
        var fileA = MakeFileBase("A", @"C:\Bases\Demo");
        var fileB = MakeFileBase("B", @"c:\bases\demo");
        Assert.Equal(InfobaseJsonTransfer.ConnectionKey(fileA), InfobaseJsonTransfer.ConnectionKey(fileB));

        var csA = MakeClientServerBase("A", "SRV", "Base1");
        var csB = MakeClientServerBase("B", "srv", "base1");
        Assert.Equal(InfobaseJsonTransfer.ConnectionKey(csA), InfobaseJsonTransfer.ConnectionKey(csB));

        Assert.NotEqual(InfobaseJsonTransfer.ConnectionKey(fileA), InfobaseJsonTransfer.ConnectionKey(csA));
    }

    [Fact]
    public void PlanImport_SkipsDuplicatesByConnection_AddsNewBases()
    {
        var existing = new List<Infobase> { MakeFileBase("Существующая", @"C:\bases\demo", "id-existing") };
        var snapshot = new InfobaseListSnapshot
        {
            Infobases =
            {
                MakeFileBase("Дубликат пути", @"c:\bases\demo", "id-dup"),       // тот же путь → пропуск
                MakeFileBase("Новая база", @"D:\bases\new", "id-new"),          // новый путь → добавление
                MakeClientServerBase("Серверная новая", "srv1", "db1", "id-cs") // другой тип → добавление
            }
        };

        var plan = InfobaseJsonTransfer.PlanImport(existing, Array.Empty<Group>(), Array.Empty<string>(), snapshot);

        Assert.Equal(2, plan.BasesToAdd.Count);
        Assert.Equal(1, plan.DuplicatesSkipped);
        Assert.Contains(plan.BasesToAdd, b => b.Id == "id-new");
        Assert.Contains(plan.BasesToAdd, b => b.Id == "id-cs");
    }

    [Fact]
    public void PlanImport_AddsMissingGroupsAndNewTags()
    {
        var existing = new List<Infobase> { MakeFileBase("Старая", @"C:\old") };
        var existingGroups = new List<Group> { new() { Id = "g1", Name = "Есть" } };

        var snapshot = new InfobaseListSnapshot
        {
            Infobases =
            {
                new Infobase
                {
                    Name = "Новая",
                    Group = "НоваяГруппа",
                    Tags = { "общий", "новый-тег" },
                    Connection = new ConnectionSettings { Type = ConnectionType.File, FilePath = @"D:\new" }
                },
                new Infobase
                {
                    Name = "Ещё",
                    Group = "НоваяГруппа",
                    Tags = { "общий", "ещё-тег" },
                    Connection = new ConnectionSettings { Type = ConnectionType.File, FilePath = @"D:\new2" }
                }
            },
            Groups = { new() { Id = "g2", Name = "НоваяГруппа" } }
        };

        var existingTags = new[] { "общий" };
        var plan = InfobaseJsonTransfer.PlanImport(existing, existingGroups, existingTags, snapshot);

        Assert.Equal(2, plan.BasesToAdd.Count);
        Assert.Single(plan.GroupsToAdd);
        Assert.Equal("НоваяГруппа", plan.GroupsToAdd[0].Name);
        // «общий» уже есть — не считается новым; остальные два — новые.
        Assert.Equal(2, plan.TagsToAdd.Count);
        Assert.Contains("новый-тег", plan.TagsToAdd);
        Assert.Contains("ещё-тег", plan.TagsToAdd);
    }

    [Fact]
    public void PlanImport_AllDuplicates_ReturnsNothingNew()
    {
        var existing = new List<Infobase> { MakeFileBase("Одна", @"C:\bases\one") };
        var snapshot = new InfobaseListSnapshot
        {
            Infobases = { MakeFileBase("Та же", @"C:\BASES\ONE") }
        };

        var plan = InfobaseJsonTransfer.PlanImport(existing, Array.Empty<Group>(), Array.Empty<string>(), snapshot);

        Assert.Empty(plan.BasesToAdd);
        Assert.Equal(1, plan.DuplicatesSkipped);
    }

    [Fact]
    public void MergeNewGroups_RenamesConflictingId_RemapsParentId()
    {
        var existing = new List<Group> { new() { Id = "g1", Name = "Занято" } };
        var incoming = new[]
        {
            new Group { Id = "g1", Name = "НоваяРодительская" },   // Id конфликтует → новый Guid
            new Group { Id = "g2", Name = "Дочерняя", ParentId = "g1" }
        };

        var merged = InfobaseJsonTransfer.MergeNewGroups(existing, incoming);

        Assert.Equal(2, merged.Count);
        var parent = merged.Single(g => g.Name == "НоваяРодительская");
        var child = merged.Single(g => g.Name == "Дочерняя");

        Assert.NotEqual("g1", parent.Id);
        Assert.True(parent.Id.Length > 0);
        // ParentId дочерней переписан на фактический Id родительской.
        Assert.Equal(parent.Id, child.ParentId);
    }

    [Fact]
    public void MergeNewGroups_KeepsFreeIdsAndHierarchy()
    {
        var incoming = new[]
        {
            new Group { Id = "g10", Name = "Родитель" },
            new Group { Id = "g11", Name = "Ребёнок", ParentId = "g10" }
        };

        var merged = InfobaseJsonTransfer.MergeNewGroups(Array.Empty<Group>(), incoming);

        Assert.Equal("g10", merged[0].Id);
        Assert.Equal("g11", merged[1].Id);
        Assert.Equal("g10", merged[1].ParentId);
    }

    [Fact]
    public void Deserialize_LegacyPlainList_Works()
    {
        const string json = """
            [
              { "Name": "База из старого файла", "Group": "Группа",
                "Connection": { "Type": 0, "FilePath": "C:\\old" } }
            ]
            """;

        var snapshot = InfobaseJsonTransfer.Deserialize(json);

        Assert.NotNull(snapshot);
        Assert.Single(snapshot!.Infobases);
        Assert.Equal("База из старого файла", snapshot.Infobases[0].Name);
        Assert.Empty(snapshot.Groups);
    }
}