using System;
using Configuration_Management.Models;
using Xunit;

namespace ConfigurationManagement.Tests;

/// <summary>
/// Тесты Этапа 13 дорожной карты StartManager (функции №3, №15): колонка
/// «Дата изменений» файла ИБ и сохранение состояния списка (раскрытые группы).
/// Здесь покрывается отображаемая логика колонки «Дата изменений»
/// (<see cref="Infobase.LastModifiedDisplay"/>), а настройки автосохранения
/// состояния списка — моделью <see cref="AppSettings"/>.
/// </summary>
public sealed class Etap13ListStateTests
{
    private static Infobase CreateFileInfobase() => new()
    {
        Connection = new ConnectionSettings
        {
            Type = ConnectionType.File,
            FilePath = @"C:\bases\demo"
        }
    };

    [Fact]
    public void LastModifiedDisplay_NonFileBase_ReturnsDash()
    {
        var ib = new Infobase
        {
            Connection = new ConnectionSettings { Type = ConnectionType.ClientServer }
        };

        Assert.Equal("—", ib.LastModifiedDisplay);
    }

    [Fact]
    public void LastModifiedDisplay_NotResolved_ReturnsEllipsis()
    {
        var ib = CreateFileInfobase();

        Assert.Equal("…", ib.LastModifiedDisplay);
    }

    [Fact]
    public void LastModifiedDisplay_ResolvedLocalTime_ReturnsFormattedDate()
    {
        var ib = CreateFileInfobase();
        var utc = new DateTime(2026, 9, 17, 12, 30, 0, DateTimeKind.Utc);

        ib.FileLastWriteTimeUtc = utc;

        var expected = utc.ToLocalTime().ToString("dd.MM.yyyy HH:mm");
        Assert.Equal(expected, ib.LastModifiedDisplay);
    }

    [Fact]
    public void LastModifiedDisplay_SettingsDefaults_AutoSaveEnabledAtTenSeconds()
    {
        var settings = new AppSettings();

        // Автосохранение состояния списка включено по умолчанию с периодичностью 10 секунд.
        Assert.True(settings.AutoSaveListState);
        Assert.Equal(10, settings.ListStateAutoSaveIntervalSeconds);
    }
}