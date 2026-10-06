using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Configuration_Management.Services;

/// <summary>
/// Диагностическая трассировка клика, закрывающего контекстное меню дерева
/// (issue #340, девятая попытка; issue #347 «Сказ о trace.json» — с 0.3.9.315 пишется
/// ТОЛЬКО при включённом флаге <c>CM_MENUCLOSE</c> в конфиге <c>trace.json</c>, псевдоним —
/// <c>CM_MENUCLICK</c>). Журнал лежит в <c>trace_menuclose.jsonl</c> РЯДОМ с настройками
/// приложения — в каталоге <see cref="PlatformPaths.AppDataDirectory"/> (Windows:
/// %APPDATA%\ConfigurationManagement\, Linux: ~/.config/ConfigurationManagement/),
/// тот же каталог, что settings.json и конфиг флагов.
/// Имя файла — по соглашению с пользователем (0.3.9.308–0.3.9.314 это был основной
/// <c>trace.json</c>; с 0.3.9.315 — <c>trace_menuclose.jsonl</c>, чтобы не конфликтовать
/// с конфигом); legacy <c>menuclose_trace.json</c> от 0.3.9.306 продолжает дописываться,
/// если уже существует (непрерывность диагностики, см. <see cref="ResolvePath"/>).
/// Формат — JSON Lines: одна JSON-запись на строку (валидный JSON, ключи латиницей).
/// При превышении ~1 МБ (0.3.9.311: лимит увеличен с 512 КБ) файл усекается по кругу:
/// остаётся хвост последних записей и первой строкой дописывается маркер
/// <c>{"event":"truncated","ts":...}</c>.
/// <see cref="EnsureStarted"/> всегда гарантирует наличие конфига флагов <c>trace.json</c>,
/// но startup-запись пишется только при включённом флаге. Запись под lock;
/// ошибки записи игнорируются — трассировка не должна влиять на работу приложения.
/// Общий для WPF и Avalonia.
/// </summary>
public static class MenuCloseTrace
{
    /// <summary>Основное имя журнала событий меню (JSONL; issue #347 — отдельно от конфига trace.json).</summary>
    public const string FileName = MenuCloseTraceFormat.PrimaryFileName;

    /// <summary>Имя конфига отладочных флагов (trace.json; issue #347).</summary>
    public const string ConfigFileName = MenuCloseTraceFormat.ConfigFileName;

    /// <summary>Прежнее имя файла трассировки 0.3.9.306 (дописывается при наличии).</summary>
    public const string LegacyFileName = MenuCloseTraceFormat.LegacyFileName;

    private static readonly object Lock = new();
    private static string? _tracePath;
    private static bool _startupWritten;

    /// <summary>
    /// Гарантирует наличие конфига флагов <c>trace.json</c> при старте приложения (issue #347):
    /// сам конфиг создаётся всегда (даже при выключенном флаге — пользователь видит файл
    /// рядом с настройками), а startup-запись журнала выполняется ТОЛЬКО при включённом
    /// <c>CM_MENUCLOSE</c>/<c>CM_MENUCLICK</c>. В 0.3.9.308–0.3.9.314 запись была безусловной —
    /// «логи капали» у всех пользователей, теперь журнал ведётся по запросу из тикета.
    /// Вызывается из конструктора/OnLoaded главного окна (WPF и Avalonia). Идемпотентна.
    /// </summary>
    public static void EnsureStarted()
    {
        try
        {
            lock (Lock)
            {
                // Конфиг флагов гарантированно существует при каждом старте (issue #347),
                // даже если журнал меню выключен.
                TraceFlags.EnsureExists();
                if (!TraceFlags.IsMenuEnabled())
                    return;
                WriteStartupIfNeeded(ResolvePath());
            }
        }
        catch
        {
            // Трассировка не должна влиять на работу приложения.
        }
    }

    /// <summary>
    /// Пишет одну запись в журнал меню (trace_menuclose.jsonl, либо legacy menuclose_trace.json
    /// при его наличии) — ТОЛЬКО при включённом флаге <c>CM_MENUCLOSE</c> или его псевдониме
    /// <c>CM_MENUCLICK</c> (issue #347; при выключенном флаге — no-op, файл не растёт).
    /// Сигнатура сохранена прежней — все существующие вызовы НЕ меняются; текст сообщения
    /// сохраняется в <c>data.message</c>. Ошибки записи игнорируются.
    /// </summary>
    public static void Log(string message)
    {
        try
        {
            lock (Lock)
            {
                if (!TraceFlags.IsMenuEnabled())
                    return;
                var path = ResolvePath();
                WriteStartupIfNeeded(path);
                var line = MenuCloseTraceFormat.BuildLine(
                    "log",
                    DateTimeOffset.Now,
                    Environment.CurrentManagedThreadId,
                    new Dictionary<string, object?> { ["message"] = message });
                AppendWithTrim(path, line);
            }
        }
        catch
        {
            // Трассировка не должна влиять на работу приложения.
        }
    }

    /// <summary>
    /// Полный путь к журналу (в каталоге данных приложения). Выбор имени
    /// (issue #340, 0.3.9.308): если рядом с настройками уже существует legacy-файл
    /// <c>menuclose_trace.json</c> (от 0.3.9.306) — журнал дописывается в него
    /// (непрерывность диагностики); иначе — основной <c>trace_menuclose.jsonl</c> (issue #347).
    /// Путь кэшируется на время сессии.
    /// </summary>
    private static string ResolvePath()
    {
        if (_tracePath is not null)
            return _tracePath;
        var dir = PlatformPaths.AppDataDirectory;
        var legacy = Path.Combine(dir, LegacyFileName);
        _tracePath = File.Exists(legacy)
            ? legacy
            : Path.Combine(dir, MenuCloseTraceFormat.ResolveFileName(legacyExists: false));
        return _tracePath;
    }

    /// <summary>
    /// Startup-запись при ПЕРВОМ обращении к трассировке (план 0.3.9.306, 2.3.1):
    /// версия (InformationalVersion), платформа (WPF/Avalonia), ОС, каталог данных,
    /// время. Флаг взводится только после успешной записи.
    /// </summary>
    private static void WriteStartupIfNeeded(string path)
    {
        if (_startupWritten)
            return;
        var startup = MenuCloseTraceFormat.BuildStartupLine(
            VersionInfo.Display(),
            PlatformName,
            RuntimeInformation.OSDescription,
            PlatformPaths.AppDataDirectory,
            DateTimeOffset.Now,
            Environment.CurrentManagedThreadId);
        AppendWithTrim(path, startup);
        _startupWritten = true;
    }

    /// <summary>Платформа UI для startup-записи: WPF (Windows) / Avalonia (Linux).</summary>
    private static string PlatformName
    {
        get
        {
#if LINUX
            return "Avalonia";
#else
            return "WPF";
#endif
        }
    }

    /// <summary>
    /// Дописывает JSONL-строку в файл с круговым усечением: каталог создаётся при
    /// первой записи; при превышении лимита (≈1 МБ, 0.3.9.311, B-7) содержимое
    /// усекается до хвоста с маркером <c>truncated</c>, затем дописывается новая
    /// строка. Бюджет усечения учитывает размер дописываемой строки, чтобы итоговый
    /// объём не выходил за лимит. Вызывается только под <see cref="Lock"/>.
    /// </summary>
    private static void AppendWithTrim(string path, string jsonLine)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var lineBytes = Encoding.UTF8.GetByteCount(jsonLine) + 1; // + '\n'
        var existingBytes = File.Exists(path) ? new FileInfo(path).Length : 0;
        if (existingBytes + lineBytes <= MenuCloseTraceFormat.MaxFileBytes)
        {
            File.AppendAllText(path, jsonLine + "\n", Encoding.UTF8);
            return;
        }

        var content = File.ReadAllText(path, Encoding.UTF8);
        var truncated = MenuCloseTraceFormat.TruncateToLimit(
            content, MenuCloseTraceFormat.MaxFileBytes - lineBytes, DateTimeOffset.Now);
        File.WriteAllText(path, truncated, Encoding.UTF8);
        File.AppendAllText(path, jsonLine + "\n", Encoding.UTF8);
    }
}