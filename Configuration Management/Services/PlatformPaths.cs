using System.IO;

namespace Configuration_Management.Services;

/// <summary>
/// Кроссплатформенные пути к каталогам данных приложения.
/// Windows: %APPDATA%\ConfigurationManagement (Environment.SpecialFolder.ApplicationData).
/// Linux:   ~/.config/ConfigurationManagement (XDG_CONFIG_HOME или ~/.config).
/// Единая точка расчёта каталога данных и каталога логов, чтобы все сервисы
/// (InfobaseRepository, FileAppLogger и др.) писали в одно место.
/// </summary>
public static class PlatformPaths
{
    /// <summary>
    /// Каталог данных приложения (infobases.json, groups.json, settings.json).
    /// В портативном режиме (<see cref="PortablePaths.IsPortable"/>) возвращается каталог
    /// рядом с исполняемым файлом (<see cref="PortablePaths.PortableAppDataDirectory"/>),
    /// что позволяет переносить все настройки вместе со сменным носителем (функция №1
    /// StartManager, Этап 10). Иначе — системный каталог профиля.
    /// </summary>
    public static string AppDataDirectory =>
        PortablePaths.TryResolveDataDirectory() ?? DefaultAppDataDirectory;

    /// <summary>
    /// Системный каталог данных приложения по умолчанию (без учёта портативного режима).
    /// Windows: %APPDATA%\ConfigurationManagement; Linux: ~/.config/ConfigurationManagement.
    /// </summary>
    public static string DefaultAppDataDirectory
    {
        get
        {
#if LINUX
            // На Linux SpecialFolder.ApplicationData уже указывает на ~/.config,
            // но задаём путь явно и корректно обрабатываем XDG_CONFIG_HOME.
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var configHome = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
            var baseDir = !string.IsNullOrWhiteSpace(configHome)
                ? configHome
                : string.IsNullOrEmpty(home)
                    ? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)
                    : Path.Combine(home, ".config");
            return Path.Combine(baseDir, "ConfigurationManagement");
#else
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "ConfigurationManagement");
#endif
        }
    }

    /// <summary>
    /// Каталог логов приложения (файловый логгер FileAppLogger).
    /// </summary>
    public static string LogDirectory => Path.Combine(AppDataDirectory, "logs");
}