using System.Collections.ObjectModel;
using Configuration_Management.Models;
using Configuration_Management.Services;

namespace Configuration_Management.ViewModels;

/// <summary>
/// Подсказка-токен подстановки: сам токен (<c>%name%</c>) и ключ локализации
/// краткого описания значения.
/// </summary>
public sealed class ScriptTokenHint
{
    public ScriptTokenHint(string token, string localizationKey)
    {
        Token = token;
        LocalizationKey = localizationKey;
    }

    /// <summary>Токен подстановки, например <c>%name%</c>.</summary>
    public string Token { get; }

    /// <summary>Ключ локализации описания токена.</summary>
    public string LocalizationKey { get; }
}

/// <summary>
/// Форма редактирования сценария запуска скрипта: наименование, путь к файлу
/// и параметры (строки). Чистый .NET, используется обеими платформами.
/// </summary>
public class ScriptScenarioEditViewModel : ViewModelBase
{
    /// <summary>Доступные токены подстановки (вставляются двойным кликом в поле параметров).</summary>
    public static readonly IReadOnlyList<ScriptTokenHint> AvailableTokens = new[]
    {
        new ScriptTokenHint("%name%", "Script.TokenName"),
        new ScriptTokenHint("%connection.server%", "Script.TokenConnectionServer"),
        new ScriptTokenHint("%connection.serverPort%", "Script.TokenConnectionServerPort"),
        new ScriptTokenHint("%connection.database%", "Script.TokenConnectionDatabase"),
        new ScriptTokenHint("%connection.filePath%", "Script.TokenConnectionFilePath"),
        new ScriptTokenHint("%connection.webUrl%", "Script.TokenConnectionWebUrl"),
        new ScriptTokenHint("%connection.connectionString%", "Script.TokenConnectionString"),
        new ScriptTokenHint("%date%", "Script.TokenDate"),
        new ScriptTokenHint("%date:yyyyMMdd_HHmm%", "Script.TokenDateCustom")
    };

    public ScriptScenarioEditViewModel(ScriptScenario? scenario)
    {
        if (scenario is not null)
        {
            Name = scenario.Name;
            FilePath = scenario.FilePath ?? "";
            foreach (var parameter in scenario.Parameters ?? new List<string>())
            {
                if (!string.IsNullOrWhiteSpace(parameter))
                    Parameters.Add(parameter.Trim());
            }
        }
        else
        {
            Parameters.Add("");
        }

        ParametersText = string.Join("\n", Parameters);
    }

    public string Name { get; set; } = "";
    public string FilePath { get; set; } = "";

    /// <summary>Параметры одной строкой через переводы строк (каждая строка — параметр).</summary>
    public string ParametersText { get; set; } = "";

    public ObservableCollection<string> Parameters { get; } = new();

    /// <summary>Разобранные (непустые, обрезанные) параметры формы.</summary>
    public List<string> NonEmptyParameters =>
        (ParametersText ?? "")
            .Replace("\r\n", "\n")
            .Replace('\r', '\n')
            .Split('\n')
            .Select(p => p.Trim())
            .Where(p => p.Length > 0)
            .ToList();

    /// <summary>
    /// Валидация полей формы. Возвращает ключ локализации ошибки либо <c>null</c>,
    /// если всё корректно (имя и путь к файлу непустые).
    /// </summary>
    public string? Validate()
    {
        if (string.IsNullOrWhiteSpace(Name))
            return "Script.NameRequired";
        if (string.IsNullOrWhiteSpace(FilePath))
            return "Script.FilePathRequired";
        return null;
    }

    /// <summary>Переносит заполненные поля формы в сценарий.</summary>
    public void ApplyTo(ScriptScenario scenario)
    {
        scenario.Name = Name.Trim();
        scenario.FilePath = FilePath.Trim();
        scenario.Parameters = NonEmptyParameters;
    }

    /// <summary>
    /// Пример командной строки с подстановками для указанной базы (используется
    /// как живая подсказка в окне редактирования и выбора).
    /// </summary>
    public static string BuildExampleCommandLine(ScriptScenario scenario, Infobase? infobase, DateTime? now = null)
    {
        var values = ScriptParameterResolver.BuildValueMap(infobase);
        return ScriptParameterResolver.BuildCommandLine(scenario, values, now);
    }
}