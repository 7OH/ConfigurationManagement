using System;
using Configuration_Management.Localization;
using Configuration_Management.Models;

namespace Configuration_Management.ViewModels;

/// <summary>
/// Строка списка версий «Обозревателя хранилища конфигурации» (0.3.9.128, этап 2):
/// номер, дата фиксации (локальное время), автор, комментарий и пометка актуальной
/// версии. Чистая модель форматирования поверх <see cref="RepositoryVersion"/> —
/// без платформенных зависимостей (обе платформы, WPF и Avalonia).
/// Служебная запись «актуальная версия» (Number = -1, см. <c>RepositoryStorageService.GetHistoryAsync</c>)
/// отображается как «акт.» с пометкой «●».
/// </summary>
public sealed class RepositoryVersionRow
{
    private readonly RepositoryVersion _version;

    public RepositoryVersionRow(RepositoryVersion version)
    {
        _version = version ?? throw new ArgumentNullException(nameof(version));
    }

    /// <summary>Номер версии хранилища (>= 1); -1 — служебная запись «актуальная версия».</summary>
    public int Number => _version.Number;

    /// <summary>Дата и время фиксации версии (как в модели).</summary>
    public DateTime Date => _version.Date;

    /// <summary>Имя пользователя хранилища, зафиксировавшего версию.</summary>
    public string Author => _version.Author ?? string.Empty;

    /// <summary>Комментарий к версии (переносы поддерживаются привязкой DataGrid/ListBox).</summary>
    public string Comment => _version.Comment ?? string.Empty;

    /// <summary>Признак актуальной (последней) версии хранилища.</summary>
    public bool IsCurrent => _version.IsCurrent;

    /// <summary>Колонка «№»: номер версии; для служебной записи — «акт.».</summary>
    public string NumberText => Number >= 0
        ? Number.ToString()
        : LocalizationManager.T("RepositoryBrowser.CurrentVersionShort");

    /// <summary>Колонка «Дата» (локальное время); «—» для служебной записи.</summary>
    public string DateText => Date == default
        ? "—"
        : Date.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss");

    /// <summary>Пометка актуальной версии: «●», иначе пустая строка.</summary>
    public string CurrentMark => IsCurrent ? "●" : string.Empty;
}