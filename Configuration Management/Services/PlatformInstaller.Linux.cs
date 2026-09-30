#if LINUX
using Configuration_Management.Models;

namespace Configuration_Management.Services;

/// <summary>
/// Установка дистрибутива технологической платформы 1С:Предприятие на Linux.
/// Автоматическая установка с правами root из GUI безопасно не выполняется:
/// окно показывает готовую команду sudo (кнопка «Скопировать команду») и
/// человекочитаемую инструкцию; после ручной установки пользователь запускает
/// «Проверить снова» — <see cref="RefreshInstalledCache"/> пересканирует
/// файловую систему. Чистая логика команд живёт в
/// <see cref="PlatformInstallerCommands"/> (обе платформы); здесь — только
/// платформенная часть (инструкция, пересканирование) и точки вызова.
/// Файл собирается только на Linux (<c>#if LINUX</c>); Windows-реализация —
/// <c>PlatformInstaller.Windows.cs</c>.
/// </summary>
public static class PlatformInstaller
{
    /// <summary>Ключ локализации: команда скопирована в буфер обмена.</summary>
    public const string SudoCommandCopied = "PlatformUpdate.Linux.Copied";

    /// <summary>
    /// Тип пакета по имени файла (делегирование в
    /// <see cref="PlatformInstallerCommands.DetectPackageType"/>).
    /// </summary>
    public static PackageType DetectPackageType(string? fileName)
        => PlatformInstallerCommands.DetectPackageType(fileName);

    /// <summary>
    /// Команда установки с sudo для пользователя (делегирование в
    /// <see cref="PlatformInstallerCommands.BuildSudoInstallCommand"/>).
    /// </summary>
    public static string BuildSudoInstallCommand(string? packagePath)
        => PlatformInstallerCommands.BuildSudoInstallCommand(packagePath);

    /// <summary>
    /// Команда удаления версии с sudo (делегирование в
    /// <see cref="PlatformInstallerCommands.BuildSudoUninstallCommand"/>).
    /// </summary>
    public static string BuildSudoUninstallCommand(string? version)
        => PlatformInstallerCommands.BuildSudoUninstallCommand(version);

    /// <summary>
    /// Человекочитаемая инструкция установки дистрибутива: многострочный текст
    /// с готовой командой sudo (для .deb/.rpm) или шагами распаковки и запуска
    /// ./install (для .tar.gz) и пояснением, что команды выполняются в терминале
    /// и требуют прав root. Чистая функция (без сети и файловой системы) —
    /// строится по файлу релиза каталога платформы.
    /// </summary>
    public static string BuildInstallInstruction(PlatformReleaseFile file)
    {
        var fileName = file?.FileName ?? string.Empty;
        var type = PlatformInstallerCommands.DetectPackageType(fileName);

        switch (type)
        {
            case PackageType.Dpkg:
                return "Установка пакета .deb\n\n" +
                       $"Выполните в терминале команду:\n{PlatformInstallerCommands.BuildSudoInstallCommand(fileName)}\n\n" +
                       "Команда требует прав root (sudo запросит пароль). Перед выполнением " +
                       "перейдите в каталог со скачанным файлом.";

            case PackageType.Rpm:
                return "Установка пакета .rpm\n\n" +
                       $"Выполните в терминале команду:\n{PlatformInstallerCommands.BuildSudoInstallCommand(fileName)}\n\n" +
                       "Команда требует прав root (sudo запросит пароль). Перед выполнением " +
                       "перейдите в каталог со скачанным файлом.";

            case PackageType.TarGz:
                return "Установка дистрибутива .tar.gz\n\n" +
                       $"1. Распакуйте архив в терминале: tar -xzf '{fileName}'\n" +
                       "2. Выполните в каталоге распаковки: ./install (или ./install_server)\n\n" +
                       "Шаги требуют прав root — при необходимости используйте sudo.";

            default:
                return "Тип пакета не определён.\n\n" +
                       "Установите дистрибутив платформы 1С вручную из терминала с правами root.";
        }
    }

    /// <summary>
    /// Пересканирует установленные версии платформы. Отдельного кэша нет —
    /// список всегда читается из файловой системы через
    /// <see cref="PlatformVersionService.FindInstalledVersionInfos"/>; метод
    /// служит явной точкой вызова из UI («Проверить снова») после ручной
    /// установки пакета.
    /// </summary>
    public static void RefreshInstalledCache()
    {
        PlatformVersionService.FindInstalledVersionInfos();
    }
}
#endif