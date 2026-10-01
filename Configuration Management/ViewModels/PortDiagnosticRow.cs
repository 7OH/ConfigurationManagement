using System.ComponentModel;
using System.Runtime.CompilerServices;
using Configuration_Management.Localization;
using Configuration_Management.Models;

namespace Configuration_Management.ViewModels;

/// <summary>
/// Строка таблицы портов окна «Диагностика подключения» (функция 12, этап 0.3.9.231):
/// порт, локализованное имя сервиса, состояние, задержка (RTT) и пояснение.
/// Чистый класс обеих платформ; тексты формируются через <c>LocalizationManager.T</c>.
/// </summary>
public sealed class PortDiagnosticRow : INotifyPropertyChanged
{
    public PortDiagnosticRow(NetworkPortProbe probe)
    {
        Port = probe.Port;
        ServiceName = LocalizationManager.T(probe.ServiceKey);
        Apply(probe);
    }

    /// <summary>Порт.</summary>
    public int Port { get; }

    /// <summary>Локализованное имя сервиса (агент/RAS/кластер/порт базы).</summary>
    public string ServiceName { get; }

    private string _stateText = string.Empty;

    /// <summary>Локализованное состояние: доступен / закрыт / таймаут / —.</summary>
    public string StateText
    {
        get => _stateText;
        private set => Set(ref _stateText, value);
    }

    private bool _isAvailable;

    /// <summary>true — порт открыт (для подсветки строки таблицы).</summary>
    public bool IsAvailable
    {
        get => _isAvailable;
        private set => Set(ref _isAvailable, value);
    }

    private string _rttText = string.Empty;

    /// <summary>Задержка установки TCP-соединения («12 мс») или «—».</summary>
    public string RttText
    {
        get => _rttText;
        private set => Set(ref _rttText, value);
    }

    private string _noteText = string.Empty;

    /// <summary>Пояснение строки («сервис не слушает», «нет ответа», «—»).</summary>
    public string NoteText
    {
        get => _noteText;
        private set => Set(ref _noteText, value);
    }

    /// <summary>Обновляет строку результатом новой проверки.</summary>
    public void Apply(NetworkPortProbe probe)
    {
        IsAvailable = probe.State == DiagnosticPortState.Available;
        StateText = probe.State switch
        {
            DiagnosticPortState.Available => LocalizationManager.T("Diagnostics.StateAvailable"),
            DiagnosticPortState.Closed => LocalizationManager.T("Diagnostics.StateClosed"),
            DiagnosticPortState.Timeout => LocalizationManager.T("Diagnostics.StateTimeout"),
            _ => LocalizationManager.T("Diagnostics.StateNotChecked")
        };
        RttText = probe.State == DiagnosticPortState.Available && probe.RttMs is { } rtt
            ? string.Format(LocalizationManager.T("Diagnostics.RttFormat"), rtt)
            : LocalizationManager.T("Diagnostics.NoteEmpty");
        NoteText = probe.ErrorCode switch
        {
            "refused" => LocalizationManager.T("Diagnostics.NoteClosed"),
            "timeout" => LocalizationManager.T("Diagnostics.NoteTimeout"),
            "dns_error" => LocalizationManager.T("Diagnostics.NoteDnsError"),
            _ => LocalizationManager.T("Diagnostics.NoteEmpty")
        };
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}