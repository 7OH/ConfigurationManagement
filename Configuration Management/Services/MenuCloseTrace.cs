using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Configuration_Management.Services;

/// <summary>
/// Диагностическая трассировка клика, закрывающего контекстное меню дерева
/// (issue #340, восьмая попытка). Пишется ВСЕГДА (без env-гейта) в файл
/// <c>menuclose_trace.json</c> РЯДОМ с настройками приложения — в каталоге
/// <see cref="PlatformPaths.AppDataDirectory"/> (Windows: %APPDATA%\ConfigurationManagement\,
/// Linux: ~/.config/ConfigurationManagement/), тот же каталог, что и settings.json.
/// Формат — JSON Lines: одна JSON-запись на строку (валидный JSON, ключи латиницей).
/// При превышении ~512 КБ файл усекается по кругу: остаётся хвост последних записей
/// и первой строкой дописывается маркер <c>{"event":"truncated","ts":...}</c>.
/// При первом обращении пишется startup-запись (версия приложения из
/// InformationalVersion, платформа WPF/Avalonia, ОС, время). Запись под lock;
/// ошибки записи игнорируются — трассировка не должна влиять на работу приложения.
/// Общий для WPF и Avalonia.
/// </summary>
public static class MenuCloseTrace
{
    /// <summary>Имя файла трассировки (содержимое — JSONL; расширение .json по просьбе пользователя).</summary>
    public const string FileName = "menuclose_trace.json";

    private static readonly object Lock = new();
    private static string? _tracePath;
    private static bool _startupWritten;

    /// <summary>
    /// Пишет одну запись в menuclose_trace.json. Сигнатура сохранена прежней —
    /// все существующие вызовы НЕ меняются; текст сообщения сохраняется в
    /// <c>data.message</c>. Ошибки записи игнорируются.
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

    /// <summary>Полный путь к файлу трассировки (в каталоге данных приложения).</summary>
    private static string ResolvePath()
    {
        return _tracePath ??= Path.Combine(PlatformPaths.AppDataDirectory, FileName);
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