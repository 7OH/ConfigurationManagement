#if LINUX
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Configuration_Management.Localization;
using Configuration_Management.Models;
using Configuration_Management.Services;
using Configuration_Management.Themes;
using Configuration_Management.ViewModels;

namespace Configuration_Management
{
    /// <summary>
    /// Диалог «Поиск потерянных и забытых баз 1С 8» (issue #247), Avalonia/Linux-версия
    /// WPF-окна <see cref="FindLostBasesWindow"/>. Выбор дисков/корней поиска, фоновое
    /// рекурсивное сканирование в поисках файлов <c>1Cv8.1CD</c> с кнопкой «Прекратить»,
    /// таблица найденных баз и добавление отсутствующих в списке приложения баз.
    /// </summary>
    public sealed class FindLostBasesWindow : ModalWindowBase
    {
        private readonly IReadOnlyList<Infobase> _infobases;
        private readonly Action<Infobase> _addBase;
        private readonly HashSet<string> _appFileDirs = new(FoundBaseRowViewModel.DirComparer);
        private readonly HashSet<string> _v8iFileDirs = new(FoundBaseRowViewModel.DirComparer);
        private readonly List<FoundBaseRowViewModel> _rows = new();
        private readonly List<CheckBox> _driveChecks = new();
        private readonly Dictionary<FoundBaseRowViewModel, CheckBox> _checks = new();
        private readonly Dictionary<FoundBaseRowViewModel, TextBlock> _inAppTexts = new();
        private readonly Dictionary<FoundBaseRowViewModel, TextBlock> _inV8iTexts = new();
        private readonly Services.IAppLogger _logger =
            AppServices.GetRequiredService<Services.IAppLogger>();

        private readonly StackPanel _rowsPanel = new() { Margin = new Thickness(4, 2) };
        private readonly TextBlock _checkedHeaderText = new();
        private readonly TextBlock _progressText = new();
        private readonly TextBlock _summaryText = new();
        private Button _searchButton = new();
        private Button _stopButton = new();
        private Button _addButton = new();

        private CancellationTokenSource? _cts;

        /// <summary>Признак того, что хотя бы одна база была добавлена (для персиста в настройках).</summary>
        public bool DataChanged { get; private set; }

        /// <param name="infobases">Список баз приложения (для признака «В приложении»).</param>
        /// <param name="addBase">Обратный вызов добавления новой базы в список приложения.</param>
        public FindLostBasesWindow(IReadOnlyList<Infobase> infobases, Action<Infobase> addBase)
        {
            Title = LocalizationManager.T("FindLostBases.Title");
            Width = 920;
            Height = 620;
            MinWidth = 720;
            MinHeight = 480;
            FontSize = 13;
            CanResize = true;
            _infobases = infobases;
            _addBase = addBase;

            BuildAppFileDirs();
            BuildV8iFileDirs();

            Content = BuildRoot();
            PopulateDriveChecks();
            UpdateCheckedHeader();
            UpdateAddEnabled();
            Closing += (_, _) => _cts?.Cancel();
        }

        private static string T(string key) => LocalizationManager.T(key);

        private static TextBlock MakeHeaderText(string text)
        {
            return new TextBlock
            {
                Text = text,
                FontSize = 12,
                FontWeight = FontWeight.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 8, 0)
            };
        }

        private static void BindSecondary(TextBlock block)
            => Themes.ThemeBrushes.Bind(block, TextBlock.ForegroundProperty, "TextSecondaryBrush");

        private void BuildAppFileDirs()
        {
            foreach (var ib in _infobases)
            {
                var dir = InfobaseMaintenanceService.GetFileBaseDirectory(ib);
                if (!string.IsNullOrWhiteSpace(dir))
                    _appFileDirs.Add(FoundBaseRowViewModel.NormalizeDir(dir));
            }
        }

        private void BuildV8iFileDirs()
        {
            var filePath = IbasesV8iImporter.FindDefaultPath();
            if (filePath is null)
                return;

            try
            {
                foreach (var ib in IbasesV8iImporter.ReadInfobases(filePath))
                {
                    if (ib.Connection.Type == ConnectionType.File
                        && !string.IsNullOrWhiteSpace(ib.Connection.FilePath))
                        _v8iFileDirs.Add(FoundBaseRowViewModel.NormalizeDir(ib.Connection.FilePath));
                }
            }
            catch (Exception ex)
            {
                _logger.Error("Не удалось прочитать записи ibases.v8i для признака «В ibases.v8i»", ex);
            }
        }

        private void PopulateDriveChecks()
        {
            var roots = InfobaseDiskScanner.EnumerateSearchRoots();
            if (roots.Count == 0)
            {
                _searchButton.IsEnabled = false;
                return;
            }

            foreach (var root in roots)
            {
                var cb = new CheckBox
                {
                    Content = root,
                    Tag = root,
                    IsChecked = true,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(6, 4, 6, 4)
                };
                _driveChecks.Add(cb);
                DrivesPanel().Children.Add(cb);
            }
        }

        /// <summary>WrapPanel корней поиска — хранится в поле для заполнения флажками.</summary>
        private readonly WrapPanel _drivesWrap = new() { Margin = new Thickness(8, 6) };

        private WrapPanel DrivesPanel() => _drivesWrap;

        private List<string> GetSelectedRoots()
            => _driveChecks.Where(c => c.IsChecked == true && c.Tag is string s && !string.IsNullOrWhiteSpace(s))
                .Select(c => (string)c.Tag!).ToList();

        private void UpdateCheckedHeader()
        {
            _checkedHeaderText.Text = string.Format(
                LocalizationManager.T("FindLostBases.Column.CheckedHeaderFormat"),
                _rows.Count(r => r.IsChecked));
        }

        private void UpdateAddEnabled()
        {
            _addButton.IsEnabled = _rows.Any(r => r.IsChecked && !r.InApp);
        }

        private void SetDriveChecksEnabled(bool enabled)
        {
            foreach (var cb in _driveChecks)
                cb.IsEnabled = enabled;
        }

        private void OnStopClick() => _cts?.Cancel();

        private void OnScanProgress(int found, int dirs)
        {
            Dispatcher.UIThread.Post(() =>
            {
                _progressText.Text = string.Format(
                    LocalizationManager.T("FindLostBases.ProgressFormat"), found, dirs);
            });
        }

        private async void OnSearchClick()
        {
            var roots = GetSelectedRoots();
            if (roots.Count == 0)
            {
                _summaryText.Text = LocalizationManager.T("FindLostBases.NoneSelected");
                return;
            }

            _searchButton.IsEnabled = false;
            _stopButton.IsEnabled = true;
            SetDriveChecksEnabled(false);
            _rows.Clear();
            _checks.Clear();
            _inAppTexts.Clear();
            _inV8iTexts.Clear();
            _rowsPanel.Children.Clear();
            _progressText.Text = string.Empty;
            _summaryText.Text = string.Empty;
            UpdateCheckedHeader();
            UpdateAddEnabled();

            _cts = new CancellationTokenSource();
            var token = _cts.Token;

            try
            {
                var results = await Task.Run(
                    () => InfobaseDiskScanner.Scan(roots, token, OnScanProgress), token);

                foreach (var found in results)
                {
                    var row = new FoundBaseRowViewModel(found)
                    {
                        InApp = _appFileDirs.Contains(FoundBaseRowViewModel.NormalizeDir(found.DirectoryPath)),
                        InIbasesV8i = _v8iFileDirs.Contains(FoundBaseRowViewModel.NormalizeDir(found.DirectoryPath))
                    };
                    _rows.Add(row);
                    AddRow(row);
                }

                _summaryText.Text = string.Format(
                    LocalizationManager.T("FindLostBases.FoundFormat"), _rows.Count);
                UpdateCheckedHeader();
                UpdateAddEnabled();
            }
            catch (OperationCanceledException)
            {
                _summaryText.Text = LocalizationManager.T("FindLostBases.Stopped");
            }
            catch (Exception ex)
            {
                _logger.Error("Ошибка сканирования дисков в поисках баз 1С", ex);
                _summaryText.Text = string.Format(LocalizationManager.T("FindLostBases.ErrorFormat"), ex.Message);
            }
            finally
            {
                _cts.Dispose();
                _cts = null;
                _searchButton.IsEnabled = true;
                _stopButton.IsEnabled = false;
                SetDriveChecksEnabled(true);
                _progressText.Text = string.Empty;
            }
        }

        private void OnAddClick()
        {
            var checkedRows = _rows.Where(r => r.IsChecked).ToList();
            var toAdd = checkedRows.Where(r => !r.InApp).ToList();
            var alreadyInList = checkedRows.Count(r => r.InApp);

            var added = 0;
            foreach (var row in toAdd)
            {
                var ib = new Infobase
                {
                    Name = row.Name,
                    Group = string.Empty,
                    Id = Guid.NewGuid().ToString("D"),
                    Connection = new ConnectionSettings
                    {
                        Type = ConnectionType.File,
                        FilePath = row.Base.DirectoryPath
                    }
                };
                _addBase(ib);

                var dirKey = FoundBaseRowViewModel.NormalizeDir(row.Base.DirectoryPath);
                _appFileDirs.Add(dirKey);
                row.InApp = true;
                row.IsChecked = false;
                DataChanged = true;
                added++;
                UpdateRow(row);
            }

            _summaryText.Text = string.Format(
                LocalizationManager.T("FindLostBases.AddedFormat"), added, alreadyInList);
            UpdateCheckedHeader();
            UpdateAddEnabled();
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
                Text = LocalizationManager.T("FindLostBases.Title"),
                FontSize = 15,
                FontWeight = FontWeight.SemiBold
            };
            Grid.SetRow(title, 0);
            grid.Children.Add(title);

            // Панель выбора дисков + кнопки Найти базы / Прекратить.
            var drivesBorder = new Border
            {
                Margin = new Thickness(0, 12, 0, 0),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6)
            };
            Themes.ThemeBrushes.Bind(drivesBorder, Border.BackgroundProperty, "CardBackgroundColorBrush");
            Themes.ThemeBrushes.Bind(drivesBorder, Border.BorderBrushProperty, "BorderColorBrush");

            var drivesDock = new DockPanel { LastChildFill = true };

            var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };

            var checkAll = new Button { Content = LocalizationManager.T("FindLostBases.Drive.All") };
            checkAll.Styled(ControlThemes.SelectAllButton);
            ToolTip.SetTip(checkAll, LocalizationManager.T("FindLostBases.Drive.AllTooltip"));
            checkAll.Click += (_, _) => _driveChecks.ForEach(c => c.IsChecked = true);

            var uncheckAll = new Button { Content = LocalizationManager.T("FindLostBases.Drive.None") };
            uncheckAll.Styled(ControlThemes.SelectAllButton);
            ToolTip.SetTip(uncheckAll, LocalizationManager.T("FindLostBases.Drive.NoneTooltip"));
            uncheckAll.Click += (_, _) => _driveChecks.ForEach(c => c.IsChecked = false);

            toolbar.Children.Add(checkAll);
            toolbar.Children.Add(uncheckAll);
            toolbar.Children.Add(new Border { Width = 1, Height = 18, Background = Brushes.Gray });

            _stopButton = new Button
            {
                Content = LocalizationManager.T("FindLostBases.Button.Stop"),
                Width = 120,
                Height = 34,
                IsEnabled = false
            };
            ToolTip.SetTip(_stopButton, LocalizationManager.T("FindLostBases.Button.StopTooltip"));
            _stopButton.Styled(ControlThemes.DialogCancelButton);
            _stopButton.Click += (_, _) => OnStopClick();

            _searchButton = new Button
            {
                Content = LocalizationManager.T("FindLostBases.Button.Search"),
                Width = 180,
                Height = 34,
                IsDefault = true
            };
            _searchButton.Styled(ControlThemes.DialogConfirmButton);
            _searchButton.Click += (_, _) => OnSearchClick();

            toolbar.Children.Add(new Border { Width = 1, Height = 18, Background = Brushes.Gray });
            toolbar.Children.Add(_stopButton);
            toolbar.Children.Add(_searchButton);

            var toolbarBorder = new Border
            {
                Padding = new Thickness(8, 4),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Background = Brushes.Transparent,
                Child = toolbar
            };
            Themes.ThemeBrushes.Bind(toolbarBorder, Border.BorderBrushProperty, "BorderColorBrush");
            DockPanel.SetDock(toolbarBorder, Dock.Top);
            drivesDock.Children.Add(toolbarBorder);

            DockPanel.SetDock(_drivesWrap, Dock.Bottom);
            drivesDock.Children.Add(_drivesWrap);

            drivesBorder.Child = drivesDock;
            Grid.SetRow(drivesBorder, 1);
            grid.Children.Add(drivesBorder);

            // Таблица найденных баз.
            var listBorder = new Border
            {
                Margin = new Thickness(0, 12, 0, 0),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6)
            };
            Themes.ThemeBrushes.Bind(listBorder, Border.BackgroundProperty, "CardBackgroundColorBrush");
            Themes.ThemeBrushes.Bind(listBorder, Border.BorderBrushProperty, "BorderColorBrush");

            var dock = new DockPanel { LastChildFill = true };
            var headerGrid = BuildHeaderGrid();
            DockPanel.SetDock(headerGrid, Dock.Top);
            dock.Children.Add(headerGrid);

            var scroll = new ScrollViewer
            {
                Content = _rowsPanel,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Padding = new Thickness(4)
            };
            dock.Children.Add(scroll);

            listBorder.Child = dock;
            Grid.SetRow(listBorder, 2);
            grid.Children.Add(listBorder);

            // Нижняя панель.
            var bottom = new Grid { Margin = new Thickness(0, 14, 0, 0) };
            bottom.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
            bottom.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));

            var leftStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
            _progressText.FontSize = 12;
            _progressText.FontWeight = FontWeight.SemiBold;
            _progressText.TextWrapping = TextWrapping.Wrap;
            leftStack.Children.Add(_progressText);

            _summaryText.FontSize = 12;
            _summaryText.TextWrapping = TextWrapping.Wrap;
            _summaryText.TextTrimming = TextTrimming.CharacterEllipsis;
            BindSecondary(_summaryText);
            leftStack.Children.Add(_summaryText);

            Grid.SetColumn(leftStack, 0);
            bottom.Children.Add(leftStack);

            _addButton = new Button
            {
                Content = LocalizationManager.T("FindLostBases.Button.Add"),
                Width = 200,
                Height = 36,
                IsEnabled = false
            };
            _addButton.Styled(ControlThemes.DialogConfirmButton);
            _addButton.Click += (_, _) => OnAddClick();

            var cancel = BuildCancelActionButton(140);
            var rightPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Spacing = 10,
                Children = { _addButton, cancel }
            };
            Grid.SetColumn(rightPanel, 1);
            bottom.Children.Add(rightPanel);

            Grid.SetRow(bottom, 3);
            grid.Children.Add(bottom);

            return grid;
        }

        private Grid BuildHeaderGrid()
        {
            var grid = new Grid { Margin = new Thickness(8, 0, 8, 2) };
            ApplyColumns(grid);

            _checkedHeaderText.FontSize = 12;
            _checkedHeaderText.FontWeight = FontWeight.SemiBold;
            BindSecondary(_checkedHeaderText);
            Grid.SetColumn(_checkedHeaderText, 0);
            grid.Children.Add(_checkedHeaderText);

            AddHeader(grid, 1, "FindLostBases.Column.Name");
            AddHeader(grid, 2, "FindLostBases.Column.Path");
            AddHeader(grid, 3, "FindLostBases.Column.Size");
            AddHeader(grid, 4, "FindLostBases.Column.Modified");
            AddHeader(grid, 5, "FindLostBases.Column.InApp");
            AddHeader(grid, 6, "FindLostBases.Column.InV8i");

            return grid;
        }

        private static void AddHeader(Grid grid, int column, string key)
        {
            var h = MakeHeaderText(LocalizationManager.T(key));
            BindSecondary(h);
            Grid.SetColumn(h, column);
            grid.Children.Add(h);
        }

        private void ApplyColumns(Grid grid)
        {
            grid.ColumnDefinitions.Clear();
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));               // 0 — флажок
            grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(2, GridUnitType.Star))); // 1 — имя
            grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(3, GridUnitType.Star))); // 2 — путь
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));               // 3 — размер
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));               // 4 — дата
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));               // 5 — в приложении
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));               // 6 — в ibases.v8i
        }

        private void AddRow(FoundBaseRowViewModel row)
        {
            var grid = new Grid { Margin = new Thickness(0, 2, 0, 2) };
            ApplyColumns(grid);

            var check = new CheckBox
            {
                IsChecked = row.IsChecked,
                IsEnabled = row.NotInApp,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(4, 0, 4, 0)
            };
            check.Styled(ControlThemes.CacheCleanCheckBox);
            check.IsCheckedChanged += (_, _) =>
            {
                row.IsChecked = check.IsChecked == true;
                UpdateCheckedHeader();
                UpdateAddEnabled();
            };
            Grid.SetColumn(check, 0);
            grid.Children.Add(check);

            AddText(grid, 1, row.Name, allowEllipsis: true);
            AddText(grid, 2, row.Path, allowEllipsis: true);

            var size = new TextBlock
            {
                Text = row.SizeText,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 8, 0)
            };
            Grid.SetColumn(size, 3);
            grid.Children.Add(size);

            var modified = new TextBlock
            {
                Text = row.LastWriteText,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 8, 0)
            };
            Grid.SetColumn(modified, 4);
            grid.Children.Add(modified);

            var inApp = MakeFlagDot();
            ApplyFlagStyle(inApp, row.InApp, "#16A34A", "#94A3B8");
            ToolTip.SetTip(inApp, LocalizationManager.T("FindLostBases.AlreadyInAppHint"));
            Grid.SetColumn(inApp, 5);
            grid.Children.Add(inApp);

            var inV8i = MakeFlagDot();
            ApplyFlagStyle(inV8i, row.InIbasesV8i, "#8B5CF6", "#94A3B8");
            ToolTip.SetTip(inV8i, LocalizationManager.T("FindLostBases.InV8iHint"));
            Grid.SetColumn(inV8i, 6);
            grid.Children.Add(inV8i);

            _checks[row] = check;
            _inAppTexts[row] = inApp;
            _inV8iTexts[row] = inV8i;
            _rowsPanel.Children.Add(grid);
        }

        private static TextBlock MakeFlagDot()
            => new()
            {
                Text = "●",
                FontSize = 15,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(4, 0, 4, 0)
            };

        private static void ApplyFlagStyle(TextBlock block, bool on, string onColor, string offColor)
            => block.Foreground = Brush.Parse(on ? onColor : offColor);

        private static void AddText(Grid grid, int column, string text, bool allowEllipsis)
        {
            var block = new TextBlock
            {
                Text = text,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 8, 0)
            };
            if (allowEllipsis)
                block.TextTrimming = TextTrimming.CharacterEllipsis;
            Grid.SetColumn(block, column);
            grid.Children.Add(block);
        }

        private void UpdateRow(FoundBaseRowViewModel row)
        {
            if (_inAppTexts.TryGetValue(row, out var inApp))
                ApplyFlagStyle(inApp, row.InApp, "#16A34A", "#94A3B8");
            if (_inV8iTexts.TryGetValue(row, out var inV8i))
                ApplyFlagStyle(inV8i, row.InIbasesV8i, "#8B5CF6", "#94A3B8");
            if (_checks.TryGetValue(row, out var check))
                check.IsEnabled = row.NotInApp;
        }
    }
}
#endif