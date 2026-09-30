using Configuration_Management.Localization;

namespace Configuration_Management.ViewModels;

/// <summary>
/// Строка фильтра «по базе» на вкладке «Регламентные задания» окна «Серверы 1С»
/// (0.3.9.177): пункт «Все базы» (<see cref="Id"/> == null) и записи информационных
/// баз кластера. Чистый .NET — без платформенных зависимостей.
/// </summary>
public sealed class RacJobFilterRow
{
    public RacJobFilterRow(System.Guid? id, string displayText)
    {
        Id = id;
        DisplayText = displayText;
    }

    /// <summary>Идентификатор базы для фильтрации; null — «Все базы».</summary>
    public System.Guid? Id { get; }

    /// <summary>Текст для выпадающего списка («Все базы» / имя базы).</summary>
    public string DisplayText { get; }

    /// <summary>Пункт «Все базы» (Id == null).</summary>
    public static RacJobFilterRow All => new(null, LocalizationManager.T("ServerMonitor.Job.Filter.All"));
}