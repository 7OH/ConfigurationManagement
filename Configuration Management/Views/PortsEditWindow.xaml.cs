#if WINDOWS
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using Configuration_Management.Localization;
using Configuration_Management.Models;
using Configuration_Management.Services;

namespace Configuration_Management;

/// <summary>
/// Модальный диалог «Порты сервисов 1С» (issue #335): таблица из двух колонок —
/// «описание сервиса» (только чтение) и «порт» (редактируется). Открывается из окна
/// «Диагностика подключения» по кнопке «Проверить порты 1С»; результат возвращается
/// через <see cref="Result"/> при подтверждении (кнопка «Проверить»).
/// </summary>
public partial class PortsEditWindow : Window
{
    /// <summary>Готовая карта портов при подтверждении, иначе <c>null</c>.</summary>
    public ServerPortsSettings? Result { get; private set; }

    private readonly IDialogService _dialogs = AppServices.GetRequiredService<IDialogService>();

    /// <param name="ports">Начальная карта портов (сохранённые или «догадка» по порту кластера).</param>
    public PortsEditWindow(ServerPortsSettings ports)
    {
        InitializeComponent();

        EditorTitle.Text = LocalizationManager.T("Ports.Title");
        Title = EditorTitle.Text;

        // Порядок строк совпадает с полями ServerPortsSettings: кластер/агент/хранилище/RAS.
        var source = new List<PortRow>
        {
            new(LocalizationManager.T("Diagnostics.PortCluster"), ports.Cluster),
            new(LocalizationManager.T("Diagnostics.PortAgent"), ports.Agent),
            new(LocalizationManager.T("Diagnostics.PortRepository"), ports.Repository),
            new(LocalizationManager.T("Diagnostics.PortRas"), ports.Ras),
        };
        PortsGrid.ItemsSource = source;

        // Фокус в первом поле порта после показа окна.
        Loaded += (_, _) =>
        {
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle,
                new System.Action(() => PortsGrid.Focus()));
        };
    }

    private void OnOkClick(object sender, RoutedEventArgs e)
    {
        if (PortsGrid.ItemsSource is not IList<PortRow> rows || rows.Count < 4)
            return;

        var parsed = rows.Select(r => int.TryParse(r.Port, out var p) ? p : 0).ToArray();
        if (parsed.Any(p => p is < 1 or > 65535))
        {
            _dialogs.ShowWarning(
                LocalizationManager.T("Ports.InvalidPort"),
                LocalizationManager.T("Ports.Title"));
            return;
        }

        // Порядок в таблице: кластер, агент, хранилище, RAS.
        Result = new ServerPortsSettings(parsed[0], parsed[1], parsed[2], parsed[3]);
        DialogResult = true;
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    /// <summary>Строка таблицы портов: описание сервиса (только чтение) и редактируемый порт.</summary>
    private sealed class PortRow
    {
        public string Service { get; }
        public string Port { get; set; }

        public PortRow(string service, int port)
        {
            Service = service;
            Port = port > 0 ? port.ToString() : string.Empty;
        }
    }
}
#endif