using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Configuration_Management.Services;

/// <summary>
/// Диагностическая трассировка клика, закрывающего контекстное меню дерева
/// (issue #340, девятая попытка). Пишется ВСЕГДА (без env-гейта) в файл
/// <c>trace.json</c> РЯДОМ с настройками приложения — в каталоге
/// <see cref="PlatformPaths.AppDataDirectory"/> (Windows: %APPDATA%\ConfigurationManagement\,
/// Linux: ~/.config/ConfigurationManagement/), тот же каталог, что и settings.json.
/// Имя файла — по соглашению с пользователем (0.3.9.308): основной <c>trace.json</c>;
/// legacy <c>menuclose_trace.json</c> от 0.3.9.306 продолжает дописываться, если уже
/// существует (непрерывность диагностики, см. <see cref="ResolvePath"/>).
/// Формат — JSON Lines: одна JSON-запись на строку (валидный JSON, ключи латиницей).
/// При превышении ~512 КБ файл усекается по кругу: остаётся хвост последних записей
/// и первой строкой дописывается маркер <c>{"event":"truncated","ts":...}</c>.
/// Файл создаётся при КАЖДОМ старте приложения через <see cref="EnsureStarted"/>
/// (startup-запись пишется вне зависимости от действий пользователя), а не только
/// при первом событии меню, как было в 0.3.9.306. Запись под lock;
/// ошибки записи игнорируются — трассировка не должна влиять на работу приложения.
/// Общий для WPF и Avalonia.
/// </summary>
public static class MenuCloseTrace
{
    /// <summary>Основное имя файла трассировки (JSONL; расширение .json по просьбе пользователя).</summary>
    public const string FileName = MenuCloseTraceFormat.PrimaryFileName;

    /// <summary>Прежнее имя файла трассировки 0.3.9.306 (дописывается при наличии).</summary>
    public const string LegacyFileName = MenuCloseTraceFormat.LegacyFileName;

    private static readonly object Lock = new();
    private static string? _tracePath;
    private static bool _startupWritten;

    /// <summary>
    /// Гарантирует создание файла трассировки при старте приложения (issue #340, 0.3.9.308):
    /// startup-запись пишется БЕЗ какого-либо события меню — чтобы пользователь всегда видел
    /// файл рядом с настройками и мог убедиться, что диагностика активна (в 0.3.9.306 файл
    /// не появлялся, т.к. startup-запись выполнялась только внутри <see cref="Log"/>).
    /// Вызывается из конструктора/OnLoaded главного окна (WPF и Avalonia). Идемпотентна.
    /// </summary>
    public static void EnsureStarted()
    {
        try
        {
            lock (Lock)
            {
                WriteStartupIfNeeded(ResolvePath());
            }
        }
        catch
        {
            // Трассировка не должна влиять на работу приложения.
        }
    }

    /// <summary>
    /// Пишет одну запись в trace.json (или legacy menuclose_trace.json при его наличии).
    /// Сигнатура сохранена прежней — все существующие вызовы НЕ меняются; текст сообщения
    /// сохраняется в <c>data.message</c>. Ошибки записи игнорируются.
    /// </summary>
    public static void Log(string message)
    {
        try
        {
            lock (Lock)
            {
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
    /// Полный путь к файлу трассировки (в каталоге данных приложения). Выбор имени
    /// (issue #340, 0.3.9.308): если рядом с настройками уже существует legacy-файл
    /// <c>menuclose_trace.json</c> (от 0.3.9.306) — журнал дописывается в него
    /// (непрерывность диагностики); иначе — основной <c>trace.json</c>. Путь
    /// кэшируется на время сессии.
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
    /// первой записи; при превышении лимита (≈512 КБ) содержимое усекается до хвоста
    /// с маркером <c>truncated</c>, затем дописывается новая строка. Бюджет усечения
    /// учитывает размер дописываемой строки, чтобы итоговый объём не выходил за лимит.
    /// Вызывается только под <see cref="Lock"/>.
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