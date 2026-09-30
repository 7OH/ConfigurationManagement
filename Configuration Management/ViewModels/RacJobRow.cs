using System.Text;
using Configuration_Management.Localization;
using Configuration_Management.Models;

namespace Configuration_Management.ViewModels;

/// <summary>
/// Строка вкладки «Регламентные задания» окна «Серверы 1С» (0.3.9.177, цикл
/// 0.3.9.176–0.3.9.179): форматирование <see cref="RacJobInfo"/> для отображения —
/// имя/метод/расписание, локализованное состояние с цветом, времена запусков,
/// результат последнего запуска (обрезанный) и признак доступности действий
/// «Приостановить/Возобновить». Также строит текст деталей <see cref="DetailsText"/>
/// для окна «Детали задания». Чистый .NET — без платформенных зависимостей.
/// </summary>
public sealed class RacJobRow
{
    /// <summary>Максимальная длина текста результата в таблице (полный — в деталях).</summary>
    public const int MaxResultLength = 80;

    private readonly RacJobInfo _info;

    public RacJobRow(RacJobInfo info, string infobaseName = "")
    {
        _info = info ?? throw new System.ArgumentNullException(nameof(info));
        InfobaseName = infobaseName;
    }

    /// <summary>Идентификатор задания (для команд rac --job=...).</summary>
    public System.Guid Id => _info.Id;

    /// <summary>Идентификатор информационной базы-владельца; null — задание без ИБ.</summary>
    public System.Guid? InfobaseId => _info.InfobaseId;

    /// <summary>Имя информационной базы-владельца (из маппинга VM) или «—».</summary>
    public string InfobaseName { get; }

    /// <summary>Имя задания.</summary>
    public string Name => _info.Name;

    /// <summary>Имя метода, выполняемого заданием.</summary>
    public string MethodName => _info.MethodName;

    /// <summary>«Да»/«Нет» для колонки «Предопределённое».</summary>
    public string PredefinedText => _info.Predefined
        ? LocalizationManager.T("Common.Yes")
        : LocalizationManager.T("Common.No");

    /// <summary>Расписание задания (cron-подобная строка); «—» если пусто.</summary>
    public string Schedule => string.IsNullOrWhiteSpace(_info.Schedule)
        ? "—"
        : _info.Schedule;

    /// <summary>Состояние задания из rac (например «scheduled», «running», «paused»).</summary>
    public string State => _info.State;

    /// <summary>Переведённое состояние задания.</summary>
    public string StateText => LocalizeState(_info.State);

    /// <summary>
    /// Цвет состояния: зелёный — выполняется, синий — запланировано, жёлтый —
    /// приостановлено/прервано, серый — снято с расписания/неизвестное.
    /// </summary>
    public string StateColorHex => _info.State.ToLowerInvariant() switch
    {
        "running" => "#16A34A",
        "scheduled" => "#2563EB",
        "paused" or "interrupted" => "#D97706",
        "disabled" => "#64748B",
        _ => "#64748B"
    };

    /// <summary>Время фактического старта текущего выполнения (локальное), «—» если не задано.</summary>
    public string StartedAtText => FormatDateTime(_info.StartedAt);

    /// <summary>Ближайшее время запуска по расписанию (локальное), «—» если не задано.</summary>
    public string NextStartText => FormatDateTime(_info.NextStart);

    /// <summary>Время последнего запуска (локальное), «—» если не задано.</summary>
    public string LastStartText => FormatDateTime(_info.LastStart);

    /// <summary>Время окончания последнего запуска (локальное), «—» если не задано.</summary>
    public string LastEndText => FormatDateTime(_info.LastEnd);

    /// <summary>
    /// Результат последнего запуска: «Успешно»/«Ошибка» по флагам rac, «—» если запусков
    /// ещё не было (все флаги и даты пусты).
    /// </summary>
    public string LastSuccessText
    {
        get
        {
            var hasRun = _info.LastStart != default || _info.LastSuccess || _info.LastError;
            if (!hasRun)
                return "—";
            if (_info.LastError)
                return LocalizationManager.T("ServerMonitor.Job.Result.Error");
            if (_info.LastSuccess)
                return LocalizationManager.T("ServerMonitor.Job.Result.Success");
            return "—";
        }
    }

    /// <summary>Текст результата последнего запуска, обрезанный до <see cref="MaxResultLength"/> символов.</summary>
    public string ResultText => TrimResult(_info.Result);

    /// <summary>Разрешено ли «Приостановить»: задание не приостановлено и не снято с расписания.</summary>
    public bool CanPause =>
        _info.State.ToLowerInvariant() is not ("paused" or "disabled") &&
        !string.IsNullOrWhiteSpace(_info.State);

    /// <summary>Разрешено ли «Возобновить»: задание приостановлено.</summary>
    public bool CanResume => _info.State.ToLowerInvariant() == "paused";

    /// <summary>
    /// Полный текст деталей задания («ключ: значение» построчно) для окна «Детали»:
    /// все поля модели, включая расписание, идентификаторы, времена, результат и ошибку.
    /// </summary>
    public string DetailsText
    {
        get
        {
            var sb = new StringBuilder();
            sb.AppendLine(Field("job", _info.Id.ToString()));
            sb.AppendLine(Field("infobase", _info.InfobaseId?.ToString() ?? string.Empty));
            sb.AppendLine(Field("infobase-name", InfobaseName));
            sb.AppendLine(Field("name", _info.Name));
            sb.AppendLine(Field("method-name", _info.MethodName));
            sb.AppendLine(Field("predefined", _info.Predefined ? "1" : "0"));
            sb.AppendLine(Field("schedule", _info.Schedule));
            sb.AppendLine(Field("state", _info.State));
            sb.AppendLine(Field("started-at", FormatIso(_info.StartedAt)));
            sb.AppendLine(Field("next-start", FormatIso(_info.NextStart)));
            sb.AppendLine(Field("last-start", FormatIso(_info.LastStart)));
            sb.AppendLine(Field("last-end", FormatIso(_info.LastEnd)));
            sb.AppendLine(Field("last-success", _info.LastSuccess ? "1" : "0"));
            sb.AppendLine(Field("last-error", _info.LastError ? "1" : "0"));
            sb.AppendLine(Field("last-error-descr", _info.LastErrorDescr));
            sb.AppendLine(Field("process", _info.ProcessId == System.Guid.Empty ? string.Empty : _info.ProcessId.ToString()));
            sb.Append(Field("result", _info.Result));
            return sb.ToString().TrimEnd();
        }
    }

    private static string Field(string key, string value) =>
        $"{key}: {value}";

    private static string FormatDateTime(System.DateTime value) => value == default
        ? "—"
        : value.ToString("dd.MM.yyyy HH:mm:ss");

    private static string FormatIso(System.DateTime value) => value == default
        ? string.Empty
        : value.ToString("yyyy-MM-ddTHH:mm:ss");

    private static string TrimResult(string result)
    {
        var value = result?.Trim() ?? string.Empty;
        if (value.Length <= MaxResultLength)
            return value;
        return value.Substring(0, MaxResultLength).TrimEnd() + "…";
    }

    private static string LocalizeState(string state) => state.ToLowerInvariant() switch
    {
        "running" => LocalizationManager.T("ServerMonitor.Job.State.Running"),
        "scheduled" => LocalizationManager.T("ServerMonitor.Job.State.Scheduled"),
        "paused" => LocalizationManager.T("ServerMonitor.Job.State.Paused"),
        "disabled" => LocalizationManager.T("ServerMonitor.Job.State.Disabled"),
        "interrupted" => LocalizationManager.T("ServerMonitor.Job.State.Interrupted"),
        _ => string.IsNullOrWhiteSpace(state) ? "—" : state
    };
}