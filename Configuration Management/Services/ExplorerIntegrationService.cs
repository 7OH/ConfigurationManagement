#if WINDOWS
using System;
using System.IO;
using Microsoft.Win32;

namespace Configuration_Management.Services;

/// <summary>
/// Интеграция с проводником Windows (функция №12): регистрация ассоциации <c>.1CD</c>
/// и команд контекстного меню в разделе реестра <c>HKCU\Software\Classes\</c>.
/// <para>
/// Команды контекстного меню вызывают исполняемый файл приложения с параметрами:
/// <c>--register "<путь>"</c>, <c>--launch "<путь>"</c>, <c>--designer "<путь>"</c>.
/// </para>
/// <para>
/// Используется только <c>HKCU</c>, поэтому работает для текущего пользователя без
/// прав администратора. На Linux/Avalonia используется заглушка
/// <see cref="ExplorerIntegrationService.Avalonia"/> (<see cref="IExplorerIntegrationService.IsAvailable"/> == false).
/// </para>
/// </summary>
public sealed class ExplorerIntegrationService : IExplorerIntegrationService
{
    /// <summary>Имя ProgID ассоциации <c>.1CD</c>.</summary>
    public const string ProgId = "ConfigurationManagement.1CD";

    /// <summary>Имя команды «Зарегистрировать в списке баз».</summary>
    public const string VerbRegister = "Register";
    /// <summary>Имя команды «Запустить 1С:Предприятие».</summary>
    public const string VerbLaunch = "Launch";
    /// <summary>Имя команды «Запустить Конфигуратор».</summary>
    public const string VerbDesigner = "Designer";

    public bool IsAvailable => true;

    /// <summary>
    /// Путь к текущему исполняемому файлу приложения (в т.ч. для single-file publish).
    /// Оборачивается в кавычки при записи в реестр, поэтому хранится без них.
    /// </summary>
    private static string ExecutablePath =>
        Environment.ProcessPath
        ?? Path.Combine(AppContext.BaseDirectory, "ConfigurationManagement.exe");

    /// <inheritdoc />
    public bool IsRegistered()
    {
        try
        {
            using var classes = Registry.CurrentUser.OpenSubKey(@"Software\Classes");
            if (classes is null)
                return false;

            using var ext = classes.OpenSubKey(".1CD");
            if (ext is null || !string.Equals(ext.GetValue(null) as string, ProgId, StringComparison.OrdinalIgnoreCase))
                return false;

            using var prog = classes.OpenSubKey(ProgId);
            if (prog is null)
                return false;

            // Проверяем наличие всех трёх команд контекстного меню.
            foreach (var verb in new[] { VerbRegister, VerbLaunch, VerbDesigner })
            {
                using var command = prog.OpenSubKey($@"shell\{verb}\command");
                if (command is null || string.IsNullOrWhiteSpace(command.GetValue(null) as string))
                    return false;
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <inheritdoc />
    public void Register()
    {
        using var classes = Registry.CurrentUser.CreateSubKey(@"Software\Classes");
        if (classes is null)
            return;

        // Ассоциация расширения .1CD → ProgID.
        using (var ext = classes.CreateSubKey(".1CD"))
            ext.SetValue(null, ProgId);

        // ProgID: подпись, значок и команды контекстного меню.
        using var prog = classes.CreateSubKey(ProgId);
        prog.SetValue(null, "База 1С");

        using (var icon = prog.CreateSubKey("DefaultIcon"))
            icon.SetValue(null, $"\"{ExecutablePath}\",0");

        WriteVerb(prog, VerbRegister, "Зарегистрировать в списке баз", "--register");
        WriteVerb(prog, VerbLaunch, "Запустить 1С:Предприятие", "--launch");
        WriteVerb(prog, VerbDesigner, "Запустить Конфигуратор", "--designer");
    }

    /// <inheritdoc />
    public void Unregister()
    {
        try
        {
            using var classes = Registry.CurrentUser.CreateSubKey(@"Software\Classes");
            classes?.DeleteSubKeyTree(".1CD", throwOnMissingSubKey: false);
            classes?.DeleteSubKeyTree(ProgId, throwOnMissingSubKey: false);
        }
        catch
        {
            // Реестр может быть недоступен — не роняем приложение.
        }
    }

    /// <summary>
    /// Создаёт пункт контекстного меню: подпапку <c>shell\<verb></c> с отображаемым
    /// именем и подпапку <c>command</c> с командной строкой запуска приложения.
    /// </summary>
    private static void WriteVerb(RegistryKey prog, string verb, string displayName, string arg)
    {
        using var verbKey = prog.CreateSubKey($@"shell\{verb}");
        verbKey.SetValue(null, displayName);
        using var command = verbKey.CreateSubKey("command");
        command.SetValue(null, $"\"{ExecutablePath}\" {arg} \"%1\"");
    }
}
#endif