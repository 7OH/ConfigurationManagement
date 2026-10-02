using System;
using System.IO;
using Configuration_Management.Models;
using Configuration_Management.Services;
using Xunit;

namespace ConfigurationManagement.Tests;

/// <summary>
/// Тесты модельной части отбора «Только запущенные» (issue #339): режим списка
/// <see cref="ListViewMode.Running"/>, настройки <see cref="AppSettings.ShowRunningOnly"/>
/// и <see cref="AppSettings.HotkeyShowRunning"/> переживают сохранение/загрузку профиля.
/// </summary>
public sealed class RunningFilterModelTests : IDisposable
{
    private readonly string _tempDir;

    public RunningFilterModelTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "cm_running_filter_tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, recursive: true);
        }
        catch
        {
            // Игнорируем: каталог мог быть занят или уже удалён.
        }
    }

    [Fact]
    public void ListViewMode_HasRunningMode_DistinctFromOthers()
    {
        Assert.True(Enum.IsDefined(typeof(ListViewMode), ListViewMode.Running));
        Assert.NotEqual(ListViewMode.All, ListViewMode.Running);
        Assert.NotEqual(ListViewMode.Favorites, ListViewMode.Running);
        Assert.NotEqual(ListViewMode.Recent, ListViewMode.Running);
    }

    [Fact]
    public void AppSettings_RunningDefaults_DisabledAndNoHotkey()
    {
        var settings = new AppSettings();

        Assert.False(settings.ShowRunningOnly);
        Assert.Equal(string.Empty, settings.HotkeyShowRunning);
    }

    [Fact]
    public void AppSettings_RunningFlags_SurviveSaveAndLoad()
    {
        var repo = new InfobaseRepository(directory: _tempDir);
        repo.SaveSettings(new AppSettings
        {
            ShowRunningOnly = true,
            HotkeyShowRunning = "Ctrl+Shift+R",
        });

        var loaded = repo.LoadSettings();

        Assert.True(loaded.ShowRunningOnly);
        Assert.Equal("Ctrl+Shift+R", loaded.HotkeyShowRunning);
    }
}