using Configuration_Management.Models;
using Configuration_Management.Services;
using Xunit;

namespace ConfigurationManagement.Tests;

/// <summary>
/// Тесты добавляющего импорта баз (<see cref="InfobaseExportMerge"/>):
/// дубликаты по Id и по подключению, порядок сохранения, сбор групп.
/// </summary>
public sealed class InfobaseExportMergeTests
{
    private static Infobase Base(string id, string name, string filePath = "") => new()
    {
        Id = id,
        Name = name,
        Connection = new ConnectionSettings { Type = ConnectionType.File, FilePath = filePath }
    };

    [Fact]
    public void SelectNew_AddsOnlyNew_ById()
    {
        var existing = new[] { Base("a1", "Бухгалтерия") };
        var incoming = new[]
        {
            Base("a1", "Бухгалтерия (копия из файла)"), // дубликат по Id
            Base("b2", "Зарплата")
        };

        var result = InfobaseExportMerge.SelectNew(existing, incoming);

        Assert.Single(result.Added);
        Assert.Equal("b2", result.Added[0].Id);
        Assert.Single(result.Skipped);
        Assert.Equal("a1", result.Skipped[0].Id);
    }

    [Fact]
    public void SelectNew_NoId_MatchesByNameAndConnection()
    {
        var existing = new[] { Base("", "Бухгалтерия", @"C:\Bases\acc") };
        var incoming = new[]
        {
            Base("", "Бухгалтерия", @"C:\Bases\ACC"), // тот же путь — дубликат
            Base("", "Бухгалтерия", @"C:\Bases\acc2") // другой путь — новая
        };

        var result = InfobaseExportMerge.SelectNew(existing, incoming);

        Assert.Single(result.Added);
        Assert.Single(result.Skipped);
    }

    [Fact]
    public void SelectNew_PreservesIncomingOrder()
    {
        var incoming = new[]
        {
            Base("c3", "Третья"),
            Base("a1", "Первая"),
            Base("b2", "Вторая")
        };

        var result = InfobaseExportMerge.SelectNew(Array.Empty<Infobase>(), incoming);

        Assert.Equal(new[] { "c3", "a1", "b2" }, result.Added.Select(b => b.Id));
    }

    [Fact]
    public void SelectNew_DuplicatesInsideIncoming_AlsoSkipped()
    {
        var incoming = new[]
        {
            Base("a1", "База"),
            Base("a1", "База (копия)") // дубликат внутри самого файла
        };

        var result = InfobaseExportMerge.SelectNew(Array.Empty<Infobase>(), incoming);

        Assert.Single(result.Added);
        Assert.Single(result.Skipped);
    }

    [Fact]
    public void CollectGroupNames_IgnoresEmptyAndDistinct()
    {
        var bases = new[]
        {
            Base("1", "A"),
            Base("2", "B"),
            Base("3", "C")
        };
        bases[0].Group = "Продакшен";
        bases[1].Group = "продакшен"; // регистр — та же группа
        bases[2].Group = "";

        var groups = InfobaseExportMerge.CollectGroupNames(bases);

        Assert.Single(groups);
        Assert.Equal("Продакшен", groups[0]);
    }
}
