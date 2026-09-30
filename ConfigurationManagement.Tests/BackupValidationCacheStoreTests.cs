using System;
using System.IO;
using System.Linq;
using Configuration_Management.Localization;
using Configuration_Management.Models;
using Configuration_Management.Services;
using Xunit;

namespace ConfigurationManagement.Tests;

/// <summary>
/// Тесты JSON-кэша результатов проверки резервных копий
/// (<see cref="BackupValidationCacheStore"/>, функция 8, этап 0.3.9.200):
/// CRUD по пути файла, нормализация, читаемый UTF-8, атомарная запись
/// и устаревание записи по fingerprint. Тесты используют временный каталог
/// (directoryOverride) — реальные данные профиля не затрагиваются.
/// </summary>
public sealed class BackupValidationCacheStoreTests : IDisposable
{
    private readonly string _tempDir;
    private readonly BackupValidationCacheStore _store;

    public BackupValidationCacheStoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "cm_backupval_cache_tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _store = new BackupValidationCacheStore(directoryOverride: _tempDir);
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
            // Временный каталог может быть занят антивирусом; для теста это не критично.
        }
    }

    private static string TempBackupFile(string dir, string name = "backup.dt")
    {
        var path = Path.Combine(dir, name);
        File.WriteAllText(path, "fake-dt-content");
        return path;
    }

    [Fact]
    public void FilePath_IsInsideDataDirectory()
    {
        Assert.Equal(
            Path.Combine(_tempDir, BackupValidationCacheStore.FileName),
            _store.FilePath);
    }

    [Fact]
    public void Save_And_Get_RoundTrip()
    {
        var path = TempBackupFile(_tempDir);
        var result = new BackupValidationResult
        {
            FilePath = path,
            Status = BackupValidationStatus.Valid,
            Depth = BackupValidationDepth.Full,
            CheckedAt = new DateTime(2026, 3, 12, 2, 0, 0),
            Duration = TimeSpan.FromMinutes(4),
            SizeBytes = 123,
            Fingerprint = BackupValidationResult.BuildFingerprint(path),
            ErrorDetail = null
        };

        _store.Save(result);

        var loaded = _store.Get(path);
        Assert.NotNull(loaded);
        Assert.Equal(BackupValidationStatus.Valid, loaded!.Status);
        Assert.Equal(BackupValidationDepth.Full, loaded.Depth);
        Assert.Equal(new DateTime(2026, 3, 12, 2, 0, 0), loaded.CheckedAt);
        Assert.Equal(TimeSpan.FromMinutes(4), loaded.Duration);
        Assert.Equal(123, loaded.SizeBytes);
        Assert.Equal(result.Fingerprint, loaded.Fingerprint);
        Assert.Equal(string.Empty, loaded.ErrorDetail); // null → пустая строка при нормализации
    }

    [Fact]
    public void Save_SamePath_OverwritesSingleEntry()
    {
        var path = TempBackupFile(_tempDir);
        _store.Save(new BackupValidationResult
        {
            FilePath = path,
            Status = BackupValidationStatus.Valid,
            CheckedAt = new DateTime(2026, 1, 1)
        });
        _store.Save(new BackupValidationResult
        {
            FilePath = path,
            Status = BackupValidationStatus.Corrupt,
            CheckedAt = new DateTime(2026, 2, 1),
            ErrorKind = "BackupValidation.Error.Timeout"
        });

        var all = _store.LoadAll();
        Assert.Single(all);
        Assert.Equal(BackupValidationStatus.Corrupt, all[0].Status);
        Assert.Equal(new DateTime(2026, 2, 1), all[0].CheckedAt);
    }

    [Fact]
    public void Save_DifferentPaths_KeepsBothEntries()
    {
        var a = TempBackupFile(_tempDir, "a.dt");
        var b = TempBackupFile(_tempDir, "b.cf");
        _store.Save(new BackupValidationResult { FilePath = a, Status = BackupValidationStatus.Valid });
        _store.Save(new BackupValidationResult { FilePath = b, Status = BackupValidationStatus.Valid });

        Assert.Equal(2, _store.LoadAll().Count);
        Assert.NotNull(_store.Get(a));
        Assert.NotNull(_store.Get(b));
    }

    [Fact]
    public void LoadAll_MissingFile_ReturnsEmpty()
    {
        Assert.Empty(_store.LoadAll());
    }

    [Fact]
    public void LoadAll_SortedByPath()
    {
        var a = TempBackupFile(_tempDir, "a.dt");
        var b = TempBackupFile(_tempDir, "b.dt");
        var z = TempBackupFile(_tempDir, "z.dt");
        _store.Save(new BackupValidationResult { FilePath = z });
        _store.Save(new BackupValidationResult { FilePath = a });
        _store.Save(new BackupValidationResult { FilePath = b });

        var paths = _store.LoadAll().Select(r => r.FilePath).ToList();
        Assert.Equal(
            new[] { a, b, z }.Select(p => Path.GetFullPath(p)).OrderBy(p => p, StringComparer.OrdinalIgnoreCase),
            paths);
    }

    [Fact]
    public void Get_IsCaseInsensitive_AndNormalizesPath()
    {
        var path = TempBackupFile(_tempDir);
        _store.Save(new BackupValidationResult { FilePath = path, Status = BackupValidationStatus.Valid });

        // Разный регистр (буква диска, каталоги, имя файла) — тот же ключ:
        // сравнение OrdinalIgnoreCase по нормализованному полному пути.
        var upper = Path.Combine(_tempDir.ToUpperInvariant(), Path.GetFileName(path).ToUpperInvariant());

        Assert.NotNull(_store.Get(upper));
    }

    [Fact]
    public void Get_UnknownPath_ReturnsNull()
    {
        Assert.Null(_store.Get(Path.Combine(_tempDir, "missing.dt")));
    }

    [Fact]
    public void Delete_RemovesEntryByPath()
    {
        var path = TempBackupFile(_tempDir);
        _store.Save(new BackupValidationResult { FilePath = path, Status = BackupValidationStatus.Valid });

        _store.Delete(path);

        Assert.Null(_store.Get(path));
        Assert.Empty(_store.LoadAll());
    }

    [Fact]
    public void Delete_UnknownPath_IsNoOp_AndMissingFileIsSafe()
    {
        var path = TempBackupFile(_tempDir);
        _store.Save(new BackupValidationResult { FilePath = path, Status = BackupValidationStatus.Valid });

        _store.Delete(Path.Combine(_tempDir, "unknown.dt"));

        Assert.Single(_store.LoadAll());

        // Удаление при отсутствии файла кэша не бросает.
        var empty = new BackupValidationCacheStore(directoryOverride: Path.Combine(_tempDir, "sub"));
        empty.Delete(path);
    }

    [Fact]
    public void Save_NullOrEmptyPath_IsNoOp()
    {
        _store.Save(new BackupValidationResult { FilePath = "" });
        _store.Save(null!);
        Assert.Empty(_store.LoadAll());
    }

    [Fact]
    public void LoadAll_BrokenJson_ReturnsEmpty_WithoutThrowing()
    {
        File.WriteAllText(_store.FilePath, "{ это не json !!!");
        Assert.Empty(_store.LoadAll());
    }

    [Fact]
    public void LoadAll_SkipsEntryWithoutPath_AndNormalizesNulls()
    {
        // Валидный JSON со смесью: запись без пути (пропускается), запись с null-полями
        // (нормализуется), enum числами и строками (принимаются оба).
        var json = "[\r\n" +
            "  { \"FilePath\": \"\", \"Status\": \"Valid\" },\r\n" +
            "  { \"FilePath\": \"C:\\\\backup\\\\x.dt\", \"Status\": \"Corrupt\",\r\n" +
            "    \"ErrorKind\": null, \"ErrorDetail\": null, \"Depth\": \"Full\",\r\n" +
            "    \"Fingerprint\": null },\r\n" +
            "  { \"FilePath\": \"C:\\\\backup\\\\y.dt\", \"Status\": 1 }\r\n" + // 1 == Valid
            "]";
        File.WriteAllText(_store.FilePath, json);

        var all = _store.LoadAll();
        Assert.Equal(2, all.Count);
        var corrupt = all.First(r => r.Status == BackupValidationStatus.Corrupt);
        Assert.Equal(string.Empty, corrupt.ErrorKind);
        Assert.Equal(string.Empty, corrupt.ErrorDetail);
        Assert.Equal(BackupValidationDepth.Full, corrupt.Depth);
        Assert.Equal(string.Empty, corrupt.Fingerprint);
        Assert.Contains(all, r => r.Status == BackupValidationStatus.Valid && r.FilePath.EndsWith("y.dt"));
    }

    [Fact]
    public void Save_WritesReadableUtf8_EnumsAsStrings_AndNoTempLeft()
    {
        var path = TempBackupFile(_tempDir, "резервная копия.dt");
        _store.Save(new BackupValidationResult
        {
            FilePath = path,
            Status = BackupValidationStatus.Corrupt,
            Depth = BackupValidationDepth.Fast,
            ErrorKind = "BackupValidation.Error.Timeout",
            ErrorDetail = "Файл занят",
            Fingerprint = "10|2026-03-12T00:00:00Z"
        });

        var text = File.ReadAllText(_store.FilePath);
        Assert.Contains("Corrupt", text);          // enum строкой
        Assert.Contains("Fast", text);
        Assert.Contains("Файл занят", text);       // кириллица читаемая UTF-8
        Assert.Contains("резервная копия.dt", text);
        Assert.DoesNotContain(@"\u", text);
        Assert.False(File.Exists(_store.FilePath + ".tmp")); // атомарная запись
    }

    [Fact]
    public void Fingerprint_DetectsFileChange_AndDisplayForShowsNotChecked()
    {
        var path = TempBackupFile(_tempDir);
        _store.Save(new BackupValidationResult
        {
            FilePath = path,
            Status = BackupValidationStatus.Valid,
            CheckedAt = new DateTime(2026, 3, 12, 2, 0, 0),
            Fingerprint = BackupValidationResult.BuildFingerprint(path)
        });

        var loaded = _store.Get(path)!;
        Assert.True(loaded.IsFresh(path));
        Assert.Contains(LocalizationManager.T("BackupValidation.Status.Valid"), loaded.DisplayTextFor(path));

        // Файл изменился после проверки — запись в кэше остаётся, но отображение даёт «не проверена».
        File.AppendAllText(path, "more data");

        Assert.False(loaded.IsFresh(path));
        Assert.Equal(LocalizationManager.T("BackupValidation.Status.NotChecked"), loaded.DisplayTextFor(path));
        Assert.NotNull(_store.Get(path)); // запись из хранилища не удаляется
    }

    [Fact]
    public void DisplayTextFor_MissingFile_ShowsNotChecked()
    {
        var missing = Path.Combine(_tempDir, "gone.dt");
        var result = new BackupValidationResult
        {
            FilePath = missing,
            Status = BackupValidationStatus.Valid,
            CheckedAt = new DateTime(2026, 3, 12, 2, 0, 0),
            Fingerprint = "10|2026-03-12T00:00:00Z"
        };
        Assert.False(result.IsFresh(missing));
        Assert.Equal(LocalizationManager.T("BackupValidation.Status.NotChecked"), result.DisplayTextFor(missing));
    }

    [Fact]
    public void DisplayText_FormatsStatusWithDate()
    {
        var result = new BackupValidationResult
        {
            Status = BackupValidationStatus.Valid,
            CheckedAt = new DateTime(2026, 3, 12, 2, 0, 0)
        };
        Assert.Equal(
            LocalizationManager.T("BackupValidation.Status.Valid") + " 12.03.2026 02:00",
            result.DisplayText);

        result.Status = BackupValidationStatus.NotChecked;
        Assert.Equal(LocalizationManager.T("BackupValidation.Status.NotChecked"), result.DisplayText);
    }
}