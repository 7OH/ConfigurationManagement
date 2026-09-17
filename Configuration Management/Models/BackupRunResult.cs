namespace Configuration_Management.Models;

/// <summary>
/// Результат выполнения сценария резервирования или операции восстановления.
/// </summary>
public class BackupRunResult
{
    /// <summary>Успешно ли выполнена операция.</summary>
    public bool Success { get; set; }

    /// <summary>Пути созданных (скопированных) файлов резервных копий.</summary>
    public IReadOnlyList<string> CreatedFiles { get; set; } = Array.Empty<string>();

    /// <summary>Сообщение об ошибке при неуспехе.</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>Имя сценария, для которого выполнялась операция.</summary>
    public string ScenarioName { get; set; } = "";
}