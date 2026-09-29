#if LINUX
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Configuration_Management.Localization;
using Configuration_Management.Models;
using Configuration_Management.Services;
using Configuration_Management.Themes;

namespace Configuration_Management
{
    /// <summary>
    /// Окно настройки сравнения конфигураций (0.3.9.99, функция №9, Avalonia/Linux):
    /// выбор режима («База ↔ .cf» / «.cf ↔ .cf»), базы, файлов .cf и платформы 1С.
    /// Сравнение выполняется в фоновом потоке с окном прогресса; результат открывается
    /// в <see cref="ConfigDiffResultWindow.Avalonia.cs"/>. Вся логика — в чистом сервисе
    /// <see cref="ConfigurationDiffService"/>.
    /// </summary>
    public sealed class ConfigDiffSetupWindow : ModalWindowBase
    {
        private readonly IDialogService _dialogs = AppServices.GetRequiredService<IDialogService>();

        private readonly IReadOnlyList<Infobase> _infobases;
        private readonly IReadOnlyList<string> _installedPlatforms;
        private readonly string _defaultPlatform;

        private readonly RadioButton _modeBaseRadio = new();
        private readonly RadioButton _modeCfRadio = new();
        private readonly StackPanel _basePanel = new();
        private readonly StackPanel _cfLeftPanel = new();
        private readonly ComboBox _basesBox = new();
        private readonly TextBox _leftCfBox = new();
        private readonly TextBox _rightCfBox = new();
        private readonly ComboBox _platformBox = new();
        private readonly TextBlock _platformHint = new();
        private readonly TextBlock _errorText = new();

        /// <param name="infobases">Все базы списка для выбора левой стороны.</param>
        /// <param name="selectedBase">Текущая выбранная база главного окна (превыбор).</param>
        /// <param name="installedPlatforms">Установленные версии платформы 1С.</param>
        /// <param name="defaultPlatform">Версия платформы по умолчанию (из настроек).</param>
        public ConfigDiffSetupWindow(
            IReadOnlyList<Infobase> infobases,
            Infobase? selectedBase,
            IReadOnlyList<string> installedPlatforms,
            string defaultPlatform)
        {
            Title = LocalizationManager.T("ConfigDiff.Title");
            Width = 560;
            Height = 540;
            CanResize = false;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ShowInTaskbar = false;
            FontSize = 13;

            _infobases = infobases?.Where(ib => ib is not null).ToList() ?? new List<Infobase>();
            _installedPlatforms = installedPlatforms?
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Distinct()
                .ToList() ?? new List<string>();
            _defaultPlatform = defaultPlatform ?? string.Empty;

            var root = new StackPanel { Margin = new Thickness(16), Spacing = 6 };

            // Описание
            var description = new TextBlock
            {
                Text = LocalizationManager.T("ConfigDiff.Description"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 6)
            };
            ThemeBrushes.Bind(description, TextBlock.ForegroundProperty, "TextSecondaryBrush");
            root.Children.Add(description);

            // Режим сравнения
            _modeBaseRadio.Content = LocalizationManager.T("ConfigDiff.ModeBaseVsCf");
            _modeBaseRadio.IsChecked = true;
            _modeCfRadio.Content = LocalizationManager.T("ConfigDiff.ModeCfVsCf");
            _modeBaseRadio.IsCheckedChanged += (_, _) => UpdateMode();
            _modeCfRadio.IsCheckedChanged += (_, _) => UpdateMode();
            // Надписи радио-кнопок берут цвет из темы: без привязки в светлой схеме
            // FluentTheme может оставить тёмный текст, плохо читаемый на подложке
            // окна (issue #316). Синхронно со стилем RadioButton WPF-версии.
            ThemeBrushes.Bind(_modeBaseRadio, TemplatedControl.ForegroundProperty, "TextPrimaryBrush");
            ThemeBrushes.Bind(_modeCfRadio, TemplatedControl.ForegroundProperty, "TextPrimaryBrush");
            root.Children.Add(_modeBaseRadio);
            root.Children.Add(_modeCfRadio);

            // База (режим «База ↔ .cf»)
            _basePanel.Spacing = 4;
            var selectBaseLabel = new TextBlock { Text = LocalizationManager.T("ConfigDiff.SelectBase") };
            ThemeBrushes.Bind(selectBaseLabel, TextBlock.ForegroundProperty, "TextPrimaryBrush");
            _basePanel.Children.Add(selectBaseLabel);
            _basesBox.ItemTemplate = new FuncDataTemplate<Infobase>((ib, _) =>
            {
                var tb = new TextBlock { Text = ib.Name, VerticalAlignment = VerticalAlignment.Center };
                ThemeBrushes.Bind(tb, TextBlock.ForegroundProperty, "TextPrimaryBrush");
                return tb;
            });
            // Текст выбранного элемента в закрытом списке — тоже из темы (issue #316).
            ThemeBrushes.Bind(_basesBox, TemplatedControl.ForegroundProperty, "TextPrimaryBrush");
            foreach (var ib in _infobases)
                _basesBox.Items.Add(ib);
            if (selectedBase is not null && _infobases.Contains(selectedBase))
                _basesBox.SelectedItem = selectedBase;
            else if (_basesBox.Items.Count > 0)
                _basesBox.SelectedIndex = 0;
            _basesBox.SelectionChanged += (_, _) => RebuildPlatforms();
            _basePanel.Children.Add(_basesBox);
            root.Children.Add(_basePanel);

            // Левый .cf (режим «.cf ↔ .cf»)
            _cfLeftPanel.Spacing = 4;
            var selectCfLeftLabel = new TextBlock { Text = LocalizationManager.T("ConfigDiff.SelectCfLeft") };
            ThemeBrushes.Bind(selectCfLeftLabel, TextBlock.ForegroundProperty, "TextPrimaryBrush");
            _cfLeftPanel.Children.Add(selectCfLeftLabel);
            _cfLeftPanel.Children.Add(BuildCfRow(_leftCfBox));
            root.Children.Add(_cfLeftPanel);

            // Правый .cf
            var selectCfRightLabel = new TextBlock { Text = LocalizationManager.T("ConfigDiff.SelectCfRight") };
            ThemeBrushes.Bind(selectCfRightLabel, TextBlock.ForegroundProperty, "TextPrimaryBrush");
            root.Children.Add(selectCfRightLabel);
            root.Children.Add(BuildCfRow(_rightCfBox));

            // Платформа 1С
            var platformLabel = new TextBlock { Text = LocalizationManager.T("ConfigDiff.Platform") };
            ThemeBrushes.Bind(platformLabel, TextBlock.ForegroundProperty, "TextPrimaryBrush");
            root.Children.Add(platformLabel);
            _platformBox.IsEditable = true;
            // Редактируемый текст платформы и её выпадающий список — из темы (issue #316).
            ThemeBrushes.Bind(_platformBox, TemplatedControl.ForegroundProperty, "TextPrimaryBrush");
            root.Children.Add(_platformBox);
            _platformHint.FontSize = 11;
            _platformHint.TextWrapping = TextWrapping.Wrap;
            ThemeBrushes.Bind(_platformHint, TextBlock.ForegroundProperty, "TextSecondaryBrush");
            root.Children.Add(_platformHint);

            // Ошибка
            _errorText.Foreground = new SolidColorBrush(Color.Parse("#EF4444"));
            _errorText.TextWrapping = TextWrapping.Wrap;
            _errorText.FontSize = 12;
            _errorText.MaxHeight = 120;
            _errorText.IsVisible = false;
            root.Children.Add(_errorText);

            // Кнопки
            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Spacing = 8,
                Margin = new Thickness(0, 10, 0, 0)
            };
            var compare = new Button
            {
                Content = LocalizationManager.T("ConfigDiff.Compare"),
                Width = 130
            };
            compare.Click += async (_, _) => await OnCompareAsync();
            var cancel = new Button
            {
                Content = LocalizationManager.T("ConfigDiff.Cancel"),
                Width = 100,
                IsCancel = true
            };
            cancel.Click += (_, _) => Close();
            buttons.Children.Add(compare);
            buttons.Children.Add(cancel);
            root.Children.Add(buttons);

            Content = root;
            UpdateMode();
        }

        /// <summary>Строка «текст + кнопка Обзор» для выбора файла .cf.</summary>
        private Control BuildCfRow(TextBox target)
        {
            var grid = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) } };
            target.MinWidth = 300;
            grid.Children.Add(target);
            var browse = new Button { Content = LocalizationManager.T("ConfigDiff.Browse"), Width = 90, Margin = new Thickness(8, 0, 0, 0) };
            browse.Click += (_, _) => BrowseCf(target);
            Grid.SetColumn(browse, 1);
            grid.Children.Add(browse);
            return grid;
        }

        private bool IsCfVsCf => _modeCfRadio.IsChecked == true;

        /// <summary>Переключение режима: видимость блоков и состав списка платформ.</summary>
        private void UpdateMode()
        {
            _basePanel.IsVisible = !IsCfVsCf;
            _cfLeftPanel.IsVisible = IsCfVsCf;
            RebuildPlatforms();
            _errorText.IsVisible = false;
        }

        /// <summary>Собирает список платформ: установленные + платформа выбранной базы.</summary>
        private void RebuildPlatforms()
        {
            var items = _installedPlatforms.ToList();
            var baseVersion = !IsCfVsCf
                ? (_basesBox.SelectedItem as Infobase)?.PlatformVersion
                : null;
            if (!string.IsNullOrWhiteSpace(baseVersion) &&
                !items.Contains(baseVersion, StringComparer.OrdinalIgnoreCase))
                items.Insert(0, baseVersion);

            _platformBox.ItemsSource = items;
            var prefer = baseVersion ?? _defaultPlatform;
            _platformBox.SelectedItem = items.FirstOrDefault(i =>
                string.Equals(i, prefer, StringComparison.OrdinalIgnoreCase));
            if (_platformBox.SelectedItem is null && items.Count > 0)
                _platformBox.SelectedIndex = 0;

            _platformHint.Text = IsCfVsCf
                ? LocalizationManager.T("ConfigDiff.PlatformHint")
                : LocalizationManager.T("ConfigDiff.PlatformFromBaseHint");
        }

        private void BrowseCf(TextBox target)
        {
            var path = _dialogs.OpenFileDialog(
                LocalizationManager.T("ConfigDiff.SelectCfFile"),
                LocalizationManager.T("ConfigDiff.CfFileFilter"));
            if (!string.IsNullOrWhiteSpace(path))
                target.Text = path;
            _errorText.IsVisible = false;
        }

        private async Task OnCompareAsync()
        {
            _errorText.IsVisible = false;

            var platform = (_platformBox.SelectedItem as string)?.Trim();
            if (string.IsNullOrWhiteSpace(platform))
            {
                ShowError(LocalizationManager.T("ConfigDiff.ErrNoPlatform"));
                return;
            }

            var rightCf = (_rightCfBox.Text ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(rightCf) || !File.Exists(rightCf))
            {
                ShowError(LocalizationManager.T("ConfigDiff.ErrNoCf"));
                return;
            }

            Infobase? baseSelected = null;
            string? leftCf = null;
            if (!IsCfVsCf)
            {
                baseSelected = _basesBox.SelectedItem as Infobase;
                if (baseSelected is null)
                {
                    ShowError(LocalizationManager.T("ConfigDiff.ErrNoBase"));
                    return;
                }
            }
            else
            {
                leftCf = (_leftCfBox.Text ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(leftCf) || !File.Exists(leftCf))
                {
                    ShowError(LocalizationManager.T("ConfigDiff.ErrNoCf"));
                    return;
                }
            }

            var request = new ConfigurationDiffRequest
            {
                Mode = IsCfVsCf ? ConfigDiffMode.CfVsCf : ConfigDiffMode.BaseVsCf,
                Base = baseSelected,
                LeftCfPath = leftCf,
                RightCfPath = rightCf,
                PlatformVersion = platform,
                LeftLabel = baseSelected?.Name ?? Path.GetFileName(leftCf) ?? string.Empty,
                RightLabel = Path.GetFileName(rightCf) ?? string.Empty
            };

            var progress = new ConfigDiffProgressWindow();
            progress.SetStage(LocalizationManager.T("ConfigDiff.StageCreateTemp"));
            progress.Show();
            IsEnabled = false;
            try
            {
                var service = new ConfigurationDiffService();
                var progressAdapter = new Progress<string>(progress.SetStage);
                var result = await Task.Run(() => service.CompareAsync(request, progressAdapter));

                Close(); // setup закрывается после успешного сравнения
                // Этап 5 (0.3.9.136): в режиме «База ↔ .cf» левая база передаётся в окно
                // отчёта — кнопка «Обозреватель метаданных…» откроет обозреватель
                // с предвыбранной базой и сразу загрузкой; для CfVsCf базы нет (null).
                var resultWindow = new ConfigDiffResultWindow(
                    result,
                    !IsCfVsCf ? baseSelected : null);
                resultWindow.ShowDialogSync(OwnerWindow());
            }
            catch (ConfigurationDiffException ex)
            {
                ShowError(ex.Message);
            }
            catch (Exception ex)
            {
                ShowError(string.Format(LocalizationManager.T("ConfigDiff.ErrOperationFailedFormat"), ex.Message));
            }
            finally
            {
                IsEnabled = true;
                progress.Close();
            }
        }

        /// <summary>
        /// Окно-владелец для окна результата (setup уже закрыто). Спрятанное в трей
        /// окно владельцем быть не может (показ поверх невидимого окна роняет приложение) —
        /// как в MainViewModel.OwnerWindow.
        /// </summary>
        private static Window? OwnerWindow()
        {
            if (Avalonia.Application.Current?.ApplicationLifetime
                is not Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
                return null;

            foreach (var window in desktop.Windows)
            {
                if (window.IsActive && window.IsVisible)
                    return window;
            }

            return desktop.MainWindow is { IsVisible: true } main ? main : null;
        }

        private void ShowError(string message)
        {
            _errorText.Text = message;
            _errorText.IsVisible = true;
        }
    }
}
#endif