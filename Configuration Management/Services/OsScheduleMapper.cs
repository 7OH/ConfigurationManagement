using System.Globalization;
using System.Text;
using System.Xml.Linq;
using Configuration_Management.Models;

namespace Configuration_Management.Services;

/// <summary>
/// Чистая логика маппинга расписания заданий (функция №2 «выполнение заданий по расписанию
/// без запущенного приложения») в форматы планировщиков ОС. Вынесена в отдельный статический
/// класс, чтобы полностью покрыть юнит-тестами без зависимостей от UI, процессов и файловой
/// системы (как <see cref="ScheduleCalculator"/>).
/// <para>
/// Windows: генерация XML-определения задачи Task Scheduler для <c>schtasks /Create /XML</c>
/// и разбор статуса из <c>schtasks /Query /XML</c>.
/// Linux: генерация строки <c>crontab</c> и ведение управляемого блока
/// <c># BEGIN/END ConfigurationManagement</c> (чужие записи пользователя не трогаются).
/// </para>
/// </summary>
public static class OsScheduleMapper
{
    /// <summary>Начальный маркер управляемого блока в crontab.</summary>
    public const string CrontabBeginMarker = "# BEGIN ConfigurationManagement";

    /// <summary>Конечный маркер управляемого блока в crontab.</summary>
    public const string CrontabEndMarker = "# END ConfigurationManagement";

    /// <summary>Префикс строки-маркера Id задания внутри управляемого блока.</summary>
    public const string CrontabTaskIdPrefix = "# id=";

    /// <summary>Имя папки задач Task Scheduler (в корне пользовательской области).</summary>
    public const string WindowsTaskFolder = "ConfigurationManagement";

    private const string TaskSchemaNamespace =
        "http://schemas.microsoft.com/windows/2004/02/mit/task";

    // ---------------------------------------------------------------------
    // Общие элементы
    // ---------------------------------------------------------------------

    /// <summary>Имя задачи в Task Scheduler: <c>\ConfigurationManagement\<id></c>.</summary>
    public static string WindowsTaskName(string taskId)
        => $@"\{WindowsTaskFolder}\{taskId}";

    // ---------------------------------------------------------------------
    // Linux: crontab
    // ---------------------------------------------------------------------

    /// <summary>
    /// Строит расписание crontab из задания: «минуты часы * * [дни]».
    /// Пустой список дней недели — ежедневно (<c>*</c>); иначе дни в cron-нотации
    /// (0=воскресенье … 6=суббота), отсортированные по возрастанию и без дублей.
    /// </summary>
    public static string BuildCronSchedule(ScheduledTask task)
    {
        var time = task.GetTime();
        // Каноническая запись crontab — без ведущих нулей («5 9»), как у crontab(5).
        var minutes = time.Minutes.ToString(CultureInfo.InvariantCulture);
        var hours = time.Hours.ToString(CultureInfo.InvariantCulture);

        var days = task.DaysOfWeek is { Count: > 0 }
            ? string.Join(",", task.DaysOfWeek.Select(ToCronDay).Distinct().OrderBy(d => d))
            : "*";

        return $"{minutes} {hours} * * {days}";
    }

    /// <summary>
    /// Строит полную строку записи crontab для задания:
    /// <c>мм чч * * [дни] "/абс/путь/exe" --run-task <id> --profile <pid> >> "лог" 2>&1</c>.
    /// Перенаправление вывода обязательно: почта cron обычно не настроена, а окружение
    /// минимально (нет DISPLAY/DBus) — stdout/stderr пишутся в файловый лог.
    /// </summary>
    public static string BuildCronEntry(ScheduledTask task, string executablePath, string profileId, string logFilePath)
    {
        var schedule = BuildCronSchedule(task);
        return string.Join(" ",
            schedule,
            QuoteShellArgument(executablePath),
            "--run-task", task.Id,
            "--profile", profileId,
            ">>", QuoteShellArgument(logFilePath), "2>&1");
    }

    /// <summary>
    /// Собирает управляемый блок crontab (с маркерами BEGIN/END) из записей
    /// «taskId → строка crontab». Каждой записи предшествует маркер <c># id=<id></c>.
    /// </summary>
    public static string BuildCronBlock(IEnumerable<KeyValuePair<string, string>> entries)
    {
        // Только LF (\n): crontab — формат Unix, на Windows AppendLine дал бы \r\n.
        var sb = new StringBuilder();
        sb.Append(CrontabBeginMarker).Append('\n');
        foreach (var pair in entries)
        {
            sb.Append(CrontabTaskIdPrefix).Append(pair.Key).Append('\n');
            sb.Append(pair.Value).Append('\n');
        }
        sb.Append(CrontabEndMarker);
        return sb.ToString();
    }

    /// <summary>
    /// Заменяет управляемый блок в содержимом crontab на <paramref name="block"/>
    /// (уже с маркерами BEGIN/END). Записи вне блока сохраняются без изменений.
    /// Если блока не было — добавляет его в конец с пустой строкой-разделителем.
    /// Пустой/пробельный блок удаляет существующий блок. Нормализует CRLF→LF.
    /// </summary>
    public static string ReplaceManagedBlock(string crontabContent, string block)
    {
        var lines = (crontabContent ?? "").Replace("\r\n", "\n").Split('\n');
        var kept = new List<string>();
        var inBlock = false;

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed == CrontabBeginMarker) { inBlock = true; continue; }
            if (trimmed == CrontabEndMarker) { inBlock = false; continue; }
            if (!inBlock)
                kept.Add(line);
        }

        // Убираем «хвостовые» пустые строки пользовательской части, чтобы блок
        // отделялся ровно одной пустой строкой.
        while (kept.Count > 0 && string.IsNullOrWhiteSpace(kept[^1]))
            kept.RemoveAt(kept.Count - 1);

        var body = (block ?? "").TrimEnd('\n', '\r');
        if (string.IsNullOrWhiteSpace(body))
            return string.Join('\n', kept) + (kept.Count > 0 ? "\n" : "");

        var head = string.Join('\n', kept);
        return head.Length == 0 ? body + "\n" : head + "\n\n" + body + "\n";
    }

    /// <summary>
    /// Извлекает Id заданий из управляемого блока crontab (по маркерам <c># id=<id></c>).
    /// Записи вне блока игнорируются.
    /// </summary>
    public static IReadOnlyList<string> ExtractManagedTaskIds(string crontabContent)
    {
        var result = new List<string>();
        var inBlock = false;

        foreach (var line in (crontabContent ?? "").Replace("\r\n", "\n").Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed == CrontabBeginMarker) { inBlock = true; continue; }
            if (trimmed == CrontabEndMarker) { inBlock = false; continue; }
            if (inBlock && trimmed.StartsWith(CrontabTaskIdPrefix, StringComparison.Ordinal))
            {
                var id = trimmed[CrontabTaskIdPrefix.Length..].Trim();
                if (id.Length > 0)
                    result.Add(id);
            }
        }

        return result;
    }

    /// <summary>День недели .NET → день недели cron (0=воскресенье … 6=суббота).</summary>
    private static int ToCronDay(DayOfWeek day) => day switch
    {
        DayOfWeek.Sunday => 0,
        DayOfWeek.Monday => 1,
        DayOfWeek.Tuesday => 2,
        DayOfWeek.Wednesday => 3,
        DayOfWeek.Thursday => 4,
        DayOfWeek.Friday => 5,
        _ => 6
    };

    /// <summary>Оборачивает аргумент shell-команды в двойные кавычки, экранируя вложенные.</summary>
    private static string QuoteShellArgument(string value)
        => "\"" + (value ?? "").Replace("\"", "\\\"") + "\"";

    // ---------------------------------------------------------------------
    // Windows: Task Scheduler XML
    // ---------------------------------------------------------------------

    /// <summary>
    /// Генерирует XML-определение задачи Task Scheduler для <c>schtasks /Create /XML</c>.
    /// Ежедневное расписание — <c>ScheduleByDay</c>; по дням недели — <c>ScheduleByWeek</c>
    /// (имена элементов дней английские: Monday…Sunday). Стартовая граница —
    /// текущая дата в локальном времени (дальнейшие срабатывания вычисляет планировщик).
    /// <c>InteractiveToken</c> + <c>LeastPrivilege</c> — задача текущего пользователя без прав
    /// администратора; <c>MultipleInstancesPolicy=IgnoreNew</c> не даёт запустить второй
    /// экземпляр задания, пока выполняется первый.
    /// </summary>
    public static string BuildWindowsTaskXml(ScheduledTask task, string executablePath, string profileId)
    {
        var ns = XNamespace.Get(TaskSchemaNamespace);

        var root = new XElement(ns + "Task",
            new XAttribute("version", "1.2"),
            new XElement(ns + "RegistrationInfo",
                new XElement(ns + "Description",
                    "Configuration Management scheduled task: " + (task.Name ?? ""))),
            new XElement(ns + "Triggers", BuildCalendarTrigger(task, ns)),
            new XElement(ns + "Principals",
                new XElement(ns + "Principal",
                    new XAttribute("id", "Author"),
                    new XElement(ns + "LogonType", "InteractiveToken"),
                    new XElement(ns + "RunLevel", "LeastPrivilege"))),
            new XElement(ns + "Settings",
                // Задача целиком (включена/выключена) — читается из /Query /XML при GetStatus.
                new XElement(ns + "Enabled", task.Enabled ? "true" : "false"),
                new XElement(ns + "MultipleInstancesPolicy", "IgnoreNew"),
                new XElement(ns + "DisallowStartIfOnBatteries", "false"),
                new XElement(ns + "StopIfGoingOnBatteries", "false"),
                new XElement(ns + "AllowHardTerminate", "true"),
                new XElement(ns + "StartWhenAvailable", "true"),
                new XElement(ns + "RunOnlyIfNetworkAvailable", "false"),
                new XElement(ns + "Hidden", "false"),
                // Ограничение времени выполнения: резервная копия крупной базы может идти долго.
                new XElement(ns + "ExecutionTimeLimit", "PT72H"),
                new XElement(ns + "Priority", "7")),
            new XElement(ns + "Actions",
                new XAttribute("Context", "Author"),
                new XElement(ns + "Exec",
                    new XElement(ns + "Command", executablePath),
                    new XElement(ns + "Arguments",
                        $"--run-task {task.Id} --profile {profileId}"))));

        var doc = new XDocument(new XDeclaration("1.0", "utf-16", null), root);
        // Без форматирования: schtasks принимает одну строку; содержимое экранируется
        // самим XDocument (<, & и т.п.), пути с пробелами/кириллицей допустимы.
        return doc.ToString(SaveOptions.DisableFormatting);
    }

    /// <summary>
    /// Разбирает статус задачи из XML-вывода <c>schtasks /Query /XML</c>. Возвращает null,
    /// если XML не похож на определение задачи (запись отсутствует либо повреждена).
    /// Поля <c>LastRunTime</c>/<c>NextRunTime</c> опциональны: дата «1601-01-01» («никогда»)
    /// трактуется как null.
    /// </summary>
    public static OsScheduledTaskStatus? ParseWindowsTaskStatus(string xml)
    {
        try
        {
            var doc = XDocument.Parse(xml);
            var root = doc.Root;
            if (root is null || root.Name.LocalName != "Task")
                return null;

            var ns = root.Name.Namespace;
            var status = new OsScheduledTaskStatus { Registered = true };

            // Включено ли задание целиком: Settings/Enabled; запасной вариант —
            // Enabled первого триггера (в переходных состояниях).
            var settingsEnabled = (string?)root.Element(ns + "Settings")?.Element(ns + "Enabled");
            if (settingsEnabled is not null && bool.TryParse(settingsEnabled, out var taskEnabled))
            {
                status.Enabled = taskEnabled;
            }
            else
            {
                var triggerEnabled = (string?)root
                    .Element(ns + "Triggers")?.Elements().FirstOrDefault()
                    ?.Element(ns + "Enabled");
                if (triggerEnabled is not null && bool.TryParse(triggerEnabled, out var trigEnabled))
                    status.Enabled = trigEnabled;
            }

            status.LastRunTime = ParseTaskDate((string?)root.Element(ns + "LastRunTime"));
            status.NextRunTime = ParseTaskDate((string?)root.Element(ns + "NextRunTime"));

            if (int.TryParse((string?)root.Element(ns + "LastTaskResult"), NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out var result))
                status.LastTaskResult = result;

            return status;
        }
        catch
        {
            return null;
        }
    }

    private static XElement BuildCalendarTrigger(ScheduledTask task, XNamespace ns)
    {
        var trigger = new XElement(ns + "CalendarTrigger",
            new XElement(ns + "StartBoundary", BuildStartBoundary(task)),
            new XElement(ns + "Enabled", task.Enabled ? "true" : "false"));

        if (task.DaysOfWeek is { Count: > 0 })
        {
            var daysOfWeek = new XElement(ns + "DaysOfWeek");
            foreach (var day in task.DaysOfWeek.Distinct())
                daysOfWeek.Add(new XElement(ns + DayElementName(day)));
            trigger.Add(new XElement(ns + "ScheduleByWeek",
                new XElement(ns + "WeeksInterval", "1"),
                daysOfWeek));
        }
        else
        {
            trigger.Add(new XElement(ns + "ScheduleByDay",
                new XElement(ns + "DaysInterval", "1")));
        }

        return trigger;
    }

    /// <summary>Стартовая граница триггера: сегодня в локальном времени, формат yyyy-MM-ddTHH:mm:ss.</summary>
    private static string BuildStartBoundary(ScheduledTask task)
    {
        var time = task.GetTime();
        return $"{DateTime.Today:yyyy-MM-dd}T{time.Hours:00}:{time.Minutes:00}:00";
    }

    /// <summary>Имя XML-элемента дня недели Task Scheduler (Monday…Sunday).</summary>
    private static string DayElementName(DayOfWeek day) => day.ToString();

    /// <summary>
    /// Дата из XML-вывода schtasks («1601-01-01T00:00:00» = «никогда») → DateTime или null.
    /// </summary>
    private static DateTime? ParseTaskDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        if (!DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            return null;
        return date.Year >= 1602 ? date : null;
    }
}