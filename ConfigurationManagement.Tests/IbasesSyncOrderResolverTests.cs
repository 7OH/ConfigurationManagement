using System;
using Configuration_Management.Services;
using Xunit;

namespace ConfigurationManagement.Tests;

/// <summary>
/// Тесты выбора порядка двусторонней синхронизации с ibases.v8i (issue #278):
/// загрузка первой выполняется, когда файл менялся внешне после последней выгрузки
/// приложения или метка выгрузки ещё не задана.
/// </summary>
public sealed class IbasesSyncOrderResolverTests
{
    private static readonly DateTime Stamp = new(2026, 9, 23, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void ShouldImportFirst_FileNewerThanStamp_ReturnsTrue()
    {
        // Файл менялся после последней выгрузки приложения (например, ручное
        // восстановление ibases.v8i) — сначала загрузка из файла.
        Assert.True(IbasesSyncOrderResolver.ShouldImportFirst(Stamp.AddMinutes(5), Stamp));
    }

    [Fact]
    public void ShouldImportFirst_FileOlderThanStamp_ReturnsFalse()
    {
        // Файл не менялся после нашей последней выгрузки — прежний порядок:
        // выгрузка, затем загрузка.
        Assert.False(IbasesSyncOrderResolver.ShouldImportFirst(Stamp.AddMinutes(-5), Stamp));
    }

    [Fact]
    public void ShouldImportFirst_StampDefault_ReturnsTrue()
    {
        // Метка не задана (первый запуск или обновление со старой версии) —
        // безопасный приоритет загрузки, чтобы не затереть файл, который мы не писали.
        Assert.True(IbasesSyncOrderResolver.ShouldImportFirst(Stamp, default));
    }

    [Fact]
    public void ShouldImportFirst_FileEqualStamp_ReturnsFalse()
    {
        // Время файла совпадает с меткой (файл писало само приложение) —
        // экспорт первым.
        Assert.False(IbasesSyncOrderResolver.ShouldImportFirst(Stamp, Stamp));
    }

    [Fact]
    public void ShouldImportFirst_MinValueFileAndNoStamp_ReturnsTrue()
    {
        // Файла ещё нет и выгрузок не было — загрузка первой (безопасно).
        Assert.True(IbasesSyncOrderResolver.ShouldImportFirst(DateTime.MinValue, DateTime.MinValue));
    }
}