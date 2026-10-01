using System;
using System.Collections.Generic;
using System.Linq;
using Configuration_Management.Models;

namespace Configuration_Management.Services;

/// <summary>
/// Тип дистрибутива для скачивания нужной версии платформы 1С (issue #330).
/// «Авто» — рекомендуемый для текущей ОС: на Windows полный клиент (zip), на Linux —
/// пакет .deb/.rpm, при их отсутствии — универсальный .tar.gz.
/// </summary>
public enum PlatformDownloadType
{
    /// <summary>Рекомендуемый дистрибутив для текущей операционной системы.</summary>
    Auto,

    /// <summary>Полный клиент Windows (zip с setup.exe).</summary>
    Client,

    /// <summary>Тонкий клиент Windows (zip с «thin» в имени файла).</summary>
    ThinClient,

    /// <summary>Пакет .deb/.rpm для Linux.</summary>
    Package,

    /// <summary>Универсальный дистрибутив .tar.gz.</summary>
    Archive,
}

/// <summary>
/// Чистый выбор файла дистрибутива версии платформы 1С под ОС, разрядность и тип
/// (issue #330): фильтрация файлов релиза каталога <c>releases.1c.ru</c> по типу
/// дистрибутива и приоритет подходящей разрядности. Не выполняет сетевых запросов
/// и не зависит от UI — покрывается юнит-тестами (маппинг «версия+разрядность → файл»).
/// </summary>
public static class PlatformDistributionPicker
{
    /// <summary>Разрядность дистрибутива: 64-битная.</summary>
    public const string Arch64 = "x64";

    /// <summary>Разрядность дистрибутива: 32-битная.</summary>
    public const string Arch32 = "x86";

    /// <summary>
    /// Выбирает файл дистрибутива из списка файлов релиза по типу и разрядности.
    /// Приоритет: файлы подходящего типа (см. <paramref name="type"/>), затем файлы
    /// нужной разрядности (x64 для 64-битной ОС, x86 для 32-битной), затем первый
    /// из оставшихся. Пустой список или отсутствие подходящего файла — null.
    /// </summary>
    /// <param name="files">Файлы дистрибутива релиза.</param>
    /// <param name="is64Bit">True — целевая разрядность x64, false — x86.</param>
    /// <param name="type">Тип дистрибутива; <see cref="PlatformDownloadType.Auto"/> —
    /// по умолчанию для <paramref name="isWindows"/>.</param>
    /// <param name="isWindows">True — целевая ОС Windows (нужен для режима Auto).</param>
    public static PlatformReleaseFile? PickFile(
        IReadOnlyList<PlatformReleaseFile> files, bool is64Bit, PlatformDownloadType type, bool isWindows)
    {
        if (files is null || files.Count == 0)
            return null;

        var filtered = FilterByType(files, type, isWindows);
        if (filtered.Count == 0)
            return null;

        // 1) Файл нужной разрядности; 2) файл без разрядности; 3) первый любой.
        var arch = is64Bit ? Arch64 : Arch32;
        return filtered.FirstOrDefault(f => string.Equals(f.Architecture, arch, StringComparison.OrdinalIgnoreCase))
            ?? filtered.FirstOrDefault(f => string.IsNullOrWhiteSpace(f.Architecture))
            ?? filtered[0];
    }

    /// <summary>
    /// Типы дистрибутивов, доступных в списке файлов: на Windows — «Полный клиент»
    /// и/или «Тонкий клиент» (при наличии файлов с «thin» в имени); на Linux —
    /// «Пакет» (deb/rpm) и/или «Архив» (tar.gz). Всегда включает «Авто», если есть
    /// хоть один подходящий файл. Список сохраняет порядок значений enum.
    /// </summary>
    public static IReadOnlyList<PlatformDownloadType> AvailableTypes(
        IReadOnlyList<PlatformReleaseFile> files, bool isWindows)
    {
        if (files is null || files.Count == 0)
            return Array.Empty<PlatformDownloadType>();

        var result = new List<PlatformDownloadType>();
        foreach (var type in Enum.GetValues<PlatformDownloadType>())
        {
            if (type == PlatformDownloadType.Auto)
                continue;
            if (FilterByType(files, type, isWindows).Count > 0)
                result.Add(type);
        }

        if (result.Count > 0)
            result.Insert(0, PlatformDownloadType.Auto);
        return result;
    }

    /// <summary>True — файл тонкого клиента Windows (имя содержит токен «thin»).</summary>
    public static bool IsThinClient(PlatformReleaseFile file)
    {
        var name = file?.FileName ?? string.Empty;
        return name.Contains("thin", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Отображаемое имя типа дистрибутива (ключ локализации).</summary>
    public static string TypeLocalizationKey(PlatformDownloadType type) => type switch
    {
        PlatformDownloadType.Auto => "PlatformDownload.Type.Auto",
        PlatformDownloadType.Client => "PlatformDownload.Type.Client",
        PlatformDownloadType.ThinClient => "PlatformDownload.Type.ThinClient",
        PlatformDownloadType.Package => "PlatformDownload.Type.Package",
        PlatformDownloadType.Archive => "PlatformDownload.Type.Archive",
        _ => "PlatformDownload.Type.Auto",
    };

    /// <summary>Фильтр файлов по типу дистрибутива (для Auto — по целевой ОС).</summary>
    private static List<PlatformReleaseFile> FilterByType(
        IReadOnlyList<PlatformReleaseFile> files, PlatformDownloadType type, bool isWindows)
    {
        return type switch
        {
            PlatformDownloadType.Client =>
                files.Where(f => f.Kind == PlatformDistributionKind.WindowsSetupZip && !IsThinClient(f)).ToList(),
            PlatformDownloadType.ThinClient =>
                files.Where(f => f.Kind == PlatformDistributionKind.WindowsSetupZip && IsThinClient(f)).ToList(),
            PlatformDownloadType.Package =>
                files.Where(f => f.Kind is PlatformDistributionKind.LinuxDeb or PlatformDistributionKind.LinuxRpm).ToList(),
            PlatformDownloadType.Archive =>
                files.Where(f => f.Kind == PlatformDistributionKind.LinuxTarGz).ToList(),
            _ => AutoFiles(files, isWindows),
        };
    }

    /// <summary>Рекомендуемые файлы для ОС: Windows — zip-клиенты (полный и тонкий),
    /// Linux — пакеты deb/rpm, при их отсутствии — tar.gz.</summary>
    private static List<PlatformReleaseFile> AutoFiles(IReadOnlyList<PlatformReleaseFile> files, bool isWindows)
    {
        if (isWindows)
        {
            var zips = files.Where(f => f.Kind == PlatformDistributionKind.WindowsSetupZip).ToList();
            if (zips.Count == 0)
                return zips;
            var full = zips.Where(f => !IsThinClient(f)).ToList();
            return full.Count > 0 ? full : zips;
        }

        var packages = files
            .Where(f => f.Kind is PlatformDistributionKind.LinuxDeb or PlatformDistributionKind.LinuxRpm)
            .ToList();
        if (packages.Count > 0)
            return packages;

        return files.Where(f => f.Kind == PlatformDistributionKind.LinuxTarGz).ToList();
    }
}