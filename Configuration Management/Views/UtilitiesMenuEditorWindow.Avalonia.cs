#if LINUX
using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Configuration_Management.Localization;
using Configuration_Management.Models;
using Configuration_Management.Services;
using Configuration_Management.Themes;
using Configuration_Management.ViewModels;

namespace Configuration_Management
{
    /// <summary>
    /// Редактор структуры меню «Утилиты» (issue #374): Avalonia/Linux-версия
    /// окна <see cref="UtilitiesMenuEditorWindow"/> (WPF). Дерево компоновки с
    /// перемещением пунктов, отключением, группировкой, назначением иконок
    /// (из каталога/внешнего файла), сбросом к стандартной компоновке и
    /// сохранением/загрузкой в файл <c>utilities_menu_layout.json</c>.
    /// </summary>
    public partial class UtilitiesMenuEditorWindow : ModalWindowBase
    {
        private readonly UtilitiesMenuEditorViewModel _vm;
        private readonly TreeView _tree = new();
        private readonly ComboBox _groupCombo = new ComboBox().Styled(ControlThemes.ModernComboBox);
        private readonly ComboBox _iconCombo = new ComboBox().Styled(ControlThemes.ModernComboBox);

        /// <param name="store">Хранилище компоновки (или null — редактирование без сохранения).</param>
        public UtilitiesMenuEditorWindow(IUtilitiesMenuLayoutStore? store)
        {
            Title = LocalizationManager.T("Utilities.Editor.Title");
            Width = 760;
            Height = 560;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            _vm = new UtilitiesMenuEditorViewModel(store);
            DataContext = _vm;
            Content = BuildLayout();

            if (_vm.Groups.Count > 0)
                _vm.SelectedGroup = _vm.Groups[0];
        }

        /// <summary>Компоновка после редактирования (актуальна после «Готово»).</summary>
        public UtilitiesMenuLayout Layout => _vm.Layout;

        private Control BuildLayout()
        {
            var dock = new DockPanel { Margin = new Thickness(16) };

            // Кнопки «Готово»/«Отмена» внизу.
            var buttons = BuildButtons(
                okText: LocalizationManager.T("Utilities.Editor.Done"), onOk: OnOkClick);
            DockPanel.SetDock(buttons, Dock.Bottom);
            buttons.Margin = new Thickness(0, 12, 0, 0);
            dock.Children.Add(buttons);

            // Дерево слева.
            var treeHeader = new TextBlock
            {
                Text = LocalizationManager.T("Utilities.Editor.TreeHeader"),
                Foreground = new SolidColorBrush(Color.Parse("#94A3B8")),
                Margin = new Thickness(4, 4, 4, 8)
            };
            DockPanel.SetDock(treeHeader, Dock.Top);
            dock.Children.Add(treeHeader);

            var treeBorder = new Border
            {
                Background = TryBrush("CardBackgroundBrush"),
                BorderBrush = TryBrush("BorderColorBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(4),
                Margin = new Thickness(0, 0, 12, 0)
            };
            dock.Children.Add(treeBorder);

            BuildTree();
            treeBorder.Child = _tree;

            // Правая панель операций.
            var panel = BuildOperationsPanel();
            dock.Children.Add(panel);

            return dock;
        }

        private void BuildTree()
        {
            _tree.ItemsSource = _vm.Roots;
            _tree.SelectionMode = SelectionMode.Single;
            _tree.ItemTemplate = new FuncTreeDataTemplate(
                typeof(UtilitiesMenuNodeItem),
                (item, _) => BuildTreeRow(item as UtilitiesMenuNodeItem),
                item => item is UtilitiesMenuNodeItem node
                    ? node.Children
                    : Array.Empty<UtilitiesMenuNodeItem>());
            if (Application.Current?.TryFindResource(ControlThemes.ModernTreeItem, out var theme) == true
                && theme is ControlTheme itemTheme)
            {
                _tree.ItemContainerTheme = itemTheme;
            }
            _tree.SelectionChanged += (_, _) =>
                _vm.SelectedItem = _tree.SelectedItem as UtilitiesMenuNodeItem;
        }

        /// <summary>Строка узла: чекбокс видимости + заголовок + пометка вида.</summary>
        private Control BuildTreeRow(UtilitiesMenuNodeItem? node)
        {
            if (node is null)
                return new TextBlock { Text = string.Empty };
            var check = new CheckBox().Styled(ControlThemes.CacheCleanCheckBox);
            check.Bind(ToggleButton.IsCheckedProperty, new Binding("IsVisible"));
            var title = new TextBlock { Text = node.Title, Margin = new Thickness(6, 0, 0, 0) };
            title.Bind(TextBlock.TextProperty, new Binding("Title"));
            var kind = new TextBlock
            {
                Text = node.KindLabel,
                FontSize = 11,
                Margin = new Thickness(8, 0, 0, 0),
                Foreground = new SolidColorBrush(Color.Parse("#94A3B8"))
            };
            kind.Bind(TextBlock.TextProperty, new Binding("KindLabel"));
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 0 };
            row.Children.Add(check);
            row.Children.Add(title);
            row.Children.Add(kind);
            return row;
        }

        private Control BuildOperationsPanel()
        {
            var panel = new StackPanel { Spacing = 6, Width = 250 };

            // Перемещение.
            panel.Children.Add(SectionHeader("Utilities.Editor.MoveHeader"));
            var moveRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            moveRow.Children.Add(ActionButton("IconArrowUp", "Utilities.Editor.MoveUpTooltip", _vm.MoveUpCommand));
            moveRow.Children.Add(ActionButton("IconArrowDown", "Utilities.Editor.MoveDownTooltip", _vm.MoveDownCommand));
            moveRow.Children.Add(ActionButton("IconFolderPlus", "Utilities.Editor.AddGroupTooltip", _vm.AddGroupCommand));
            panel.Children.Add(moveRow);

            // Поместить в группу.
            panel.Children.Add(SectionHeader("Utilities.Editor.MoveToGroupHeader", top: 14));
            _groupCombo.ItemsSource = _vm.Groups;
            _groupCombo.ItemTemplate = new FuncDataTemplate<UtilitiesMenuNodeItem>(
                (g, _) => new TextBlock { Text = g.GroupLabel });
            _groupCombo.Bind(SelectingItemsControl.SelectedItemProperty, new Binding("SelectedGroup"));
            var groupRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            groupRow.Children.Add(_groupCombo);
            groupRow.Children.Add(ActionButton("IconFolderMove", null, _vm.MoveToGroupCommand, caption: "Utilities.Editor.MoveToGroup"));
            panel.Children.Add(groupRow);

            // Иконки.
            panel.Children.Add(SectionHeader("Utilities.Editor.IconHeader", top: 14));
            _iconCombo.ItemsSource = _vm.CatalogIcons;
            _iconCombo.ItemTemplate = new FuncDataTemplate<UtilitiesIconOption>(
                (o, _) => new TextBlock { Text = o.Label });
            // В Avalonia нет SelectedValuePath: ключ выбранной иконки пишем в VM вручную.
            _iconCombo.SelectionChanged += (_, _) =>
            {
                if (_iconCombo.SelectedItem is UtilitiesIconOption option)
                    _vm.SelectedIconKey = option.Key;
            };
            var iconRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            iconRow.Children.Add(_iconCombo);
            var applyIconButton = ActionButton("IconOk", null, _vm.SetCatalogIconCommand, caption: "Utilities.Editor.ApplyIcon");
            applyIconButton.Bind(Button.CommandParameterProperty, new Binding("SelectedIconKey"));
            iconRow.Children.Add(applyIconButton);
            panel.Children.Add(iconRow);

            var iconRow2 = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            iconRow2.Children.Add(TextButton("Utilities.Editor.ExternalIcon", OnExternalIconClick));
            iconRow2.Children.Add(ActionButton(null, null, _vm.ClearIconCommand, caption: "Utilities.Editor.ClearIcon"));
            panel.Children.Add(iconRow2);

            // Сохранение/загрузка/сброс.
            panel.Children.Add(SectionHeader("Utilities.Editor.FileHeader", top: 14));
            var fileRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            fileRow.Children.Add(ActionButton("IconSave", null, _vm.SaveCommand, caption: "Utilities.Editor.Save"));
            fileRow.Children.Add(ActionButton("IconFolderDownload", null, _vm.LoadCommand, caption: "Utilities.Editor.Load"));
            fileRow.Children.Add(ActionButton("IconRestore", null, _vm.ResetDefaultCommand, caption: "Utilities.Editor.Reset"));
            panel.Children.Add(fileRow);

            // Пояснение.
            var hint = new Border
            {
                Background = TryBrush("CardBackgroundBrush"),
                BorderBrush = TryBrush("BorderColorBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(10),
                Margin = new Thickness(0, 14, 0, 0)
            };
            hint.Child = new TextBlock
            {
                Text = LocalizationManager.T("Utilities.Editor.Hint"),
                TextWrapping = TextWrapping.Wrap,
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.Parse("#94A3B8"))
            };
            panel.Children.Add(hint);

            return panel;
        }

        private TextBlock SectionHeader(string key, double top = 0)
        {
            var text = new TextBlock
            {
                Text = LocalizationManager.T(key),
                Margin = new Thickness(0, top, 0, 4),
                Foreground = new SolidColorBrush(Color.Parse("#94A3B8"))
            };
            return text;
        }

        private Button ActionButton(string? iconKey, string? tooltipKey, System.Windows.Input.ICommand command, string? caption = null)
        {
            var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            if (iconKey is not null)
                content.Children.Add(IconHelper.MakeIcon(iconKey, 16, "TextPrimaryBrush"));
            if (caption is not null)
                content.Children.Add(new TextBlock { Text = LocalizationManager.T(caption), VerticalAlignment = VerticalAlignment.Center });
            var button = new Button
            {
                Content = content,
                Padding = new Thickness(10, 6),
                VerticalAlignment = VerticalAlignment.Center
            };
            button.Styled(ControlThemes.ModernButton);
            if (tooltipKey is not null)
                ToolTip.SetTip(button, LocalizationManager.T(tooltipKey));
            button.Command = command;
            return button;
        }

        private Button TextButton(string captionKey, Action onClick)
        {
            var button = new Button
            {
                Content = new TextBlock
                {
                    Text = LocalizationManager.T(captionKey),
                    VerticalAlignment = VerticalAlignment.Center
                },
                Padding = new Thickness(10, 6),
                VerticalAlignment = VerticalAlignment.Center
            };
            button.Styled(ControlThemes.ModernButton);
            button.Click += (_, _) => onClick();
            return button;
        }

        /// <summary>Иконка из внешнего файла: системный диалог выбора файла.</summary>
        private void OnExternalIconClick()
        {
            try
            {
                var dialogs = AppServices.GetRequiredService<IDialogService>();
                var path = dialogs.OpenFileDialog(
                    LocalizationManager.T("Utilities.Editor.ExternalIconDialogTitle"),
                    LocalizationManager.T("Utilities.Editor.ExternalIconFilter"));
                if (!string.IsNullOrWhiteSpace(path))
                    _vm.SetExternalIconCommand.Execute(path);
            }
            catch
            {
                // Диалог недоступен — просто не назначаем иконку.
            }
        }

        private void OnOkClick()
        {
            _vm.SaveCommand.Execute(null);
            DialogResult = true;
            Close();
        }

        private IBrush? TryBrush(string key)
        {
            if (Application.Current is not { } app || !app.TryFindResource(key, out var found))
                return null;
            return found as IBrush;
        }
    }
}
#endif