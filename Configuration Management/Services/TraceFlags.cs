using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Configuration_Management.Services;

/// <summary>
/// Механизм отладочных флагов диагностики (issue #347 «Сказ о trace.json», кластер C).
/// Файл-конфиг <c>trace.json</c> РЯДОМ с настройками приложения
/// (<see cref="PlatformPaths.AppDataDirectory"/>; Windows: %APPDATA%\ConfigurationManagement\,
/// Linux: ~/.config/ConfigurationManagement/) несёт флажки диагностики:
/// <c>{"version":1,"CM_COLUMNS":false,"CM_MENUCLICK":false,"CM_MENUCLOSE":false,"CM_REDIRECT":false}</c>.
/// <para>
/// По умолчанию ВСЕ флаги выключены — логи закрытых тикетов перестают «капать». Пользователь
/// включает нужный флаг по наставлению из тикета (правит JSON или ждёт инструкции), файл
/// перечитывается при изменении <c>LastWriteTimeUtc</c> — БЕЗ перезапуска приложения.
/// </para>
/// <para>
/// Старый JSONL-журнал (0.3.9.308–0.3.9.314, первая строка <c>{"ts":…</c>) при первом старте
/// новой версии переименовывается в <c>trace_menuclose_legacy.json</c> — история диагностики
/// сохраняется, а <c>trace.json</c> становится конфигом флагов. Повреждённый JSON трактуется
/// как «все флаги выключены» с предупреждением в общий лог приложения (файл не перезаписывается).
/// </para>
/// <para>
/// Окружение (env-переменная) остаётся только OVERRIDE ВКЛЮЧЕНИЯ: если переменная с именем
/// флага (например <c>CM_REDIRECT=1</c>) установлена в <c>1</c> — флаг включён независимо от
/// конфига; для <c>CM_COLUMNS</c> поддерживается прежнее имя <c>CM_COLUMNS_TRACE=1</c>.
/// Выключить флаг через env нельзя — только через конфиг.
/// </para>
/// </summary>
public static class TraceFlags
{
    /// <summary>Флаг диагностики колонок списка (CM_COLUMNS): стартовый дамп + изменения ширины.</summary>
    public const string ColumnsFlag = "CM_COLUMNS";

    /// <summary>Флаг отладки правого клика/открытия контекстного меню (CM_MENUCLICK);
    /// в этой версии — псевдоним на <see cref="MenuCloseFlag"/>.</summary>
    public const string MenuClickFlag = "CM_MENUCLICK";

    /// <summary>Флаг журнала событий меню/кликов дерева (CM_MENUCLOSE): trace_menuclose.jsonl.</summary>
    public const string MenuCloseFlag = "CM_MENUCLOSE";

    /// <summary>Флаг INFO-диагностики редиректов/входа портала 1С (CM_REDIRECT).</summary>
    public const string RedirectFlag = "CM_REDIRECT";

    private static readonly object Lock = new();
    private static string? _configDirectoryOverride;
    private static Func<string, string?> _envReader = Environment.GetEnvironmentVariable;
    private static bool _initialized;
    private static DateTime _lastMtimeUtc;
    private static IReadOnlyDictionary<string, bool> _flags = TraceFlagsFormat.EmptyFlags;

    /// <summary>
    /// Каталог конфига флагов для юнит-тестов (по умолчанию — <see cref="PlatformPaths.AppDataDirectory"/>).
    /// Установка значения сбрасывает кэш. Намеренно internal: в проде каталог всегда платформенный.
    /// </summary>
    internal static string? ConfigDirectoryOverride
    {
        get { lock (Lock) return _configDirectoryOverride; }
        set { lock (Lock) { _configDirectoryOverride = value; ResetCacheCore(); } }
    }

    /// <summary>Точка подмены чтения env-переменных для юнит-тестов.</summary>
    internal static Func<string, string?> EnvReader
    {
        get { lock (Lock) return _envReader; }
        set
        {
            if (value is null)
                throw new ArgumentNullException(nameof(value));
            lock (Lock) _envReader = value;
        }
    }

    /// <summary>Сброс кэша (тесты): следующий <see cref="IsEnabled"/> перечитает конфиг заново.</summary>
    internal static void ResetCacheForTesting()
    {
        lock (Lock) ResetCacheCore();
    }

    private static void ResetCacheCore()
    {
        _initialized = false;
        _lastMtimeUtc = default;
        _flags = TraceFlagsFormat.EmptyFlags;
    }

    /// <summary>
    /// Гарантирует наличие конфига флагов при старте приложения (issue #347): создаёт каталог
    /// данных, при первом старте новой версии мигрирует старый JSONL-журнал в
    /// <c>trace_menuclose_legacy.json</c> (см. <see cref="TraceFlagsFormat.ShouldMigrateLegacyJsonl"/>)
    /// и создаёт <c>trace.json</c>-конфиг с дефолтами, если файла нет. Идемпотентна; ошибки
    /// игнорируются — диагностика не должна влиять на работу приложения.
    /// Вызывается из точек входа (WPF/Avalonia) и из <see cref="MenuCloseTrace.EnsureStarted"/>.
    /// </summary>
    public static void EnsureExists()
    {
        lock (Lock) EnsureExistsCore();
    }

    /// <summary>
    /// Включён ли отладочный флаг (регистр безразличен). Сначала проверяется env-override
    /// включения (<c><ФЛАГ>=1</c>, для колонок также прежний <c>CM_COLUMNS_TRACE=1</c>) —
    /// без файлового I/O; затем конфиг <c>trace.json</c> с mtime-кэшем: файл перечитывается
    /// только при изменении <see cref="File.GetLastWriteTimeUtc"/>, поэтому пользователь может
    /// включить флаг без перезапуска приложения. Повреждённый JSON → все флаги выключены
    /// + предупреждение в общий лог. Потокобезопасна.
    /// </summary>
    public static bool IsEnabled(string flag)
    {
        if (string.IsNullOrWhiteSpace(flag))
            return false;
        var name = flag.Trim().ToUpperInvariant();
        if (EnvOverride(name))
            return true;
        lock (Lock)
        {
            EnsureExistsCore();
            ReloadIfChangedCore();
            return TraceFlagsFormat.IsEnabled(_flags, name);
        }
    }

    /// <summary>Журнал событий меню включён: CM_MENUCLOSE либо его псевдоним CM_MENUCLICK (issue #347).</summary>
    public static bool IsMenuEnabled()
        => IsEnabled(MenuCloseFlag) || IsEnabled(MenuClickFlag);

    /// <summary>env-override включения: переменная с именем флага = "1" (для колонок — ещё и CM_COLUMNS_TRACE).</summary>
    private static bool EnvOverride(string name)
    {
        try
        {
            if (string.Equals(_envReader(name), "1", StringComparison.OrdinalIgnoreCase))
                return true;
            if (name == ColumnsFlag &&
                string.Equals(_envReader("CM_COLUMNS_TRACE"), "1", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        catch
        {
            // Чтение env не должно ронять диагностику.
        }
        return false;
    }

    private static void EnsureExistsCore()
    {
        var path = ConfigFilePath();
        if (path is null)
            return;

        try
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
        }
        catch
        {
            // Каталог недоступен (нет прав) — флаги остаются выключенными.
            return;
        }

        try
        {
            if (File.Exists(path))
            {
                // Миграция старого JSONL-журнала (первая строка {"ts":...) в trace_menuclose_legacy.json
                // (issue #347): журнал переезжает в trace_menuclose.jsonl, а trace.json становится конфигом.
                if (TraceFlagsFormat.ShouldMigrateLegacyJsonl(ReadAllTextQuietly(path)))
                {
                    var legacyPath = Path.Combine(
                        Path.GetDirectoryName(path)!, TraceFlagsFormat.LegacyJsonlBackupFileName);
                    File.Move(path, legacyPath, overwrite: true);
                }
            }

            // Конфиг создаётся при каждом старте, если отсутствует (в т.ч. после миграции).
            if (!File.Exists(path))
                File.WriteAllText(path, TraceFlagsFormat.SerializeDefaults(), Encoding.UTF8);

            _initialized = false; // следующий IsEnabled перечитает файл с диска
        }
        catch
        {
            // Диагностика не должна влиять на работу приложения.
        }
    }

    private static void ReloadIfChangedCore()
    {
        var path = ConfigFilePath();
        if (path is null)
            return;

        DateTime mtime;
        try
        {
            mtime = File.GetLastWriteTimeUtc(path);
        }
        catch
        {
            mtime = DateTime.MinValue;
        }

        // Кэш актуален — файл не трогаем (mtime не изменился).
        if (_initialized && mtime == _lastMtimeUtc)
            return;

        try
        {
            var text = ReadAllTextQuietly(path);
            var parsed = TraceFlagsFormat.Parse(text);
            _flags = parsed.Flags;
            _lastMtimeUtc = File.GetLastWriteTimeUtc(path);
            _initialized = true;
            if (!parsed.IsValid)
                WarnBrokenConfig();
        }
        catch
        {
            _flags = TraceFlagsFormat.EmptyFlags;
            _lastMtimeUtc = mtime;
            _initialized = true;
            WarnBrokenConfig();
        }
    }

    private static string? ReadAllTextQuietly(string path)
    {
        try { return File.ReadAllText(path, Encoding.UTF8); }
        catch { return null; }
    }

    /// <summary>Предупреждение о повреждённом конфиге в общий лог приложения (файл НЕ перезаписывается).</summary>
    private static void WarnBrokenConfig()
    {
        try
        {
            AppServices.GetRequiredService<IAppLogger>().Warn(
                "[TraceFlags] trace.json повреждён или не читается — все флаги считаются выключенными (файл не перезаписывается).");
        }
        catch
        {
            // Логгер может быть ещё не готов (ранний старт) — предупреждение теряется.
        }
    }

    private static string? ConfigFilePath()
    {
        var dir = _configDirectoryOverride ?? PlatformPaths.AppDataDirectory;
        return string.IsNullOrWhiteSpace(dir) ? null : Path.Combine(dir, TraceFlagsFormat.ConfigFileName);
    }
}