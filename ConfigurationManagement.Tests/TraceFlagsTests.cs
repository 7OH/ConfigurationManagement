using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Configuration_Management.Services;
using Xunit;

namespace ConfigurationManagement.Tests;

/// <summary>
/// Тесты механизма отладочных флагов trace.json (issue #347 «Сказ о trace.json», кластер C):
/// чистый разбор/сериализация конфига (регистр имён безразличен, неизвестные флаги
/// игнорируются, битый JSON → все выключены без исключения), решение о миграции старого
/// JSONL-журнала и перечитывание конфига по mtime без перезапуска приложения.
/// </summary>
public sealed class TraceFlagsTests
{
    private static readonly DateTimeOffset Ts =
        new DateTimeOffset(2026, 10, 4, 20, 30, 0, TimeSpan.FromHours(3));

    // ============ Чистый парсер: дефолты, регистр, битый JSON, неизвестные флаги ============

    [Fact]
    public void Parse_MissingFile_ReturnsDefaultsAllFalse()
    {
        // Отсутствие данных (файла) трактуется как «все флаги выключены».
        var result = TraceFlagsFormat.Parse(null);

        Assert.False(result.IsValid);
        foreach (var flag in TraceFlagsFormat.KnownFlags)
            Assert.False(TraceFlagsFormat.IsEnabled(result.Flags, flag));
    }

    [Fact]
    public void Parse_JsonWithFlags_ReadsValues_IgnoreCase()
    {
        // Имена флагов в JSON могут быть в любом регистре; запрос IsEnabled тоже
        // регистронезависим. Отсутствующий флаг — false.
        const string json = "{\"version\":1,\"cm_columns\":true,\"cm_menuclose\":false,\"CM_REDIRECT\":true}";

        var result = TraceFlagsFormat.Parse(json);

        Assert.True(result.IsValid);
        Assert.True(TraceFlagsFormat.IsEnabled(result.Flags, "CM_COLUMNS"));
        Assert.False(TraceFlagsFormat.IsEnabled(result.Flags, "CM_MENUCLOSE"));
        Assert.True(TraceFlagsFormat.IsEnabled(result.Flags, "cm_redirect")); // регистр запроса безразличен
        Assert.False(TraceFlagsFormat.IsEnabled(result.Flags, "CM_MENUCLICK")); // отсутствует → false
    }

    [Fact]
    public void Parse_BrokenJson_ReturnsDefaults_NoThrow()
    {
        // Повреждённый JSON не бросает исключение: все флаги выключены (файл при этом
        // НЕ перезаписывается — решение принимает TraceFlags, здесь только признак).
        var result = TraceFlagsFormat.Parse("{broken json!!");

        Assert.False(result.IsValid);
        foreach (var flag in TraceFlagsFormat.KnownFlags)
            Assert.False(TraceFlagsFormat.IsEnabled(result.Flags, flag));
    }

    [Fact]
    public void Parse_UnknownFlags_Ignored()
    {
        // Неизвестные имена игнорируются (устойчивость к будущим версиям), известные читаются.
        const string json = "{\"version\":2,\"CM_FUTURE\":true,\"CM_COLUMNS\":true}";

        var result = TraceFlagsFormat.Parse(json);

        Assert.True(result.IsValid);
        Assert.True(TraceFlagsFormat.IsEnabled(result.Flags, "CM_COLUMNS"));
        Assert.False(TraceFlagsFormat.IsEnabled(result.Flags, "CM_FUTURE"));
    }

    [Fact]
    public void Serialize_CreatesVersionedDefaults()
    {
        // Дефолтный конфиг: version=1, все флаги false, стабильный порядок ключей.
        var json = TraceFlagsFormat.SerializeDefaults();

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.Equal(1, root.GetProperty("version").GetInt32());
        foreach (var flag in TraceFlagsFormat.KnownFlags)
            Assert.False(root.GetProperty(flag).GetBoolean());

        var keys = root.EnumerateObject().Select(p => p.Name).ToArray();
        Assert.Equal(
            new[] { "version" }.Concat(TraceFlagsFormat.KnownFlags).ToArray(),
            keys);
    }

    // ============ Миграция старого JSONL-журнала (I/O через временный каталог) ============

    [Fact]
    public void Migrate_JsonlTrace_RenamesToLegacy()
    {
        using var dir = new TempDir();
        // Старый trace.json — JSONL-журнал 0.3.9.308–0.3.9.314 (первая строка {"ts":...).
        var configPath = Path.Combine(dir.Path, TraceFlagsFormat.ConfigFileName);
        var legacyPath = Path.Combine(dir.Path, TraceFlagsFormat.LegacyJsonlBackupFileName);
        File.WriteAllText(
            configPath,
            MenuCloseTraceFormat.BuildStartupLine(
                "0.3.9.314", "WPF", "Windows", dir.Path, Ts, threadId: 1) + "\n",
            System.Text.Encoding.UTF8);

        TraceFlags.EnsureExists();

        // История сохранена в trace_menuclose_legacy.json, а trace.json — снова конфиг.
        Assert.True(File.Exists(legacyPath), "старый JSONL-журнал должен быть переименован");
        Assert.True(File.Exists(configPath), "trace.json должен существовать как конфиг");
        Assert.StartsWith("{\"ts\":", File.ReadAllText(legacyPath).TrimStart(), StringComparison.Ordinal);
        Assert.StartsWith("{\"version\":1", File.ReadAllText(configPath), StringComparison.Ordinal);
    }

    [Fact]
    public void Migrate_ConfigJson_NotRenamed()
    {
        using var dir = new TempDir();
        var configPath = Path.Combine(dir.Path, TraceFlagsFormat.ConfigFileName);
        var legacyPath = Path.Combine(dir.Path, TraceFlagsFormat.LegacyJsonlBackupFileName);
        // Уже конфиг-формат (JSON-объект флагов) — миграция не нужна.
        File.WriteAllText(configPath, TraceFlagsFormat.SerializeDefaults(), System.Text.Encoding.UTF8);

        TraceFlags.EnsureExists();

        Assert.False(File.Exists(legacyPath), "конфиг не должен переименовываться");
        Assert.Equal(TraceFlagsFormat.SerializeDefaults(), File.ReadAllText(configPath));
    }

    // ============ Перечитывание конфига по mtime (без перезапуска) ============

    [Fact]
    public void Reload_OnMtimeChange_SeesNewValue()
    {
        using var dir = new TempDir();
        var configPath = Path.Combine(dir.Path, TraceFlagsFormat.ConfigFileName);

        // Первый старт: конфиг создан с дефолтами, флаг выключен.
        TraceFlags.EnsureExists();
        Assert.False(TraceFlags.IsEnabled("CM_MENUCLOSE"));

        // Пользователь включил флаг в конфиге (по наставлению из тикета) БЕЗ перезапуска:
        // следующий IsEnabled видит новое значение по изменению LastWriteTimeUtc.
        File.WriteAllText(
            configPath,
            "{\"version\":1,\"CM_MENUCLOSE\":true,\"CM_COLUMNS\":false}",
            System.Text.Encoding.UTF8);
        File.SetLastWriteTimeUtc(configPath, DateTime.UtcNow.AddSeconds(5));

        Assert.True(TraceFlags.IsEnabled("CM_MENUCLOSE"));
        Assert.False(TraceFlags.IsEnabled("CM_COLUMNS"));
    }

    /// <summary>Временный каталог теста с установкой каталога конфига TraceFlags (без env-override).</summary>
    private sealed class TempDir : IDisposable
    {
        private readonly string? _previousDir;
        private readonly Func<string, string?> _previousEnv;
        private bool _disposed;

        public TempDir()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), $"cm_traceflags_{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
            _previousDir = TraceFlags.ConfigDirectoryOverride;
            _previousEnv = TraceFlags.EnvReader;
            // Изолируем тест от env-переменных процесса, НО не отключаем CM_REDIRECT:
            // тесты входа портала (OneCUpdatesLoginFlow и др.) выполняются параллельно
            // и полагаются на этот override включения (TestEnvironment.ModuleInitializer).
            TraceFlags.EnvReader = name =>
                string.Equals(name, TraceFlags.RedirectFlag, StringComparison.Ordinal)
                    ? Environment.GetEnvironmentVariable(name)
                    : null;
            TraceFlags.ConfigDirectoryOverride = Path;
            TraceFlags.ResetCacheForTesting();
        }

        public string Path { get; }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            TraceFlags.ConfigDirectoryOverride = _previousDir;
            TraceFlags.EnvReader = _previousEnv;
            TraceFlags.ResetCacheForTesting();
            try { Directory.Delete(Path, recursive: true); } catch { /* не мешаем остальным тестам */ }
        }
    }
}