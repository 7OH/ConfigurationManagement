using System;

namespace Configuration_Management.Services;

/// <summary>
/// Тип пакета дистрибутива технологической платформы 1С на Linux,
/// определяемый по расширению имени файла.
/// </summary>
public enum PackageType
{
    /// <summary>Пакет .deb (Debian/Ubuntu и производные).</summary>
    Dpkg,

    /// <summary>Пакет .rpm (Red Hat/Fedora/SUSE и производные).</summary>
    Rpm,

    /// <summary>Универсальный дистрибутив .tar.gz/.tgz (установка вручную).</summary>
    TarGz,

    /// <summary>Неизвестный тип пакета.</summary>
    Other
}

/// <summary>
/// Чистые команды установки и удаления дистрибутива технологической платформы
/// 1С на Linux: определение типа пакета по имени файла и сборка команд sudo
/// для пользователя. Класс без платформенных зависимостей (без директив
/// условной компиляции) — собирается на обеих платформах и покрывается тестами
/// на любом прогоне (в т.ч. Windows). Платформенная часть (человекочитаемая
/// инструкция, пересканирование установленных версий) живёт в
/// <c>PlatformInstaller.Linux.cs</c> (#if LINUX).
/// </summary>
public static class PlatformInstallerCommands
{
    /// <summary>
    /// Определяет тип пакета по расширению имени файла (регистронезависимо):
    /// .deb → <see cref="PackageType.Dpkg"/>, .rpm → <see cref="PackageType.Rpm"/>,
    /// .tar.gz/.tgz → <see cref="PackageType.TarGz"/>, прочее (в т.ч. пустое
    /// имя) — <see cref="PackageType.Other"/>. Чистая функция.
    /// </summary>
    public static PackageType DetectPackageType(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return PackageType.Other;

        var name = fileName.Trim().ToLowerInvariant();

        if (name.EndsWith(".deb", StringComparison.Ordinal))
            return PackageType.Dpkg;
        if (name.EndsWith(".rpm", StringComparison.Ordinal))
            return PackageType.Rpm;
        if (name.EndsWith(".tar.gz", StringComparison.Ordinal) ||
            name.EndsWith(".tgz", StringComparison.Ordinal))
            return PackageType.TarGz;

        return PackageType.Other;
    }

    /// <summary>
    /// Экранирует путь для встраивания в одинарные кавычки shell: внутренние
    /// апострофы «'» заменяются на «'\''» (закрыть кавычку, литеральный
    /// апостроф, открыть кавычку заново). Чистая функция.
    /// </summary>
    private static string EscapeShellPath(string packagePath)
        => (packagePath ?? string.Empty).Replace("'", "'\\''");

    /// <summary>
    /// Собирает команду установки пакета с sudo для пользователя:
    /// «sudo dpkg -i '<путь>'» для .deb, «sudo dnf install -y '<путь>'»
    /// для .rpm; путь заключается в одинарные кавычки, внутренние апострофы
    /// экранируются. Для .tar.gz возвращает короткую инструкцию (распаковка +
    /// выполнение ./install — команда требует root); для неизвестного типа —
    /// подсказку об установке вручную. Чистая функция.
    /// </summary>
    public static string BuildSudoInstallCommand(string? packagePath)
    {
        var path = packagePath ?? string.Empty;

        switch (DetectPackageType(path))
        {
            case PackageType.Dpkg:
                return $"sudo dpkg -i '{EscapeShellPath(path)}'";

            case PackageType.Rpm:
                return $"sudo dnf install -y '{EscapeShellPath(path)}'";

            case PackageType.TarGz:
                return "Распакуйте архив и выполните ./install — команда требует прав root (sudo).";

            default:
                return "Тип пакета не определён — установите дистрибутив платформы вручную.";
        }
    }

    /// <summary>
    /// Собирает команду удаления версии платформы:
    /// «sudo dpkg -r 1c-enterprise83-<версия>». Запасной вариант
    /// «sudo rm -rf /opt/1cv8/<версия>» рассматривается на этапе удаления
    /// старых версий (0.3.9.215). Чистая функция.
    /// </summary>
    public static string BuildSudoUninstallCommand(string? version)
    {
        var clean = (version ?? string.Empty).Trim();
        return $"sudo dpkg -r 1c-enterprise83-{clean}";
    }
}