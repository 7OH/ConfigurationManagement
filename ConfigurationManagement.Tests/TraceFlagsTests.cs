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
/// игнорируются, битый JSON → все выключены без исключения), pretty-print с переводами
/// строк/отступами и разбором обратно, жёсткая семантика гейта (явный false в конфиге
/// выключает флаг ВСЕГДА — env его не перекрывает), решение о миграции старого
/// JSONL-журнала, перенос конфига из корня в каталог активного профиля и перечитывание
/// конфига по mtime без перезапуска приложения.
/// </summary>
[Collection("TraceFlagsState")]
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

    // ============ Сериализация: pretty-print + разбор обратно ============

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

    [Fact]
    public void Serialize_PrettyPrinted_RoundTrips()
    {
        // issue #347, замечание 1: trace.json пишется с ПЕРЕНОСАМИ СТРОК И ОТСТУПАМИ,
        // а не в одну строку; парсер разбирает такой формат обратно без потерь.
        var json = TraceFlagsFormat.SerializeDefaults();

        Assert.Contains('\n', json);
        Assert.Contains("\n  \"CM_COLUMNS\": false", json, StringComparison.Ordinal);
        Assert.Contains("\n  \"version\": 1", json, StringComparison.Ordinal);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.Equal(1, root.GetProperty("version").GetInt32());
        foreach (var flag in TraceFlagsFormat.KnownFlags)
            Assert.False(root.GetProperty(flag).GetBoolean());

        var parsed = TraceFlagsFormat.Parse(json);
        Assert.True(parsed.IsValid);
        foreach (var flag in TraceFlagsFormat.KnownFlags)
            Assert.False(TraceFlagsFormat.IsEnabled(parsed.Flags, flag));
    }

    // ============ Жёсткий гейт: явный false в конфиге > env (issue #347, замечание 2) ============

    [Fact]
    public void ExplicitFalse_InConfig_WithEnv1_IsDisabled()
    {
        using var dir = new TempDir(envOverride: name =>
            name == TraceFlags.ColumnsFlag || name == "CM_COLUMNS_TRACE" ? "1" : null);
        var configPath = Path.Combine(dir.Path, TraceFlagsFormat.ConfigFileName);
        File.WriteAllText(configPath, "{\"version\":1,\"CM_COLUMNS\":false}", System.Text.Encoding.UTF8);
        File.SetLastWriteTimeUtc(configPath, DateTime.UtcNow.AddSeconds(5));

        // Явное false в trace.json выключает флаг ВСЕГДА — env-переменная (в т.ч. прежняя
        // CM_COLUMNS_TRACE=1 от 0.3.9.305) НЕ перекрывает: записей CM_COLUMNS нет.
        Assert.False(TraceFlags.IsEnabled(TraceFlags.ColumnsFlag));
    }

    [Fact]
    public void True_InConfig_IsEnabled()
    {
        using var dir = new TempDir();
        var configPath = Path.Combine(dir.Path, TraceFlagsFormat.ConfigFileName);
        File.WriteAllText(configPath, "{\"version\":1,\"CM_COLUMNS\":true}", System.Text.Encoding.UTF8);
        File.SetLastWriteTimeUtc(configPath, DateTime.UtcNow.AddSeconds(5));

        // Флаг true в конфиге → диагностика включена (env при этом не обязательна).
        Assert.True(TraceFlags.IsEnabled(TraceFlags.ColumnsFlag));
    }

    [Fact]
    public void NoFile_WithEnv1_IsEnabled()
    {
        using var dir = new TempDir(envOverride: name =>
            name == TraceFlags.ColumnsFlag || name == "CM_COLUMNS_TRACE" ? "1" : null);

        // Файла конфига нет (EnsureExists заранее не вызывали): env-переменная остаётся
        // ЗАПАСНЫМ способом включения — флаг включён.
        Assert.False(File.Exists(Path.Combine(dir.Path, TraceFlagsFormat.ConfigFileName)));
        Assert.True(TraceFlags.IsEnabled(TraceFlags.ColumnsFlag));
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
        var parsed = TraceFlagsFormat.Parse(File.ReadAllText(configPath));
        Assert.True(parsed.IsValid, "новый trace.json должен разбираться как конфиг флагов");
        Assert.False(TraceFlagsFormat.IsEnabled(parsed.Flags, TraceFlags.ColumnsFlag));
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

    // ============ Перенос конфига в каталог активного профиля (issue #347, замечание 3) ============

    [Fact]
    public void ProfileMigration_RootTraceJson_MovedIntoProfileDirectory()
    {
        using var dir = new TempDir();
        // До выбора профиля EnsureExists создал trace.json в корне каталога данных.
        var rootPath = Path.Combine(dir.Path, TraceFlagsFormat.ConfigFileName);
        File.WriteAllText(rootPath, TraceFlagsFormat.SerializeDefaults(), System.Text.Encoding.UTF8);
        var profileDir = Path.Combine(dir.Path, "profiles", "p1");

        TraceFlags.SetProfileDataDirectory(profileDir);

        // Конфиг перенесён рядом с settings.json активного профиля, из корня исчез.
        var target = Path.Combine(profileDir, TraceFlagsFormat.ConfigFileName);
        Assert.True(File.Exists(target), "trace.json должен быть перенесён в каталог профиля");
        Assert.False(File.Exists(rootPath), "корневой trace.json после переноса не должен оставаться");
        // Чтение идёт уже из профильного каталога (дефолты → флаг выключен).
        Assert.False(TraceFlags.IsEnabled(TraceFlags.ColumnsFlag));
    }

    [Fact]
    public void ProfileMigration_NoRootFile_CreatesInProfileOnDemand()
    {
        using var dir = new TempDir();
        var profileDir = Path.Combine(dir.Path, "profiles", "p1");

        TraceFlags.SetProfileDataDirectory(profileDir);

        Assert.False(TraceFlags.IsEnabled(TraceFlags.ColumnsFlag));
        Assert.True(
            File.Exists(Path.Combine(profileDir, TraceFlagsFormat.ConfigFileName)),
            "конфиг должен создаться в каталоге профиля, а не в корне");
    }

    [Fact]
    public void ProfileMigration_ProfileConfigExists_RemovesRootDuplicate()
    {
        // issue #349: на повторном запуске EnsureExists (вызывается до выбора профиля)
        // заново создаёт trace.json в корне каталога данных рядом с profiles.json, хотя
        // в папке профиля конфиг уже есть. Дубль должен удаляться, а не копиться.
        // Корневой файл создаём ВРУЧНУЮ (эмуляция результата EnsureExists) — прямое
        // обращение к глобальному ConfigFilePath в параллельном прогоне может попасть
        // в момент, когда другой тестовый класс меняет ConfigDirectoryOverride.
        using var dir = new TempDir();
        var profileDir = Path.Combine(dir.Path, "profiles", "p1");
        var rootPath = Path.Combine(dir.Path, TraceFlagsFormat.ConfigFileName);
        var profilePath = Path.Combine(profileDir, TraceFlagsFormat.ConfigFileName);

        // Профильный конфиг с пользовательской правкой (флаг включён) — существовал
        // с прошлого запуска; его содержимое не должно измениться.
        Directory.CreateDirectory(profileDir);
        File.WriteAllText(profilePath, "{\"version\":1,\"CM_COLUMNS\":true}", System.Text.Encoding.UTF8);

        // До выбора профиля приложение создало trace.json в корне (дефолты, все false).
        File.WriteAllText(rootPath, TraceFlagsFormat.SerializeDefaults(), System.Text.Encoding.UTF8);

        TraceFlags.SetProfileDataDirectory(profileDir);

        Assert.False(File.Exists(rootPath), "корневой дубль trace.json должен удаляться (issue #349)");
        Assert.True(File.Exists(profilePath), "профильный конфиг остаётся на месте");
        Assert.Contains("\"CM_COLUMNS\":true", File.ReadAllText(profilePath), StringComparison.Ordinal);
        Assert.True(TraceFlags.IsEnabled(TraceFlags.ColumnsFlag));
    }

    [Fact]
    public void ProfileMigration_LegacyJsonlInRoot_WithProfileConfig_IsNotDeleted()
    {
        // issue #349: в корне лежит старый JSONL-журнал (0.3.9.308–0.3.9.314, первая
        // строка {"ts":...) — это НЕ дубль конфига: его не удаляем (обрабатывает
        // EnsureExistsCore — переименование в trace_menuclose_legacy.json).
        using var dir = new TempDir();
        var profileDir = Path.Combine(dir.Path, "profiles", "p1");
        var rootLegacy = Path.Combine(dir.Path, TraceFlagsFormat.ConfigFileName);
        var profilePath = Path.Combine(profileDir, TraceFlagsFormat.ConfigFileName);

        Directory.CreateDirectory(profileDir);
        File.WriteAllText(profilePath, TraceFlagsFormat.SerializeDefaults(), System.Text.Encoding.UTF8);
        File.WriteAllText(
            rootLegacy,
            MenuCloseTraceFormat.BuildStartupLine("0.3.9.314", "WPF", "Windows", dir.Path, Ts, threadId: 1) + "\n",
            System.Text.Encoding.UTF8);

        TraceFlags.SetProfileDataDirectory(profileDir);

        Assert.True(File.Exists(rootLegacy), "legacy JSONL-журнал в корне не должен удаляться");
        Assert.True(File.Exists(profilePath), "профильный конфиг не затронут");
    }

    [Fact]
    public void ProfileMigration_SameDirectoryAsRoot_NoDeletion()
    {
        // Легаси-режим без профиля: CurrentProfileDataDirectory == корню каталога данных.
        // sourcePath == targetPath — метод выходит раньше любой работы с файлами.
        using var dir = new TempDir();
        var rootPath = Path.Combine(dir.Path, TraceFlagsFormat.ConfigFileName);
        File.WriteAllText(rootPath, TraceFlagsFormat.SerializeDefaults(), System.Text.Encoding.UTF8);

        TraceFlags.SetProfileDataDirectory(dir.Path);

        Assert.True(File.Exists(rootPath), "при совпадении каталогов файл не должен удаляться");
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

    /// <summary>Временный каталог теста с установкой каталога конфига TraceFlags.</summary>
    private sealed class TempDir : IDisposable
    {
        private readonly string? _previousDir;
        private readonly Func<string, string?> _previousEnv;
        private bool _disposed;

        public TempDir(Func<string, string?>? envOverride = null)
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), $"cm_traceflags_{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
            _previousDir = TraceFlags.ConfigDirectoryOverride;
            _previousEnv = TraceFlags.EnvReader;
            // Изолируем тест от env-переменных процесса, НО не отключаем CM_REDIRECT:
            // тесты входа портала (OneCUpdatesLoginFlow и др.) выполняются параллельно
            // и полагаются на этот override включения (TestEnvironment.ModuleInitializer).
            // envOverride позволяет конкретному тесту включить свой флаг (например CM_COLUMNS=1).
            TraceFlags.EnvReader = name =>
            {
                if (string.Equals(name, TraceFlags.RedirectFlag, StringComparison.Ordinal))
                    return Environment.GetEnvironmentVariable(name);
                return envOverride?.Invoke(name);
            };
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