using System.Collections.ObjectModel;
using Configuration_Management.Models;

namespace Configuration_Management.ViewModels;

/// <summary>
/// Форма редактирования сценария резервирования: поля формы и валидация.
/// Чистый .NET, используется обеими платформами.
/// </summary>
public class BackupScenarioEditViewModel : ViewModelBase
{
    public BackupScenarioEditViewModel(BackupScenario? scenario)
    {
        if (scenario is not null)
        {
            Name = scenario.Name;
            FileNameTemplate = scenario.FileNameTemplate;
            Format = scenario.Format;
            BasePrefix = scenario.BasePrefix ?? "";
            IncludeTimestamp = scenario.IncludeTimestamp;
            UseInfobaseAuth = scenario.Credential?.UseInfobaseAuth ?? true;
            User = scenario.Credential?.User ?? "";
            Password = scenario.Credential?.Password ?? "";
            foreach (var dir in scenario.TargetDirectories ?? new List<string>())
                TargetDirectories.Add(dir);
        }

        if (TargetDirectories.Count == 0)
            TargetDirectories.Add("");
    }

    public string Name { get; set; } = "";
    public string FileNameTemplate { get; set; } = "{Base}_{Timestamp}";
    public ObservableCollection<string> TargetDirectories { get; } = new();
    public BackupFormat Format { get; set; } = BackupFormat.Dt;
    public string BasePrefix { get; set; } = "";
    public bool IncludeTimestamp { get; set; } = true;
    public bool UseInfobaseAuth { get; set; } = true;
    public string User { get; set; } = "";
    public string Password { get; set; } = "";

    public void AddDirectory() => TargetDirectories.Add("");

    public void RemoveDirectoryAt(int index)
    {
        if (index >= 0 && index < TargetDirectories.Count)
            TargetDirectories.RemoveAt(index);
    }

    /// <summary>
    /// Валидация полей формы. Возвращает ключ локализации ошибки либо <c>null</c>,
    /// если всё корректно (имя непустое, 1–3 каталога назначения).
    /// </summary>
    public string? Validate()
    {
        if (string.IsNullOrWhiteSpace(Name))
            return "Backup.NameRequired";

        var dirs = NonEmptyDirectories;
        if (dirs.Count == 0)
            return "Backup.NoTargetDirectory";
        if (dirs.Count > 3)
            return "Backup.TooManyDirectories";

        return null;
    }

    /// <summary>Непустые (обрезанные) каталоги назначения формы.</summary>
    public List<string> NonEmptyDirectories =>
        TargetDirectories
            .Where(d => !string.IsNullOrWhiteSpace(d))
            .Select(d => d.Trim())
            .ToList();

    /// <summary>Переносит заполненные поля формы в сценарий.</summary>
    public void ApplyTo(BackupScenario scenario)
    {
        scenario.Name = Name.Trim();
        scenario.FileNameTemplate = string.IsNullOrWhiteSpace(FileNameTemplate)
            ? "{Base}_{Timestamp}"
            : FileNameTemplate.Trim();
        scenario.Format = Format;
        scenario.BasePrefix = BasePrefix?.Trim() ?? "";
        scenario.IncludeTimestamp = IncludeTimestamp;
        scenario.TargetDirectories = NonEmptyDirectories;
        scenario.Credential = new BackupCredential
        {
            UseInfobaseAuth = UseInfobaseAuth,
            User = User?.Trim() ?? "",
            Password = Password ?? ""
        };
    }
}