namespace Configuration_Management.Models;

/// <summary>
/// Сценарий запуска внешнего скрипта/исполняемого файла для информационной базы
/// (issue #308 «Пожелание — Скрипты»): наименование, путь к файлу и список строк
/// параметров с подстановками свойств базы (<c>%name%</c>, <c>%connection.server%</c>,
/// дата в пользовательском формате). Хранится в отдельном JSON-файле
/// (см. Services.ScriptScenarioStore).
/// </summary>
public class ScriptScenario
{
    /// <summary>Идентификатор сценария (GUID).</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>Наименование сценария.</summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// Путь к запускаемому файлу: скрипт (.bat/.cmd/.ps1, .sh) или исполняемый файл.
    /// Передаётся системному shell (cmd/sh) вместе с параметрами.
    /// </summary>
    public string FilePath { get; set; } = "";

    /// <summary>
    /// Параметры запуска (строки). Поддерживаются подстановки: <c>%name%</c> — имя базы,
    /// <c>%connection.server%</c> и другие свойства подключения через точку,
    /// <c>%date%</c>/<c>%date:формат%</c> — текущая дата. Неизвестный ключ остаётся
    /// в строке как есть (см. Services.ScriptParameterResolver).
    /// </summary>
    public List<string> Parameters { get; set; } = new();
}