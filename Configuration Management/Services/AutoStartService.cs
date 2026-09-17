#if WINDOWS
using System;
using System.IO;
using Microsoft.Win32;

namespace Configuration_Management.Services;

/// <summary>
/// Автозапуск при старте Windows (функция №31 дорожной карты): запись пути к приложению
/// в ключ реестра <c>HKCU\Software\Microsoft\Windows\CurrentVersion\Run</c>. Используется
/// только раздел <c>HKCU</c>, поэтому работает для текущего пользователя без прав
/// администратора. На Linux/Avalonia используется парная реализация
/// <see cref="AutoStartService.Avalonia"/> (автозапуск десктоп-окружения).
/// </summary>
public sealed class AutoStartService : IAutoStartService
{
    /// <summary>Путь к ключу реестра «Run» текущего пользователя.</summary>
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    /// <summary>Имя значения реестра с путём к приложению.</summary>
    private const string ValueName = "ConfigurationManagement";

    /// <inheritdoc />
    public bool IsAvailable => true;

    /// <summary>Путь к текущему исполняемому файлу приложения (в т.ч. для single-file publish).</summary>
    private static string ExecutablePath =>
        Environment.ProcessPath
        ?? Path.Combine(AppContext.BaseDirectory, "ConfigurationManagement.exe");

    /// <inheritdoc />
    public bool IsEnabled()
    {
        try
        {
            using var runKey = Registry.CurrentUser.OpenSubKey(RunKeyPath);
            if (runKey is null)
                return false;
            return !string.IsNullOrWhiteSpace(runKey.GetValue(ValueName) as string);
        }
        catch
        {
            return false;
        }
    }

    /// <inheritdoc />
    public void Enable()
    {
        try
        {
            using var runKey = Registry.CurrentUser.CreateSubKey(RunKeyPath);
            runKey?.SetValue(ValueName, $"\"{ExecutablePath}\"");
        }
        catch
        {
            // Реестр может быть недоступен — не роняем приложение.
        }
    }

    /// <inheritdoc />
    public void Disable()
    {
        try
        {
            using var runKey = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            runKey?.DeleteValue(ValueName, throwOnMissingValue: false);
        }
        catch
        {
            // Реестр может быть недоступен — не роняем приложение.
        }
    }
}
#endif