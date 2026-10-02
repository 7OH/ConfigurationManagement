#if LINUX
using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Configuration_Management.Localization;
using Configuration_Management.Models;
using Configuration_Management.Services;
using Configuration_Management.Themes;

namespace Configuration_Management
{
    /// <summary>
    /// Модальный диалог «Порты сервисов 1С» (issue #335): таблица из двух колонок —
    /// «описание сервиса» (только чтение) и «порт» (редактируется). Открывается из окна
    /// «Диагностика подключения» по кнопке «Проверить порты 1С»; результат возвращается
    /// через <see cref="Result"/> при подтверждении. Avalonia/Linux-версия WPF-окна
    /// <see cref="PortsEditWindow"/>.
    /// </summary>
    public sealed class PortsEditWindow : ModalWindowBase
    {
        private readonly IDialogService _dialogs = AppServices.GetRequiredService<IDialogService>();
        private readonly List<PortRow> _rows = new();

        /// <summary>Готовая карта портов при подтверждении, иначе <c>null</c>.</summary>
        public ServerPortsSettings? Result { get; private set; }

        /// <param name="ports">Начальная карта портов (сохранённые или «догадка» по порту кластера).</param>
        public PortsEditWindow(ServerPortsSettings ports)
        {
            Title = T("Ports.Title");
            Width = 480;
            Height = 400;
            MinWidth = 420;
            MinHeight = 300;
            FontSize = 13;
            CanResize = true;

            // Порядок строк совпадает с полями ServerPortsSettings: кластер/агент/хранилище/RAS.
            _rows.AddRange(new[]
            {
                new PortRow(T("Diagnostics.PortCluster"), ports.Cluster),
                new PortRow(T("Diagnostics.PortAgent"), ports.Agent),
                new PortRow(T("Diagnostics.PortRepository"), ports.Repository),
                new PortRow(T("Diagnostics.PortRas"), ports.Ras),
            });

            Content = BuildRoot();
        }

        /// <summary>Показывает окно модально (синхронно).</summary>
        public bool ShowSync(Window? owner = null) => ShowDialogSync(owner);

        private static string T(string key) => LocalizationManager.T(key);

        private Control BuildRoot()
        {
            var panel = new StackPanel { Margin = new Thickness(16), Spacing = 10 };

            var title = new TextBlock
            {
                Text = Title,
                FontSize = 15,
                FontWeight = FontWeight.SemiBold
            };
            panel.Children.Add(title);

            // Таблица «описание сервиса | порт»: заголовок + строки с редактируемым портом.
            var header = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(new GridLength(110)) } };
            header.Children.Add(MakeHeaderText(T("Ports.Service"), 0));
            header.Children.Add(MakeHeaderText(T("Ports.Port"), 1));
            panel.Children.Add(header);

            foreach (var row in _rows)
                panel.Children.Add(BuildRow(row));

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Spacing = 10,
                Margin = new Thickness(0, 12, 0, 0)
            };
            var ok = new Button { Content = T("Ports.Check"), Width = 140, Height = 36, IsDefault = true };
            ok.Styled(ControlThemes.DialogConfirmButton);
            ok.Click += (_, _) => OnOkClick();
            var cancel = BuildCancelActionButton(120);
            cancel.Click += (_, _) => OnCancelClick();
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);
            panel.Children.Add(buttons);

            return panel;
        }

        private static TextBlock MakeHeaderText(string text, int column)
        {
            var block = new TextBlock
            {
                Text = text,
                FontSize = 12,
                FontWeight = FontWeight.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0)
            };
            ThemeBrushes.Bind(block, TextBlock.ForegroundProperty, "TextSecondaryBrush");
            Grid.SetColumn(block, column);
            return block;
        }

        private static Control BuildRow(PortRow row)
        {
            var grid = new Grid
            {
                Margin = new Thickness(0, 2, 0, 2),
                ColumnDefinitions =
                {
                    new ColumnDefinition(GridLength.Star),
                    new ColumnDefinition(new GridLength(110))
                }
            };

            var service = new TextBlock
            {
                Text = row.Service,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(8, 0)
            };
            Grid.SetColumn(service, 0);
            grid.Children.Add(service);

            var portBox = new TextBox
            {
                Text = row.Port,
                Height = 30,
                Padding = new Thickness(6, 3),
                VerticalContentAlignment = VerticalAlignment.Center
            };
            portBox.Styled(ControlThemes.ModernTextBox);
            portBox.TextChanged += (_, _) => row.Port = portBox.Text ?? string.Empty;
            Grid.SetColumn(portBox, 1);
            grid.Children.Add(portBox);

            return grid;
        }

        private void OnOkClick()
        {
            var parsed = _rows.Select(r => int.TryParse(r.Port, out var p) ? p : 0).ToArray();
            if (parsed.Length < 4 || parsed.Any(p => p is < 1 or > 65535))
            {
                _dialogs.ShowWarning(T("Ports.InvalidPort"), T("Ports.Title"));
                return;
            }

            // Порядок в таблице: кластер, агент, хранилище, RAS.
            Result = new ServerPortsSettings(parsed[0], parsed[1], parsed[2], parsed[3]);
            DialogResult = true;
            Close();
        }

        private void OnCancelClick()
        {
            DialogResult = false;
            Close();
        }
    }

    /// <summary>Строка таблицы портов: описание сервиса (только чтение) и редактируемый порт.</summary>
    internal sealed class PortRow
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