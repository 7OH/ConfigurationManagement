#if LINUX
using System;
using System.IO;

namespace Configuration_Management.Services;

/// <summary>
/// Автозапуск при старте десктоп-окружения для Linux/Avalonia (функция №31 дорожной
/// карты): создание файла автозапуска <c>~/.config/autostart/configuration-management.desktop</c>.
/// Каталог <c>~/.config</c> берётся через <see cref="Environment.SpecialFolder.ApplicationData"/>,
/// который на Linux соответствует <c>$XDG_CONFIG_HOME</c> (или <c>~/.config</c> по умолчанию).
/// На Windows/WPF используется парная реализация на реестре <c>HKCU\...\Run</c>.
/// </summary>
public sealed class AutoStartService : IAutoStartService
{
    /// <summary>Имя файла автозапуска в каталоге <c>autostart</c>.</summary>
    private const string FileName = "configuration-management.desktop";

    /// <inheritdoc />
    public bool IsAvailable => true;

    /// <summary>Каталог автозапуска десктоп-окружения (<c>~/.config/autostart</c>).</summary>
    private static string AutostartDir =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "autostart");

    /// <summary>Полный путь к файлу автозапуска.</summary>
    private static string DesktopFilePath => Path.Combine(AutostartDir, FileName);

    /// <summary>Путь к текущему исполняемому файлу приложения.</summary>
    private static string ExecutablePath =>
        Environment.ProcessPath ?? AppContext.BaseDirectory;

    /// <inheritdoc />
    public bool IsEnabled() => File.Exists(DesktopFilePath);

    /// <inheritdoc />
    public void Enable()
    {
        try
        {
            Directory.CreateDirectory(AutostartDir);
            var content =
                "[Desktop Entry]\n" +
                "Type=Application\n" +
                "Version=1.0\n" +
                "Name=Управление конфигурациями 1С\n" +
                $"Exec=\"{ExecutablePath}\"\n" +
                "Terminal=false\n" +
                "X-GNOME-Autostart-enabled=true\n";
            File.WriteAllText(DesktopFilePath, content);
        }
        catch
        {
            // Каталог может быть недоступен — не роняем приложение.
        }
    }

    /// <inheritdoc />
    public void Disable()
    {
        try
        {
            if (File.Exists(DesktopFilePath))
                File.Delete(DesktopFilePath);
        }
        catch
        {
            // Файл может быть защищён от записи — не роняем приложение.
        }
    }
}
#endif