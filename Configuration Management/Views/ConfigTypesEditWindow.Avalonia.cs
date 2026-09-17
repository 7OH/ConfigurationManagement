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
    /// Окно редактирования списка типовых конфигураций 1С: позволяет добавить, изменить и удалить
    /// пользовательские конфигурации (имя, сегмент URL и редакции/каталоги релизов). Предопределённые
    /// конфигурации из <see cref="BuiltInConfigTypes"/> показываются только для чтения. Список
    /// пользовательских конфигураций загружается из и сохраняется в <see cref="AppSettings.CustomConfigTypes"/>
    /// через репозиторий при закрытии окна. Avalonia/Linux-версия WPF-окна <see cref="ConfigTypesEditWindow"/>.
    /// </summary>
    public sealed class ConfigTypesEditWindow : ModalWindowBase
    {
        private readonly Services.IInfobaseRepository _repository = AppServices.GetRequiredService<Services.IInfobaseRepository>();
        private readonly Services.IAppLogger _logger = AppServices.GetRequiredService<Services.IAppLogger>();
        private readonly IDialogService _dialogs = AppServices.GetRequiredService<IDialogService>();

        private readonly List<OneCConfigType> _customTypes = new();
        private readonly List<ConfigTypeItemViewModel> _rows = new();
        private readonly List<OneCConfigEdition> _editions = new();

        private ConfigTypeItemViewModel? _editingRow;
        private bool _editingIsNew;

        // Списки строк и редактор переключаются видимостью, как две панели WPF-разметки.
        private readonly StackPanel _rowsPanel = new();
        private readonly DockPanel _listView = new() { LastChildFill = true };
        private Control? _editorView;

        // Поля редактора конфигурации.
        private readonly TextBlock _editorTitle = new();
        private TextBox _nameBox = new();
        private TextBox _urlCodeBox = new();
        private ComboBox _editionsList = new();
        private TextBox _editionNameBox = new();
        private TextBox _editionRedBox = new();
        private TextBox _editionSubRedBox = new();
        private TextBox _editionUrlOverrideBox = new();
        private Button _saveButton = new();
        private Button _cancelButton = new();

        private bool _editionSyncing;

        /// <summary>
        /// Открывает окно редактирования списка типовых конфигураций.
        /// </summary>
        /// <param name="initialCustomTypes">Необязательные начальные пользовательские конфигурации.
        /// Если не заданы — загружаются из настроек репозитория.</param>
        public ConfigTypesEditWindow(IEnumerable<OneCConfigType>? initialCustomTypes = null)
        {
            Title = LocalizationManager.T("Updates.ConfigTypesTitle");
            Width = 760;
            Height = 560;
            MinWidth = 620;
            MinHeight = 440;
            FontSize = 13;
            CanResize = true;

            if (initialCustomTypes is not null)
            {
                foreach (var ct in initialCustomTypes)
                    _customTypes.Add(ct);
            }
            else
            {
                LoadCustomTypes();
            }

            RebuildRows();
            Content = BuildRoot();
            Closing += (_, _) => SaveCustomTypes();
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
                var settings = _repository.LoadSettings();
                _customTypes.Clear();
                foreach (var ct in settings.CustomConfigTypes ?? new List<OneCConfigType>())
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
            _rowsPanel.Children.Add(BuildRow(row));
        }

        /// <summary>Строит визуальную строку таблицы конфигураций.</summary>
        private Grid BuildRow(ConfigTypeItemViewModel row)
        {
            var grid = new Grid { Margin = new Thickness(0, 2, 0, 2) };
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

            if (!row.IsBuiltIn)
            {
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

        private void OnEditRow(ConfigTypeItemViewModel row)
        {
            OpenEditor(row, isNew: false);
        }

        private void OnDeleteRow(ConfigTypeItemViewModel row)
        {
            if (!_dialogs.Confirm(T("Updates.ConfirmDelete"), T("Updates.ConfigTypesTitle")))
                return;
            try
            {
                _customTypes.Remove(row.Model);
                _rows.Remove(row);
                RebuildRows();
            }
            catch (Exception ex)
            {
                _logger.Error($"Ошибка удаления конфигурации «{row.Name}»", ex);
            }
        }

        private void OnAddConfigClick()
        {
            var config = new OneCConfigType { IsBuiltIn = false, IsTracked = true };
            _customTypes.Add(config);
            var row = new ConfigTypeItemViewModel(config, OnEditRow, OnDeleteRow);
            _rows.Add(row);
            _rowsPanel.Children.Add(BuildRow(row));
            OpenEditor(row, isNew: true);
        }

        private void OpenEditor(ConfigTypeItemViewModel row, bool isNew)
        {
            _editingRow = row;
            _editingIsNew = isNew;

            _editorTitle.Text = isNew ? T("Updates.AddConfig") : T("Updates.EditConfig");

            _nameBox.Text = row.Model.Name;
            _urlCodeBox.Text = row.Model.UrlCode;

            _editions.Clear();
            foreach (var ed in row.Model.Editions)
                _editions.Add(ed);
            if (_editions.Count > 0)
                _editionsList.SelectedIndex = 0;
            ClearEditionFields();

            _listView.IsVisible = false;
            _editorView!.IsVisible = true;
            _nameBox.Focus();
        }

        private void OnEditorSaveClick()
        {
            if (_editingRow is null)
                return;

            CommitEditionFields();

            var name = _nameBox.Text?.Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                _dialogs.ShowWarning(T("Updates.NoConfigSelected"), T("Updates.ConfigTypesTitle"));
                _nameBox.Focus();
                return;
            }

            var model = _editingRow.Model;
            model.Name = name;
            model.UrlCode = _urlCodeBox.Text?.Trim() ?? string.Empty;
            model.Editions.Clear();
            foreach (var ed in _editions)
                model.Editions.Add(ed);

            _editingRow.Refresh();
            CloseEditor();
        }

        private void OnEditorCancelClick()
        {
            if (_editingIsNew && _editingRow is not null)
            {
                // Новая конфигурация, добавленная на «Добавить», но не сохранённая — убрать её.
                _customTypes.Remove(_editingRow.Model);
                _rows.Remove(_editingRow);
            }
            CloseEditor();
        }

        private void CloseEditor()
        {
            _editingRow = null;
            _editingIsNew = false;
            _editorView!.IsVisible = false;
            _listView.IsVisible = true;
            RebuildRows();
        }

        private void OnAddEditionClick()
        {
            CommitEditionFields();
            var edition = new OneCConfigEdition { Name = T("Updates.Name") };
            _editions.Add(edition);
            _editionsList.SelectedIndex = _editions.Count - 1;
        }

        private void OnRemoveEditionClick()
        {
            if (_editionsList.SelectedItem is not OneCConfigEdition edition)
                return;
            var index = _editions.IndexOf(edition);
            _editions.Remove(edition);
            ClearEditionFields();
            if (_editions.Count > 0)
                _editionsList.SelectedIndex = Math.Min(index, _editions.Count - 1);
        }

        private void OnEditionSelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (_editionSyncing)
                return;
            CommitEditionFields();
            if (_editionsList.SelectedItem is OneCConfigEdition edition)
                LoadEditionFields(edition);
            else
                ClearEditionFields();
        }

        /// <summary>Переносит значения полей редактора в выбранную редакцию.</summary>
        private void CommitEditionFields()
        {
            if (_editionsList.SelectedItem is not OneCConfigEdition edition)
                return;
            edition.Name = _editionNameBox.Text?.Trim() ?? string.Empty;
            edition.Red = _editionRedBox.Text?.Trim() ?? string.Empty;
            edition.SubRed = _editionSubRedBox.Text?.Trim() ?? string.Empty;
            edition.UrlOverride = _editionUrlOverrideBox.Text?.Trim() ?? string.Empty;
        }

        private void LoadEditionFields(OneCConfigEdition edition)
        {
            _editionSyncing = true;
            try
            {
                _editionNameBox.Text = edition.Name;
                _editionRedBox.Text = edition.Red;
                _editionSubRedBox.Text = edition.SubRed;
                _editionUrlOverrideBox.Text = edition.UrlOverride;
            }
            finally
            {
                _editionSyncing = false;
            }
        }

        private void ClearEditionFields()
        {
            _editionSyncing = true;
            try
            {
                _editionNameBox.Text = string.Empty;
                _editionRedBox.Text = string.Empty;
                _editionSubRedBox.Text = string.Empty;
                _editionUrlOverrideBox.Text = string.Empty;
            }
            finally
            {
                _editionSyncing = false;
            }
        }

        private void SaveCustomTypes()
        {
            try
            {
                var settings = _repository.LoadSettings();
                settings.CustomConfigTypes = new List<OneCConfigType>(_customTypes);
                _repository.SaveSettings(settings);
            }
            catch (Exception ex)
            {
                _logger.Error("Ошибка сохранения списка типовых конфигураций", ex);
            }
        }

        private Control BuildRoot()
        {
            var grid = new Grid { Margin = new Thickness(16) };
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

            // Верхняя карточка списка.
            var listBorder = new Border
            {
                Margin = new Thickness(0, 12, 0, 0),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6)
            };
            Themes.ThemeBrushes.Bind(listBorder, Border.BackgroundProperty, "CardBackgroundColorBrush");
            Themes.ThemeBrushes.Bind(listBorder, Border.BorderBrushProperty, "BorderColorBrush");

            // Панель списка: заголовок + скролл строк.
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
            var addButton = new Button { Content = T("Updates.Add"), Height = 32 };
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

            Grid.SetRow(listBorder, 1);
            grid.Children.Add(listBorder);

            // Панель редактора (переключается видимостью со списком).
            _editorView = BuildEditorPanel();
            _editorView!.IsVisible = false;
            Grid.SetRow(_editorView, 1);
            grid.Children.Add(_editorView);

            // Нижняя панель: кнопка закрытия.
            var bottom = new Grid { Margin = new Thickness(0, 14, 0, 0) };
            bottom.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
            bottom.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            var close = BuildCancelActionButton(140);
            close.Click += (_, _) => Close();
            Grid.SetColumn(close, 1);
            bottom.Children.Add(close);

            Grid.SetRow(bottom, 2);
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

        /// <summary>Форма редактирования одной конфигурации (имя, сегмент URL и редакции).</summary>
        private Control BuildEditorPanel()
        {
            var panel = new StackPanel { Margin = new Thickness(0, 12, 0, 0), Spacing = 8 };

            _editorTitle.FontSize = 15;
            _editorTitle.FontWeight = FontWeight.SemiBold;
            panel.Children.Add(_editorTitle);

            // Общие поля конфигурации.
            _nameBox = MakeTextBox(T("Updates.Name"));
            _urlCodeBox = MakeTextBox(T("Updates.UrlCode"));
            panel.Children.Add(MakeFieldRow(T("Updates.Name"), _nameBox));
            panel.Children.Add(MakeFieldRow(T("Updates.UrlCode"), _urlCodeBox));

            // Список редакций и поля выбранной редакции.
            _editionsList = new ComboBox
            {
                ItemsSource = _editions,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                MinHeight = 34
            };
            _editionsList.SelectionChanged += OnEditionSelectionChanged;

            var editionButtons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            var addEd = new Button { Content = T("Updates.Add"), Height = 30 };
            addEd.Styled(ControlThemes.SelectAllButton);
            addEd.Click += (_, _) => OnAddEditionClick();
            var removeEd = new Button { Content = T("Updates.Delete"), Height = 30 };
            removeEd.Styled(ControlThemes.SelectAllButton);
            removeEd.Click += (_, _) => OnRemoveEditionClick();
            editionButtons.Children.Add(addEd);
            editionButtons.Children.Add(removeEd);

            var editionRow = new Grid();
            editionRow.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
            editionRow.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            _editionsList.Margin = new Thickness(0);
            Grid.SetColumn(_editionsList, 0);
            editionRow.Children.Add(_editionsList);
            Grid.SetColumn(editionButtons, 1);
            editionButtons.Margin = new Thickness(8, 0, 0, 0);
            editionRow.Children.Add(editionButtons);
            panel.Children.Add(MakeFieldRow(T("Updates.Editions"), editionRow));

            // Поля выбранной редакции размещаются одним рядом без подписей, как в разметке
            // (ConfigTypesEditWindow.xaml:217-220): имя, «Ред», «Подред» и переопределённый URL.
            _editionNameBox = MakeTextBox(T("Updates.Name"));
            _editionRedBox = MakeTextBox(string.Empty);
            _editionSubRedBox = MakeTextBox(string.Empty);
            _editionUrlOverrideBox = MakeTextBox(string.Empty);
            panel.Children.Add(BuildEditionFieldsRow());

            // Кнопки редактора.
            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Spacing = 10,
                Margin = new Thickness(0, 8, 0, 0)
            };
            _saveButton = new Button { Content = T("Updates.Save"), Width = 130, Height = 36, IsDefault = true };
            _saveButton.Styled(ControlThemes.DialogConfirmButton);
            _saveButton.Click += (_, _) => OnEditorSaveClick();

            _cancelButton = BuildCancelActionButton(130);
            _cancelButton.Click += (_, _) => OnEditorCancelClick();
            buttons.Children.Add(_saveButton);
            buttons.Children.Add(_cancelButton);
            panel.Children.Add(buttons);

            return panel;
        }

        private static TextBox MakeTextBox(string watermark)
        {
            var tb = new TextBox
            {
                Watermark = watermark,
                Margin = new Thickness(0),
                VerticalContentAlignment = VerticalAlignment.Center,
                MinHeight = 34
            };
            tb.Styled(ControlThemes.ModernTextBox);
            return tb;
        }

        /// <summary>Ряд из четырёх полей редакции: имя, «Ред», «Подред» и переопределённый URL.</summary>
        private Grid BuildEditionFieldsRow()
        {
            var grid = new Grid { Margin = new Thickness(0, 3) };
            for (var i = 0; i < 4; i++)
                grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));

            var fields = new[] { _editionNameBox, _editionRedBox, _editionSubRedBox, _editionUrlOverrideBox };
            for (var i = 0; i < fields.Length; i++)
            {
                fields[i].Margin = i < fields.Length - 1 ? new Thickness(0, 0, 6, 0) : new Thickness(0);
                Grid.SetColumn(fields[i], i);
                grid.Children.Add(fields[i]);
            }
            return grid;
        }

        /// <summary>Строка «подпись / поле» формы редактора.</summary>
        private static Grid MakeFieldRow(string labelKey, Control field)
        {
            var grid = new Grid { Margin = new Thickness(0, 3) };
            grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(150)));
            grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
            var label = new TextBlock
            {
                Text = LocalizationManager.T(labelKey),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0)
            };
            Grid.SetColumn(label, 0);
            grid.Children.Add(label);
            Grid.SetColumn(field, 1);
            grid.Children.Add(field);
            return grid;
        }
    }
}
#endif