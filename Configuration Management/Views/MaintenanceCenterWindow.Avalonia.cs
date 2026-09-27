#if LINUX
using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Configuration_Management.Localization;
using Configuration_Management.Models;
using Configuration_Management.Themes;
using Configuration_Management.ViewModels;

namespace Configuration_Management
{
    /// <summary>
    /// Окно «Центр обслуживания» (0.3.9.89, Avalonia/Linux): таблица всех баз со
    /// сводкой состояния (доступность, последняя копия, размер ИБ, кэш, конфигурация,
    /// возраст данных, проверка обновлений), фильтр-переключатель «Только проблемы»
    /// и кнопки «Проверить доступность» / «Открыть базу». Двойной клик по строке —
    /// переход к базе в главном окне (FindInList-механика). Вся логика — в чистой
    /// ViewModel <see cref="MaintenanceCenterViewModel"/>.
    /// </summary>
    public sealed class MaintenanceCenterWindow : ModalWindowBase
    {
        private readonly MaintenanceCenterViewModel _vm;
        private readonly ListBox _grid = new();
        private readonly TextBlock _hint;

        /// <param name="infobases">Все базы списка.</param>
        /// <param name="checkAvailability">Запуск общей проверки доступности (команда главного окна).</param>
        /// <param name="openBase">Переход к базе в главном окне (FindInList-механика).</param>
        public MaintenanceCenterWindow(
            IEnumerable<Infobase> infobases,
            Action checkAvailability,
            Action<Infobase> openBase)
        {
            Title = LocalizationManager.T("Maintenance.Title");
            Width = 1080;
            Height = 600;
            MinWidth = 760;
            MinHeight = 420;
            FontSize = 13;
            CanResize = true;
            DataContext = _vm = new MaintenanceCenterViewModel(infobases, checkAvailability, openBase);

            // Верхняя панель: заголовок + фильтр «Только проблемы».
            var title = new TextBlock
            {
                Text = LocalizationManager.T("Maintenance.Title"),
                FontSize = 15,
                FontWeight = FontWeight.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            };
            var onlyProblems = new CheckBox
            {
                Content = LocalizationManager.T("Maintenance.OnlyProblems"),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(24, 0, 0, 0)
            };
            onlyProblems.Bind(CheckBox.IsCheckedProperty, new Binding("OnlyProblems")
            {
                Mode = BindingMode.TwoWay
            });

            var header = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Children = { title, onlyProblems }
            };

            var summary = new TextBlock
            {
                FontSize = 12,
                Opacity = 0.65,
                Margin = new Thickness(0, 6, 0, 10)
            };
            summary.Bind(TextBlock.TextProperty, new Binding("SummaryText"));

            // Таблица баз × состояние: шапка с фиксированными колонками и строки.
            _grid.ItemsSource = _vm.Rows;
            _grid.ItemTemplate = new FuncDataTemplate<MaintenanceCenterRowViewModel>((_, _) => BuildRow());
            _grid.DoubleTapped += OnGrid_DoubleTapped;
            ScrollViewer.SetHorizontalScrollBarVisibility(_grid, ScrollBarVisibility.Disabled);

            _hint = new TextBlock
            {
                Text = _vm.HasBases
                    ? LocalizationManager.T("Maintenance.Hint")
                    : LocalizationManager.T("Maintenance.NoBases"),
                FontSize = 11,
                Opacity = 0.65,
                Margin = new Thickness(0, 8, 0, 0),
                TextWrapping = TextWrapping.Wrap
            };

            // Кнопки: «Проверить доступность», «Открыть базу», «Закрыть».
            var checkButton = BuildActionButton("IconSonar",
                LocalizationManager.T("Maintenance.CheckAvailability"), () => _vm.CheckAvailability());
            var openButton = BuildActionButton("IconShortcut",
                LocalizationManager.T("Maintenance.OpenBase"), OpenSelectedBase);
            openButton.Width = 150;
            var closeButton = BuildCloseButton();

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Spacing = 8,
                Margin = new Thickness(0, 14, 0, 0),
                Children = { checkButton, openButton, closeButton }
            };

            var grid = new Grid
            {
                Margin = new Thickness(16),
                RowDefinitions =
                {
                    new RowDefinition(GridLength.Auto),
                    new RowDefinition(GridLength.Auto),
                    new RowDefinition(GridLength.Star),
                    new RowDefinition(GridLength.Auto),
                    new RowDefinition(GridLength.Auto)
                }
            };
            Place(grid, header, 0);
            Place(grid, summary, 1);
            Place(grid, _grid, 2);
            Place(grid, buttons, 3);
            Place(grid, _hint, 4);
            Content = grid;
        }

        private static void Place(Grid grid, Control control, int row)
        {
            Grid.SetRow(control, row);
            grid.Children.Add(control);
        }

        private static Button BuildActionButton(string iconKey, string text, System.Action onClick)
        {
            var button = new Button
            {
                Content = IconHelper.IconAndText(iconKey, text, 16, "TextPrimaryColorBrush"),
                Padding = new Thickness(12, 5)
            };
            button.Styled(ControlThemes.ModernButton);
            Themes.ThemeBrushes.Bind(button, Button.BackgroundProperty, "ItemHoverBrush");
            button.Click += (_, _) => onClick();
            return button;
        }

        private Button BuildCloseButton()
        {
            var button = new Button
            {
                Content = IconHelper.IconAndText("IconClose", LocalizationManager.T("Common.Close"), 14, "TextPrimaryBrush"),
                Width = 110,
                IsCancel = true
            };
            button.Styled(ControlThemes.ModernButton);
            Themes.ThemeBrushes.Bind(button, Button.BackgroundProperty, "ItemHoverBrush");
            Themes.ThemeBrushes.Bind(button, Button.ForegroundProperty, "TextPrimaryBrush");
            button.Click += (_, _) => Close();
            return button;
        }

        /// <summary>Строка таблицы: имя с пояснением и семь колонок состояния.</summary>
        private Control BuildRow()
        {
            var name = new TextBlock { FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
            name.Bind(TextBlock.TextProperty, new Binding("Name"));
            var subtitle = new TextBlock { FontSize = 11, Opacity = 0.6, TextTrimming = TextTrimming.CharacterEllipsis };
            subtitle.Bind(TextBlock.TextProperty, new Binding("Subtitle"));

            var nameCell = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(4, 2, 8, 2),
                Children = { name, subtitle }
            };

            var availability = CellText("AvailabilityText", bold: true);
            var lastBackup = CellText("LastBackupText");
            var size = CellText("SizeText");
            var cache = CellText("CacheText");
            var config = CellText("ConfigurationText");
            var dataAge = CellText("DataAgeText");
            var update = CellText("UpdateText");

            var row = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition(new GridLength(2, GridUnitType.Star)),
                    new ColumnDefinition(GridLength.Auto),
                    new ColumnDefinition(GridLength.Auto),
                    new ColumnDefinition(GridLength.Auto),
                    new ColumnDefinition(GridLength.Auto),
                    new ColumnDefinition(new GridLength(1.5, GridUnitType.Star)),
                    new ColumnDefinition(GridLength.Auto),
                    new ColumnDefinition(new GridLength(1.3, GridUnitType.Star))
                },
                Children = { nameCell, availability, lastBackup, size, cache, config, dataAge, update }
            };
            Grid.SetColumn(nameCell, 0);
            Grid.SetColumn(availability, 1);
            Grid.SetColumn(lastBackup, 2);
            Grid.SetColumn(size, 3);
            Grid.SetColumn(cache, 4);
            Grid.SetColumn(config, 5);
            Grid.SetColumn(dataAge, 6);
            Grid.SetColumn(update, 7);
            return row;
        }

        private static Control CellText(string property, bool bold = false)
        {
            var text = new TextBlock
            {
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0),
                TextTrimming = TextTrimming.CharacterEllipsis,
                FontWeight = bold ? FontWeight.SemiBold : FontWeight.Normal
            };
            text.Bind(TextBlock.TextProperty, new Binding(property));
            return text;
        }

        private void OnGrid_DoubleTapped(object? sender, TappedEventArgs e)
        {
            if (_grid.SelectedItem is MaintenanceCenterRowViewModel row)
                OpenBaseAndClose(row);
        }

        private void OpenSelectedBase()
        {
            if (_grid.SelectedItem is MaintenanceCenterRowViewModel row)
                OpenBaseAndClose(row);
        }

        /// <summary>«Открыть базу»: переход к строке в главном окне и закрытие дашборда.</summary>
        private void OpenBaseAndClose(MaintenanceCenterRowViewModel row)
        {
            _vm.OpenBase(row);
            Close();
        }
    }
}
#endif