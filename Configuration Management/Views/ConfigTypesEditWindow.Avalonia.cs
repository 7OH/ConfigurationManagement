#if LINUX
using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Configuration_Management.Localization;
using Configuration_Management.Models;
using Configuration_Management.Services;
using Configuration_Management.Themes;
using Configuration_Management.ViewModels;

namespace Configuration_Management
{
    /// <summary>
    /// Окно «Типовые конфигурации» (issue #321): список предопределённых (только для чтения)
    /// и пользовательских конфигураций 1С. Добавление/правка выполняются в отдельном модальном
    /// окне <see cref="ConfigTypeEditWindow"/> — «Правка» больше не переключает это окно «два в
    /// одном», а «Закрыть» закрывает только список. Изменения сохраняются сразу в файл
    /// <c>custom_config_types.json</c> через <see cref="ICustomConfigTypesStore"/> и переживают
    /// перезапуск. Предопределённые конфигурации из <see cref="BuiltInConfigTypes"/> нельзя
    /// изменять или удалять (общие статические экземпляры — их мутация «расползалась» по другим
    /// окнам: актуальные релизы, проверка обновлений, связь с конфигурацией).
    /// Avalonia/Linux-версия WPF-окна <see cref="ConfigTypesEditWindow"/>.
    /// </summary>
    public sealed class ConfigTypesEditWindow : ModalWindowBase
    {
        private readonly Services.ICustomConfigTypesStore _store = AppServices.GetRequiredService<Services.ICustomConfigTypesStore>();
        private readonly Services.IAppLogger _logger = AppServices.GetRequiredService<Services.IAppLogger>();
        private readonly IDialogService _dialogs = AppServices.GetRequiredService<IDialogService>();

        private readonly List<OneCConfigType> _customTypes = new();
        private readonly List<ConfigTypeItemViewModel> _rows = new();

        // Панель строк списка (пересобирается после каждого изменения).
        private readonly StackPanel _rowsPanel = new();
        private readonly DockPanel _listView = new() { LastChildFill = true };

        /// <summary>
        /// Открывает окно редактирования списка типовых конфигураций.
        /// </summary>
        public ConfigTypesEditWindow()
        {
            Title = LocalizationManager.T("Updates.ConfigTypesTitle");
            Width = 820;
            Height = 560;
            MinWidth = 640;
            MinHeight = 440;
            FontSize = 13;
            CanResize = true;

            LoadCustomTypes();
            RebuildRows();
            Content = BuildRoot();
        }

        /// <summary>
        /// Показывает окно модально (синхронно). Открытая публичная обёртка над
        /// <see cref="ModalWindowBase.ShowDialogSync(Window?)"/>, чтобы диалог можно было
        /// вызывать из ViewModel (не наследника окна).
        /// </summary>
        public bool ShowSync(Window? owner = null) => ShowDialogSync(owner);

        private static string T(string key) => LocalizationManager.T(key);

        private void LoadCustomTypes()
        {
            try
            {
                _customTypes.Clear();
                foreach (var ct in _store.Load())
                    _customTypes.Add(ct);
            }
            catch (Exception ex)
            {
                _logger.Error("Ошибка загрузки списка пользовательских типовых конфигураций", ex);
            }
        }

        /// <summary>Формирует строки таблицы: сначала предопределённые (только для чтения), затем пользовательские.</summary>
        private void RebuildRows()
        {
            _rows.Clear();
            _rowsPanel.Children.Clear();
            foreach (var ct in BuiltInConfigTypes.All)
                AddRow(new ConfigTypeItemViewModel(ct, OnEditRow, OnDeleteRow));
            foreach (var ct in _customTypes)
                AddRow(new ConfigTypeItemViewModel(ct, OnEditRow, OnDeleteRow));
        }

        private void AddRow(ConfigTypeItemViewModel row)
        {
            _rows.Add(row);
            var index = _rowsPanel.Children.Count;
            _rowsPanel.Children.Add(BuildRow(row, index));
        }

        /// <summary>Строит визуальную строку таблицы конфигураций.</summary>
        private Grid BuildRow(ConfigTypeItemViewModel row, int index)
        {
            var grid = new Grid { Margin = new Thickness(0, 2, 0, 2) };

            // Подсветка только нечётных строк (1-я, 3-я, 5-я…): индекс строки начинается с 0,
            // поэтому нечётной позиции соответствует чётный индекс. Hover подсвечивает текущую строку.
            var oddRow = index % 2 == 0;
            var bandBrush = oddRow ? (TryBrush("ItemHoverBrush") ?? Brushes.Transparent) : Brushes.Transparent;
            var hoverBrush = TryBrush("ItemSelectedBrush") ?? Brushes.Transparent;
            grid.Background = bandBrush;
            grid.PointerEntered += (_, _) => grid.Background = hoverBrush;
            grid.PointerExited += (_, _) => grid.Background = bandBrush;

            grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(2, GridUnitType.Star)));
            grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(2, GridUnitType.Star)));
            grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(3, GridUnitType.Star)));
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));

            var name = new TextBlock
            {
                Text = row.Name,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 8, 0)
            };
            if (row.IsBuiltIn)
                Themes.ThemeBrushes.Bind(name, TextBlock.ForegroundProperty, "TextSecondaryBrush");
            Grid.SetColumn(name, 0);
            grid.Children.Add(name);

            var urlCode = new TextBlock
            {
                Text = row.UrlCode,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 8, 0)
            };
            Grid.SetColumn(urlCode, 1);
            grid.Children.Add(urlCode);

            var editions = new TextBlock
            {
                Text = row.EditionsSummary,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 8, 0)
            };
            Grid.SetColumn(editions, 2);
            grid.Children.Add(editions);

            // Предопределённые конфигурации только для чтения: кнопки «Изменить»/«Удалить»
            // для них не выводятся (issue #321).
            if (!row.IsBuiltIn)
            {
                var edit = new Button
                {
                    Content = T("Updates.Edit"),
                    VerticalAlignment = VerticalAlignment.Center,
                    Padding = new Thickness(10, 4),
                    Margin = new Thickness(4, 0, 4, 0)
                };
                edit.Styled(ControlThemes.SelectAllButton);
                edit.Click += (_, _) => OnEditRow(row);
                Grid.SetColumn(edit, 3);
                grid.Children.Add(edit);

                var delete = new Button
                {
                    Content = T("Updates.Delete"),
                    VerticalAlignment = VerticalAlignment.Center,
                    Padding = new Thickness(10, 4),
                    Margin = new Thickness(4, 0, 4, 0)
                };
                delete.Styled(ControlThemes.SelectAllButton);
                delete.Click += (_, _) => OnDeleteRow(row);
                Grid.SetColumn(delete, 4);
                grid.Children.Add(delete);
            }

            return grid;
        }

        /// <summary>Открывает отдельное окно правки пользовательской конфигурации.</summary>
        private void OnEditRow(ConfigTypeItemViewModel row)
        {
            if (row.IsBuiltIn)
                return; // Предопределённые конфигурации только для чтения.

            var edit = new ConfigTypeEditWindow(row.Model);
            if (!edit.ShowSync(this) || edit.Result is not { } updated)
                return; // Отмена — модель не изменялась (правка велась на копии).

            ApplyTo(row.Model, updated);
            Save();
            RebuildRows();
        }

        /// <summary>Открывает отдельное окно создания новой пользовательской конфигурации.</summary>
        private void OnAddConfigClick()
        {
            var edit = new ConfigTypeEditWindow();
            if (!edit.ShowSync(this) || edit.Result is not { } created)
                return;

            created.IsBuiltIn = false;
            created.IsTracked = true;
            _customTypes.Add(created);
            Save();
            RebuildRows();
        }

        private void OnDeleteRow(ConfigTypeItemViewModel row)
        {
            if (row.IsBuiltIn)
                return; // Предопределённые конфигурации нельзя удалить.

            if (!_dialogs.Confirm(T("Updates.ConfirmDelete"), T("Updates.ConfigTypesTitle")))
                return;
            try
            {
                _customTypes.Remove(row.Model);
                Save();
                RebuildRows();
            }
            catch (Exception ex)
            {
                _logger.Error($"Ошибка удаления конфигурации «{row.Name}»", ex);
            }
        }

        /// <summary>Сразу сохраняет пользовательские конфигурации в файл custom_config_types.json.</summary>
        private void Save()
        {
            try
            {
                _store.Save(_customTypes);
            }
            catch (Exception ex)
            {
                _logger.Error("Ошибка сохранения списка типовых конфигураций", ex);
            }
        }

        /// <summary>Переносит отредактированную копию в модель строки (изменение одной строки не
        /// влияет на другие конфигурации — правка велась на отдельном экземпляре).</summary>
        private static void ApplyTo(OneCConfigType target, OneCConfigType source)
        {
            target.Code = source.Code;
            target.Name = source.Name;
            target.UrlCode = source.UrlCode;
            target.Nick = source.Nick;
            target.Editions.Clear();
            target.Editions.AddRange(source.Editions);
        }

        private Control BuildRoot()
        {
            var grid = new Grid { Margin = new Thickness(16) };
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            grid.RowDefinitions.Add(new RowDefinition(new GridLength(1, GridUnitType.Star)));
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

            var title = new TextBlock
            {
                Text = T("Updates.ConfigTypesTitle"),
                FontSize = 15,
                FontWeight = FontWeight.SemiBold
            };
            Grid.SetRow(title, 0);
            grid.Children.Add(title);

            // Пояснения (issue #321): что это за окно, что нельзя менять предопределённые,
            // где хранятся пользовательские конфигурации.
            var explainPanel = new StackPanel { Spacing = 4, Margin = new Thickness(0, 6, 0, 0) };
            var explain = new TextBlock
            {
                Text = T("Updates.ConfigTypesExplanation"),
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap
            };
            Themes.ThemeBrushes.Bind(explain, TextBlock.ForegroundProperty, "TextSecondaryColorBrush");
            explainPanel.Children.Add(explain);

            var storageHint = new TextBlock
            {
                Text = T("Updates.CustomStorageHint"),
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap
            };
            Themes.ThemeBrushes.Bind(storageHint, TextBlock.ForegroundProperty, "TextSecondaryColorBrush");
            explainPanel.Children.Add(storageHint);
            Grid.SetRow(explainPanel, 1);
            grid.Children.Add(explainPanel);

            // Карточка списка конфигураций.
            var listBorder = new Border
            {
                Margin = new Thickness(0, 12, 0, 0),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6)
            };
            Themes.ThemeBrushes.Bind(listBorder, Border.BackgroundProperty, "CardBackgroundColorBrush");
            Themes.ThemeBrushes.Bind(listBorder, Border.BorderBrushProperty, "BorderColorBrush");

            _rowsPanel.Margin = new Thickness(4, 2);
            var header = BuildHeaderGrid();

            var scroll = new ScrollViewer
            {
                Content = _rowsPanel,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Padding = new Thickness(4)
            };

            _listView.Children.Clear();
            var addButton = new Button { Content = T("Updates.AddConfig"), Height = 32 };
            addButton.Styled(ControlThemes.ModernButton);
            addButton.Click += (_, _) => OnAddConfigClick();

            var toolbar = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Margin = new Thickness(8, 8, 8, 4)
            };
            toolbar.Children.Add(addButton);

            var toolbarBorder = new Border
            {
                Padding = new Thickness(8, 4),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Background = Brushes.Transparent,
                Child = toolbar
            };
            Themes.ThemeBrushes.Bind(toolbarBorder, Border.BorderBrushProperty, "BorderColorBrush");
            DockPanel.SetDock(toolbarBorder, Dock.Top);
            _listView.Children.Add(toolbarBorder);

            DockPanel.SetDock(header, Dock.Top);
            _listView.Children.Add(header);
            _listView.Children.Add(scroll);
            listBorder.Child = _listView;

            Grid.SetRow(listBorder, 2);
            grid.Children.Add(listBorder);

            // Нижняя панель: «Добавить» / «Закрыть» (закрывает только окно списка).
            var bottom = new Grid { Margin = new Thickness(0, 14, 0, 0) };
            bottom.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
            bottom.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            var addBottom = new Button { Content = T("Updates.AddConfig"), Height = 34 };
            addBottom.Styled(ControlThemes.ModernButton);
            addBottom.Click += (_, _) => OnAddConfigClick();
            Grid.SetColumn(addBottom, 0);
            bottom.Children.Add(addBottom);

            var close = BuildCancelActionButton(140);
            close.Click += (_, _) => Close();
            Grid.SetColumn(close, 1);
            bottom.Children.Add(close);

            Grid.SetRow(bottom, 3);
            grid.Children.Add(bottom);

            return grid;
        }

        /// <summary>Заголовок таблицы конфигураций.</summary>
        private Grid BuildHeaderGrid()
        {
            var grid = new Grid { Margin = new Thickness(8, 0, 8, 2) };
            grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(2, GridUnitType.Star)));
            grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(2, GridUnitType.Star)));
            grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(3, GridUnitType.Star)));
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));

            grid.Children.Add(MakeHeaderText(T("Updates.Name"), 0));
            grid.Children.Add(MakeHeaderText(T("Updates.UrlCode"), 1));
            grid.Children.Add(MakeHeaderText(T("Updates.Editions"), 2));
            return grid;
        }

        private static IBrush? TryBrush(string key)
        {
            if (Application.Current is not { } app || !app.TryFindResource(key, out var found))
                return null;
            return found as IBrush;
        }

        private static TextBlock MakeHeaderText(string text, int column)
        {
            var block = new TextBlock
            {
                Text = text,
                FontSize = 12,
                FontWeight = FontWeight.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 8, 0)
            };
            Themes.ThemeBrushes.Bind(block, TextBlock.ForegroundProperty, "TextSecondaryBrush");
            Grid.SetColumn(block, column);
            return block;
        }
    }
}
#endif