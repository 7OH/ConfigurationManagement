using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Configuration_Management.Services;

/// <summary>
/// Механизм отладочных флагов диагностики (issue #347 «Сказ о trace.json», кластер C).
/// Файл-конфиг <c>trace.json</c> лежит в каталоге данных АКТИВНОГО ПРОФИЛЯ — рядом с
/// <c>settings.json</c> (Windows: %APPDATA%\ConfigurationManagement\profiles\<Id>\,
/// Linux: ~/.config/ConfigurationManagement/profiles/<Id>/; legacy-режим без профиля —
/// корень <see cref="PlatformPaths.AppDataDirectory"/>). До выбора профиля при старте
/// конфиг создаётся в корне каталога данных, а после инициализации профилей переносится
/// (<see cref="SetProfileDataDirectory"/>). Формат — pretty-print JSON со стабильным порядком
/// ключей: <c>version</c>, <c>CM_COLUMNS</c>, <c>CM_MENUCLICK</c>, <c>CM_MENUCLOSE</c>,
/// <c>CM_REDIRECT</c>.
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
/// Семантика гейта (issue #347, замечание 2): ЯВНОЕ <c>false</c> в <c>trace.json</c> выключает
/// флаг ВСЕГДА — env-переменная его НЕ перекрывает. Окружение (например <c>CM_REDIRECT=1</c>;
/// для <c>CM_COLUMNS</c> также прежнее <c>CM_COLUMNS_TRACE=1</c>) остаётся только ЗАПАСНЫМ
/// способом включения: если флаг в конфиге отсутствует либо установлен в <c>true</c>. Файл,
/// только что созданный приложением с дефолтами (пользователь его ещё не правил), не считается
/// «явным выбором» — env продолжает работать в течение сессии до первой правки конфига.
/// </para>
/// </summary>
public static class TraceFlags
{
    /// <summary>Флаг диагностики колонок списка (CM_COLUMNS): стартовый дамп + изменения ширины.</summary>
    public const string ColumnsFlag = "CM_COLUMNS";

    /// <summary>Флаг отладки правого клика/открытия контекстного меню (CM_MENUCLICK);
    /// в этой версии — псевдоним на <see cref="MenuCloseFlag"/>.</summary>
    public const string MenuClickFlag = "CM_MENUCLICK";

    /// <summary>Флаг журнала событий меню/кликов дерева (CM_MENUCLOSE): trace_menuclose.json в логах.</summary>
    public const string MenuCloseFlag = "CM_MENUCLOSE";

    /// <summary>Флаг INFO-диагностики редиректов/входа портала 1С (CM_REDIRECT).</summary>
    public const string RedirectFlag = "CM_REDIRECT";

    private static readonly object Lock = new();
    private static string? _configDirectoryOverride;
    private static string? _profileDataDirectory;
    private static Func<string, string?> _envReader = Environment.GetEnvironmentVariable;
    private static bool _initialized;
    private static bool _configCreatedByUs;
    private static DateTime _lastMtimeUtc;
    private static IReadOnlyDictionary<string, bool> _flags = TraceFlagsFormat.EmptyFlags;

    /// <summary>
    /// Каталог конфига флагов для юнит-тестов (по умолчанию — <see cref="PlatformPaths.AppDataDirectory"/>).
    /// Установка значения сбрасывает кэш. Намеренно internal: в проде каталог всегда платформенный.
    /// </summary>
    internal static string? ConfigDirectoryOverride
    {
        get { lock (Lock) return _configDirectoryOverride; }
        set
        {
            lock (Lock)
            {
                _configDirectoryOverride = value;
                // Смена override тестами полностью изолирует состояние от предыдущих тестов.
                _profileDataDirectory = null;
                ResetCacheCore();
            }
        }
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
        lock (Lock)
        {
            // Полная изоляция между тестами: сбрасываем и привязку к профильному каталогу.
            _profileDataDirectory = null;
            ResetCacheCore();
        }
    }

    private static void ResetCacheCore()
    {
        _initialized = false;
        _configCreatedByUs = false;
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
    /// Переводит конфиг флагов в каталог данных активного профиля (issue #347, замечание 3):
    /// вызывается из точек входа ПОСЛЕ инициализации профилей (и выбора учётной записи).
    /// Существующий <c>trace.json</c> из прежнего расположения (корень каталога данных либо
    /// каталог, использовавшийся до смены) переносится в профильный каталог, если там его
    /// ещё нет. Идемпотентна; ошибки игнорируются — диагностика не должна влиять на работу.
    /// </summary>
    public static void SetProfileDataDirectory(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
            return;
        lock (Lock)
        {
            if (string.Equals(_profileDataDirectory, directory, StringComparison.OrdinalIgnoreCase))
                return;

            // Исходный файл — там, где конфиг лежал ДО смены каталога (корень AppDataDirectory
            // либо ConfigDirectoryOverride для тестов).
            var sourcePath = ConfigFilePath();
            _profileDataDirectory = directory;
            MigrateConfigFromRootCore(sourcePath);
            SyncCacheToCurrentPath();
        }
    }

    /// <summary>
    /// Включён ли отладочный флаг (регистр безразличен). Семантика гейта (issue #347,
    /// замечание 2): ЯВНОЕ <c>false</c> в конфиге <c>trace.json</c> выключает флаг ВСЕГДА —
    /// env-переменная его не перекрывает (записи диагностики не появляются даже при
    /// установленной env, как у пользователя с <c>CM_COLUMNS_TRACE=1</c> от 0.3.9.305).
    /// env-переменная (<c><ФЛАГ>=1</c>, для колонок также прежняя <c>CM_COLUMNS_TRACE=1</c>)
    /// остаётся ЗАПАСНЫМ способом включения: если флаг в конфиге отсутствует либо установлен
    /// в <c>true</c>. Файл, только что созданный приложением с дефолтами и ещё не правленный
    /// пользователем, не считается «явным false» — env работает в течение сессии.
    /// Конфиг перечитывается только при изменении <see cref="File.GetLastWriteTimeUtc"/> —
    /// правка trace.json применяется без перезапуска. Потокобезопасна.
    /// </summary>
    public static bool IsEnabled(string flag)
    {
        if (string.IsNullOrWhiteSpace(flag))
            return false;
        var name = flag.Trim().ToUpperInvariant();
        lock (Lock)
        {
            EnsureExistsCore();
            ReloadIfChangedCore();

            // Явный false в trace.json выключает флаг ВСЕГДА — env НЕ перекрывает.
            var present = _flags.TryGetValue(name, out var configured);
            if (present && !configured)
                return false;

            // env — только запасной способ включения (флаг отсутствует либо true).
            if (EnvOverride(name))
                return true;

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

    /// <summary>
    /// Перенос существующего <c>trace.json</c> из прежнего расположения в каталог активного
    /// профиля (issue #347, замечание 3; issue #349): при первом старте новой версии конфиг,
    /// созданный до выбора профиля в корне каталога данных, переезжает рядом с
    /// <c>settings.json</c>. Если в профильном каталоге файл УЖЕ есть — корневой файл
    /// является бесполезным дублем (EnsureExists до выбора профиля создал его заново при
    /// каждом запуске, читается всегда профильный файл — приоритет в ConfigFilePath),
    /// поэтому дубль УДАЛЯЕТСЯ, а не остаётся мусором рядом с profiles.json (issue #349).
    /// Прежний JSONL-журнал 0.3.9.308–0.3.9.314 НЕ переносится и НЕ удаляется — он
    /// остаётся на месте (его обрабатывает <see cref="EnsureExistsCore"/>). Вызывается
    /// под lock.
    /// </summary>
    private static void MigrateConfigFromRootCore(string? sourcePath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
            return;
        try
        {
            var fileName = Path.GetFileName(sourcePath);
            if (!string.Equals(fileName, TraceFlagsFormat.ConfigFileName, StringComparison.Ordinal))
                return;

            var targetDir = _profileDataDirectory;
            if (string.IsNullOrWhiteSpace(targetDir))
                return;
            var targetPath = Path.Combine(targetDir, fileName);
            if (string.Equals(targetPath, sourcePath, StringComparison.OrdinalIgnoreCase))
                return;
            if (!File.Exists(sourcePath))
                return;

            // Старый JSONL-журнал не является конфигом флагов — не трогаем (в корне или
            // в профиле его обрабатывает EnsureExistsCore).
            if (TraceFlagsFormat.ShouldMigrateLegacyJsonl(ReadAllTextQuietly(sourcePath)))
                return;

            if (File.Exists(targetPath))
            {
                // Конфиг в профиле уже есть (создан/перенесён в предыдущих запусках),
                // а EnsureExists в этом старте заново создал trace.json в корне каталога
                // данных ДО выбора профиля. Чтение всегда идёт из профильного файла,
                // корневой — мусор рядом с profiles.json (issue #349): удаляем дубль,
                // чтобы он не накапливался при каждом запуске. Имя строго trace.json —
                // журналы (trace_menuclose.json и legacy-файлы) не затрагиваются.
                TryDeleteQuietly(sourcePath);
                return;
            }

            Directory.CreateDirectory(targetDir);
            File.Move(sourcePath, targetPath);
        }
        catch
        {
            // Миграция не должна ломать запуск: файл останется в прежнем расположении.
        }
    }

    /// <summary>Тихое удаление файла; ошибки игнорируются — диагностика не должна
    /// ломать запуск (issue #349).</summary>
    private static void TryDeleteQuietly(string path)
    {
        try { File.Delete(path); }
        catch { /* файл останется — это не критично */ }
    }

    /// <summary>
    /// Синхронизирует кэш с текущим путём конфига после смены каталога
    /// (<see cref="SetProfileDataDirectory"/>): если файл был создан приложением в этом старте
    /// и не менялся (в т.ч. перенесён в профиль) — сохраняем состояние «флагов нет» (env
    /// остаётся рабочим способом включения); иначе сбрасываем кэш для перечитывания с диска.
    /// </summary>
    private static void SyncCacheToCurrentPath()
    {
        var path = ConfigFilePath();
        if (path is null || !File.Exists(path))
        {
            ResetCacheCore();
            return;
        }

        var mtime = File.GetLastWriteTimeUtc(path);
        if (_configCreatedByUs && _lastMtimeUtc == mtime)
        {
            // Файл создан нами автоматически и пользователь его ещё не правил:
            // флагов в конфиге фактически нет (дефолты не считаем явным выбором).
            _initialized = true;
            _flags = TraceFlagsFormat.EmptyFlags;
        }
        else
        {
            ResetCacheCore();
        }
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
                // (issue #347): журнал переезжает в logs/trace_menuclose.json, а trace.json становится конфигом.
                if (TraceFlagsFormat.ShouldMigrateLegacyJsonl(ReadAllTextQuietly(path)))
                {
                    var legacyPath = Path.Combine(
                        Path.GetDirectoryName(path)!, TraceFlagsFormat.LegacyJsonlBackupFileName);
                    File.Move(path, legacyPath, overwrite: true);
                }
            }

            // Конфиг создаётся при каждом старте, если отсутствует (в т.ч. после миграции).
            if (!File.Exists(path))
            {
                File.WriteAllText(path, TraceFlagsFormat.SerializeDefaults(), Encoding.UTF8);

                // Файл создан приложением автоматически: пока пользователь не правил его,
                // считаем, что флагов в конфиге НЕТ — env-переменная остаётся запасным
                // способом включения (issue #347: «нет файла + env=1 → включено»).
                _configCreatedByUs = true;
                _initialized = true;
                _lastMtimeUtc = File.GetLastWriteTimeUtc(path);
                _flags = TraceFlagsFormat.EmptyFlags;
            }
            // Файл существовал до нас (его правил пользователь): оставляем кэш как есть —
            // при необходимости ReloadIfChangedCore перечитает его по изменению mtime.
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
        // Приоритет: каталог активного профиля (выбран через SetProfileDataDirectory) →
        // override тестов → корень каталога данных. После выбора профиля конфиг живёт
        // ТОЛЬКО в профильном каталоге (рядом с settings.json), override тестов при этом
        // уступает — иначе миграция в профиль не имела бы эффекта.
        var dir = _profileDataDirectory ?? _configDirectoryOverride ?? PlatformPaths.AppDataDirectory;
        return string.IsNullOrWhiteSpace(dir) ? null : Path.Combine(dir, TraceFlagsFormat.ConfigFileName);
    }
}