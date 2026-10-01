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
    /// <c>%connection.server%</c> и другие свойства подключения через точку (включая
    /// <c>%connection.password%</c>/<c>%password%</c>), <c>%date%</c>/<c>%date:формат%</c> —
    /// текущая дата. Неизвестный ключ остаётся в строке как есть
    /// (см. Services.ScriptParameterResolver).
    /// </summary>
    public List<string> Parameters { get; set; } = new();

    /// <summary>
    /// Папка запуска скрипта (рабочий каталог процесса, <c>WorkingDirectory</c>).
    /// Необязательна: при пустом значении процесс наследует рабочий каталог
    /// приложения. Старые JSON-файлы сценариев без этого поля мигрируют без ошибок —
    /// дефолт сохраняет прежнее поведение (issue #308, п.7).
    /// </summary>
    public string WorkingDirectory { get; set; } = "";

    /// <summary>
    /// Скрывать окно запущенного скрипта: <c>true</c> — окно скрыто (по умолчанию),
    /// <c>false</c> — консольное окно видимо (Windows: cmd.exe без CreateNoWindow;
    /// на Linux /bin/sh выполняется без терминала, видимое окно зависит от окружения).
    /// </summary>
    public bool HideWindow { get; set; } = true;

    /// <summary>
    /// Уведомлять о запуске сценария (issue #308): при <c>true</c> после запуска
    /// показывается диалог «скрипт запущен» и отправляется системное уведомление;
    /// при <c>false</c> (по умолчанию) — запуск выполняется без диалога и
    /// уведомлений («без опций — просто не надо»). Старые JSON-файлы сценариев
    /// без этого поля мигрируют без ошибок — дефолт отключает уведомления.
    /// </summary>
    public bool NotifyOnStart { get; set; }

    /// <summary>
    /// Не закрывать окно после завершения сценария (issue #308): при <c>true</c>
    /// к командной строке добавляется хвостовая команда, удерживающая консольное
    /// окно открытым при ошибках/завершении — <c>pause</c> (cmd), <c>Read-Host</c>
    /// (PowerShell), <c>read</c> (sh). По умолчанию <c>false</c> — окно закрывается
    /// как раньше.
    /// </summary>
    public bool KeepOpen { get; set; }

    /// <summary>
    /// Интерпретатор (shell) для запуска сценария (issue #308, п.9):
    /// <see cref="ScriptShell.Auto"/> — по платформе (cmd.exe на Windows, /bin/sh
    /// на Linux), либо явный выбор — cmd / PowerShell / sh. Старые JSON-файлы
    /// сценариев без этого поля мигрируют без ошибок — дефолт сохраняет прежнее
    /// поведение (Auto).
    /// </summary>
    public ScriptShell Shell { get; set; } = ScriptShell.Auto;
}