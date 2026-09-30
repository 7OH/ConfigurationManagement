using System;
using System.IO;
using System.Linq;
using Configuration_Management.Models;
using Configuration_Management.Services.EventLog;
using Xunit;

namespace ConfigurationManagement.Tests;

/// <summary>
/// Тесты поиска каталога журнала регистрации (<see cref="LgdJournalLocator"/>):
/// определение формата по содержимому 1Cv8Log, пути файловой базы (файл/каталог),
/// сортировка фрагментов *.lgp и обработка ошибочных ситуаций.
/// </summary>
public sealed class LgdJournalLocatorTests
{
    [Fact]
    public void Resolve_Sqlite_NextTo1Cv81Cd()
    {
        using var tmp = TempBase.Create();
        File.WriteAllBytes(Path.Combine(tmp.BaseDir, "1Cv8.1CD"), Array.Empty<byte>());
        var logDir = Path.Combine(tmp.BaseDir, "1Cv8Log");
        Directory.CreateDirectory(logDir);
        File.WriteAllBytes(Path.Combine(logDir, "1Cv8.lgd"), Array.Empty<byte>());

        var ib = FileBase(Path.Combine(tmp.BaseDir, "1Cv8.1CD"));
        var location = LgdJournalLocator.Resolve(ib);

        Assert.True(location.IsValid);
        Assert.Equal(LgdFormat.Sqlite, location.Format);
        Assert.Equal(logDir, location.LogDir);
        Assert.EndsWith("1Cv8.lgd", location.MainFile);
        Assert.Single(location.DataFiles);
        Assert.Null(location.ErrorMessage);
    }

    [Fact]
    public void Resolve_FilePathIsDirectory_AlsoWorks()
    {
        using var tmp = TempBase.Create();
        var logDir = Path.Combine(tmp.BaseDir, "1Cv8Log");
        Directory.CreateDirectory(logDir);
        File.WriteAllBytes(Path.Combine(logDir, "1Cv8.lgd"), Array.Empty<byte>());

        var location = LgdJournalLocator.Resolve(FileBase(tmp.BaseDir));

        Assert.True(location.IsValid);
        Assert.Equal(LgdFormat.Sqlite, location.Format);
    }

    [Fact]
    public void Resolve_Sequential_SortsFragmentsByName()
    {
        using var tmp = TempBase.Create();
        var logDir = Path.Combine(tmp.BaseDir, "1Cv8Log");
        Directory.CreateDirectory(logDir);
        File.WriteAllBytes(Path.Combine(logDir, "1Cv8.lgf"), Array.Empty<byte>());
        File.WriteAllBytes(Path.Combine(logDir, "20260201000000.lgp"), Array.Empty<byte>());
        File.WriteAllBytes(Path.Combine(logDir, "20260101000000.lgp"), Array.Empty<byte>());

        var location = LgdJournalLocator.Resolve(FileBase(tmp.BaseDir));

        Assert.True(location.IsValid);
        Assert.Equal(LgdFormat.Sequential, location.Format);
        Assert.Equal(2, location.DataFiles.Count);
        Assert.EndsWith("20260101000000.lgp", location.DataFiles[0]);
        Assert.EndsWith("20260201000000.lgp", location.DataFiles[1]);
    }

    [Fact]
    public void Resolve_LgfWithoutFragments_NotValid()
    {
        using var tmp = TempBase.Create();
        var logDir = Path.Combine(tmp.BaseDir, "1Cv8Log");
        Directory.CreateDirectory(logDir);
        File.WriteAllBytes(Path.Combine(logDir, "1Cv8.lgf"), Array.Empty<byte>());

        var location = LgdJournalLocator.Resolve(FileBase(tmp.BaseDir));

        Assert.False(location.IsValid);
        Assert.Equal(LgdFormat.Unknown, location.Format);
        Assert.NotNull(location.ErrorMessage);
        Assert.Contains("lgp", location.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Resolve_NoLogDirectory_Error()
    {
        using var tmp = TempBase.Create();
        var location = LgdJournalLocator.Resolve(FileBase(tmp.BaseDir));

        Assert.False(location.IsValid);
        Assert.NotNull(location.ErrorMessage);
        Assert.Contains("не найден", location.ErrorMessage);
    }

    [Fact]
    public void Resolve_EmptyFilePath_Error()
    {
        var location = LgdJournalLocator.Resolve(FileBase(string.Empty));

        Assert.False(location.IsValid);
        Assert.NotNull(location.ErrorMessage);
    }

    [Fact]
    public void Resolve_ClientServerBase_ServerPathError()
    {
        var ib = new Infobase
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = "Серверная база",
            Connection = new ConnectionSettings
            {
                Type = ConnectionType.ClientServer,
                Server = "srv-1c",
                DatabaseName = "AccountingCorp"
            }
        };

        var location = LgdJournalLocator.Resolve(ib);

        Assert.False(location.IsValid);
        Assert.NotNull(location.ErrorMessage);
        Assert.Contains("сервере", location.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FromDirectory_NonExisting_Error()
    {
        var missing = Path.Combine(Path.GetTempPath(), $"missing-log-{Guid.NewGuid():N}");
        var location = LgdJournalLocator.FromDirectory(missing);

        Assert.False(location.IsValid);
        Assert.NotNull(location.ErrorMessage);
    }

    [Fact]
    public void FromDirectory_Null_Error()
    {
        var location = LgdJournalLocator.FromDirectory(null);
        Assert.False(location.IsValid);
        Assert.NotNull(location.ErrorMessage);
    }

    private static Infobase FileBase(string path) => new()
    {
        Id = Guid.NewGuid().ToString("N"),
        Name = "Файловая база",
        Connection = new ConnectionSettings
        {
            Type = ConnectionType.File,
            FilePath = path
        }
    };

    /// <summary>Temp-каталог «файловой базы», удаляется при Dispose.</summary>
    private sealed class TempBase : IDisposable
    {
        public string BaseDir { get; }

        private TempBase(string dir) => BaseDir = dir;

        public static TempBase Create()
        {
            var dir = Path.Combine(Path.GetTempPath(), $"lgd-loc-{Guid.NewGuid():N}");
            Directory.CreateDirectory(dir);
            return new TempBase(dir);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(BaseDir))
                    Directory.Delete(BaseDir, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}