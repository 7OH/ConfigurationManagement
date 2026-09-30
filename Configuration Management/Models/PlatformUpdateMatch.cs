namespace Configuration_Management.Models;

/// <summary>
/// Строка окна «Автообновление платформы»: версия из объединения установленных
/// и доступных версий платформы 1С.
/// </summary>
public sealed class PlatformUpdateMatch
{
    /// <summary>Номер версии, например «8.3.27.2214».</summary>
    public string Version { get; init; } = string.Empty;

    /// <summary>True — версия установлена на этой машине.</summary>
    public bool IsInstalled { get; init; }

    /// <summary>True — установленная версия, для которой в каталоге есть более новая
    /// (признак «доступно обновление»). Для не установленных версий — false.</summary>
    public bool HasUpdate { get; init; }

    /// <summary>Новейшая доступная версия (для установленной строки), либо null,
    /// если обновления нет или версия только доступна в каталоге.</summary>
    public string? AvailableVersion { get; init; }
}