using Configuration_Management.Localization;
using Configuration_Management.Models;

namespace Configuration_Management.ViewModels;

/// <summary>
/// Строка вкладки «Рабочие процессы» окна «Серверы 1С» (0.3.9.124): форматирование
/// <see cref="RacProcessInfo"/> для отображения — память в МБ, время старта локально,
/// загрузка CPU в процентах, цвет состояния (запущен/остановлен). Чистый .NET —
/// без платформенных зависимостей.
/// </summary>
public sealed class RacProcessRow
{
    private readonly RacProcessInfo _info;

    public RacProcessRow(RacProcessInfo info)
    {
        _info = info ?? throw new System.ArgumentNullException(nameof(info));
    }

    /// <summary>Идентификатор процесса.</summary>
    public System.Guid Id => _info.Id;

    /// <summary>Тип процесса (rphost/rmngr); пусто, если не определён.</summary>
    public string Type => _info.Type;

    /// <summary>Имя компьютера процесса.</summary>
    public string Host => _info.Host;

    /// <summary>PID процесса ОС.</summary>
    public int Pid => _info.Pid;

    /// <summary>Порт процесса.</summary>
    public int Port => _info.Port;

    /// <summary>Время старта процесса (локальное), «—» если не задано.</summary>
    public string StartedAtText => _info.StartedAt == default
        ? "—"
        : _info.StartedAt.ToString("dd.MM.yyyy HH:mm:ss");

    /// <summary>Текущий объём памяти, МБ.</summary>
    public string MemorySizeText => FormatMb(_info.MemorySize);

    /// <summary>Доступный процессу объём памяти, МБ.</summary>
    public string MemoryTotalText => FormatMb(_info.MemoryTotal);

    /// <summary>Свободная память процесса, МБ.</summary>
    public string MemoryAvailableText => FormatMb(_info.MemoryAvailable);

    /// <summary>Превышение лимита памяти, МБ.</summary>
    public string MemoryExcessText => FormatMb(_info.MemoryExcess);

    /// <summary>Количество потоков.</summary>
    public int Threads => _info.Threads;

    /// <summary>Загрузка CPU, %.</summary>
    public string CpuText => _info.Cpu.ToString("0.0") + "%";

    /// <summary>Доступная производительность, %.</summary>
    public string AvailablePerformancesText => _info.AvailablePerformances.ToString("0.0") + "%";

    /// <summary>Признак запущенного процесса.</summary>
    public bool Running => _info.Running;

    /// <summary>«Да»/«Нет» для колонки «Запущен».</summary>
    public string RunningText => _info.Running
        ? LocalizationManager.T("Common.Yes")
        : LocalizationManager.T("Common.No");

    /// <summary>Количество информационных баз процесса.</summary>
    public int Infobases => _info.Infobases;

    /// <summary>Цвет состояния: зелёный — запущен, нейтральный — остановлен.</summary>
    public string StateColorHex => _info.Running ? "#16A34A" : "#64748B";

    private static string FormatMb(long bytes) =>
        (bytes / 1024.0 / 1024.0).ToString("0.0") + " " + LocalizationManager.T("ServerMonitor.Mb");
}