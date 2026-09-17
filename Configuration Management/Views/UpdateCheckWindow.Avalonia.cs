#if LINUX
using System;
using System.IO;
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
    /// Окно проверки обновлений конфигурации для выбранной информационной базы (горячая клавиша F9).
    /// Показывает имя базы, текущую версию конфигурации, последнюю версию из каталога релизов 1С,
    /// адрес каталога и статус проверки; подсвечивает наличие нового релиза и позволяет скачать
    /// дистрибутив с индикатором прогресса. Сетевые операции выполняются в фоновом потоке.
    /// Avalonia/Linux-версия WPF-окна <see cref="UpdateCheckWindow"/>.
    /// </summary>
    public sealed class UpdateCheckWindow : ModalWindowBase
    {
        private readonly Services.IOneCUpdatesService _updates = AppServices.GetRequiredService<Services.IOneCUpdatesService>();
        private readonly Services.IInfobaseRepository _repository = AppServices.GetRequiredService<Services.IInfobaseRepository>();
        private readonly Services.IAppLogger _logger = AppServices.GetRequiredService<Services.IAppLogger>();
        private readonly IDialogService _dialogs = AppServices.GetRequiredService<IDialogService>();

        private readonly Infobase _infobase;
        private readonly UpdateCheckRowViewModel _row;
        private CancellationTokenSource? _cts;

        private readonly TextBlock _baseNameText = new();
        private readonly TextBlock _currentVersionText = new();
        private readonly TextBlock _latestVersionText = new();
        private readonly TextBlock _urlText = new();
        private readonly TextBlock _statusText = new();
        private readonly TextBlock _errorText = new();
        private readonly StackPanel _progressPanel = new() { IsVisible = false };
        private readonly ProgressBar _progressBar = new() { Minimum = 0, Maximum = 1 };
        private readonly TextBlock _progressText = new();
        private Button _checkButton = new();
        private Button _downloadButton = new();

        /// <param name="infobase">Информационная база, для которой выполняется проверка обновлений.</param>
        public UpdateCheckWindow(Infobase infobase)
        {
            _infobase = infobase ?? throw new ArgumentNullException(nameof(infobase));
            _row = new UpdateCheckRowViewModel(infobase, OnDownloadRow);

            Title = LocalizationManager.T("Updates.CheckTitle");
            Width = 620;
            Height = 560;
            MinWidth = 520;
            MinHeight = 460;
            FontSize = 13;
            CanResize = true;

            _baseNameText.Text = _row.Name;
            _currentVersionText.Text = string.IsNullOrWhiteSpace(_row.CurrentVersion) ? "—" : _row.CurrentVersion;

            _row.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(_row.Progress))
                    UpdateProgressDisplay();
                else if (e.PropertyName == nameof(_row.LatestVersion)
                         || e.PropertyName == nameof(_row.Url)
                         || e.PropertyName == nameof(_row.Status)
                         || e.PropertyName == nameof(_row.Error)
                         || e.PropertyName == nameof(_row.CanDownload))
                    RefreshDetailDisplay();
            };

            Content = BuildRoot();
            Opened += async (_, _) => await RunCheckAsync();
        }

        /// <summary>
        /// Показывает окно модально (синхронно). Открытая публичная обёртка над
        /// <see cref="ModalWindowBase.ShowDialogSync(Window?)"/>, чтобы диалог можно было
        /// вызывать из ViewModel (не наследника окна).
        /// </summary>
        public bool ShowSync(Window? owner = null) => ShowDialogSync(owner);

        private static string T(string key) => LocalizationManager.T(key);

        /// <summary>Запускает сетевую проверку наличия обновлений для связанной конфигурации.</summary>
        private async Task RunCheckAsync()
        {
            if (_row.IsChecking)
                return;

            _cts?.Cancel();
            _cts = new CancellationTokenSource();
            var token = _cts.Token;

            _row.IsChecking = true;
            _checkButton.IsEnabled = false;
            _errorText.Text = string.Empty;

            try
            {
                var config = FindLinkedConfig();
                var url = _updates.BuildUpdateUrl(config, config?.DefaultEdition, _infobase.UpdateUrlOverride);
                _row.Url = url;

                if (string.IsNullOrWhiteSpace(url))
                {
                    _row.Status = ConfigUpdateStatus.Failed;
                    _row.Error = T("Updates.NoUrl");
                }
                else
                {
                    var configName = config?.Name ?? _row.Name;
                    var currentVersion = _row.CurrentVersion;
                    var result = await Task.Run(
                        () => _updates.CheckForUpdatesAsync(configName, currentVersion, url, token), token);
                    _row.ApplyResult(result);
                }
            }
            catch (OperationCanceledException)
            {
                _row.Status = ConfigUpdateStatus.Failed;
                _row.Error = T("Updates.Cancelled");
            }
            catch (Exception ex)
            {
                _logger.Error($"Ошибка проверки обновлений конфигурации базы «{_row.Name}»", ex);
                _row.Status = ConfigUpdateStatus.Failed;
                _row.Error = T("Updates.NetworkError");
            }
            finally
            {
                _row.IsChecking = false;
                _checkButton.IsEnabled = true;
                RefreshDetailDisplay();
            }
        }

        /// <summary>Находит типовую конфигурацию, связанную с базой (по коду связи).</summary>
        private OneCConfigType? FindLinkedConfig()
        {
            var code = _infobase.UpdateConfigCode;
            if (string.IsNullOrWhiteSpace(code))
                return null;

            var all = BuiltInConfigTypes.All
                .Concat(LoadCustomTypes())
                .ToList();
            return all.FirstOrDefault(c =>
                string.Equals(c.Code, code, StringComparison.OrdinalIgnoreCase));
        }

        private System.Collections.Generic.List<OneCConfigType> LoadCustomTypes()
        {
            try
            {
                var settings = _repository.LoadSettings();
                return settings.CustomConfigTypes ?? new System.Collections.Generic.List<OneCConfigType>();
            }
            catch (Exception ex)
            {
                _logger.Warn("Не удалось загрузить пользовательские конфигурации для проверки: " + ex.Message);
                return new System.Collections.Generic.List<OneCConfigType>();
            }
        }

        /// <summary>Обновляет текстовые ячейки деталей и состояние кнопок из модели строки.</summary>
        private void RefreshDetailDisplay()
        {
            _latestVersionText.Text = string.IsNullOrWhiteSpace(_row.LatestVersion) ? "—" : _row.LatestVersion;
            _urlText.Text = string.IsNullOrWhiteSpace(_row.Url) ? "—" : _row.Url;
            _statusText.Text = StatusTextLocalized(_row.Status);

            IBrush? brush = new SolidColorBrush(Colors.Gray);
            var primary = TryGetBrush("TextPrimaryBrush");
            if (primary is not null) brush = primary;
            if (_row.HasNewer)
            {
                brush = TryGetBrush("AccentBrush") ?? new SolidColorBrush(Color.Parse("#16A34A"));
            }
            else if (_row.Status == ConfigUpdateStatus.Failed)
            {
                brush = new SolidColorBrush(Color.Parse("#EF4444"));
            }
            _statusText.Foreground = brush;

            _errorText.Text = _row.Status == ConfigUpdateStatus.Failed ? _row.Error : string.Empty;
            _downloadButton.IsEnabled = _row.CanDownload;
        }

        private IBrush? TryGetBrush(string key)
        {
            if (Application.Current is not { } app || !app.TryFindResource(key, out var found))
                return null;
            return found as IBrush;
        }

        private void UpdateProgressDisplay()
        {
            // Progress<double> приходит из фонового потока — маршализуем обновление в UI-поток.
            Dispatcher.UIThread.Post(() =>
            {
                if (_row.IsDownloading)
                {
                    _progressPanel.IsVisible = true;
                    _progressBar.Value = Math.Clamp(_row.Progress, 0, 1);
                    _progressText.Text = $"{Math.Round(_row.Progress * 100, 0):0} %";
                }
                else
                {
                    _progressPanel.IsVisible = false;
                }
            });
        }

        private async void OnCheckClick()
        {
            await RunCheckAsync();
        }

        /// <summary>Загружает дистрибутив обновления по ссылке каталога релизов (кнопка «Скачать»).</summary>
        private async void OnDownloadRow(UpdateCheckRowViewModel row)
        {
            if (!row.CanDownload || string.IsNullOrWhiteSpace(row.Url))
                return;

            var fileName = BuildDownloadFileName(row);
            var targetPath = _dialogs.SaveFileDialog(
                T("Updates.Download"),
                fileName,
                "Архивы (*.zip;*.rar;*.7z)|*.zip;*.rar;*.7z|Все файлы (*.*)|*.*",
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));

            if (string.IsNullOrWhiteSpace(targetPath))
                return;

            row.IsDownloading = true;
            row.Progress = 0;
            UpdateProgressDisplay();
            _cts?.Cancel();

            try
            {
                var progress = new Progress<double>(v =>
                    Dispatcher.UIThread.Post(() => row.Progress = Math.Clamp(v, 0, 1)));
                var savedPath = await Task.Run(() =>
                    _updates.DownloadUpdateAsync(row.Url, targetPath, progress, CancellationToken.None));

                if (string.IsNullOrWhiteSpace(savedPath))
                {
                    _dialogs.ShowWarning(T("Updates.NetworkError"), T("Updates.CheckTitle"));
                }
                else
                {
                    row.Progress = 1;
                    UpdateProgressDisplay();
                    _dialogs.ShowInfo(string.Format(T("Updates.Loaded"), savedPath), T("Updates.CheckTitle"));
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"Ошибка загрузки обновления конфигурации «{row.Name}»", ex);
                _dialogs.ShowError(T("Updates.NetworkError"), T("Updates.CheckTitle"));
            }
            finally
            {
                row.IsDownloading = false;
                UpdateProgressDisplay();
            }
        }

        private static string BuildDownloadFileName(UpdateCheckRowViewModel row)
        {
            var baseName = SanitizeFileName(row.Name);
            var latest = string.IsNullOrWhiteSpace(row.LatestVersion) ? "update" : row.LatestVersion;
            return $"{baseName}_{latest}.zip";
        }

        private static string SanitizeFileName(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            return string.Concat(name.Select(ch => invalid.Contains(ch) ? '_' : ch));
        }

        private static string StatusTextLocalized(ConfigUpdateStatus status)
        {
            return status switch
            {
                ConfigUpdateStatus.UpToDate => LocalizationManager.T("Updates.UpToDate"),
                ConfigUpdateStatus.NewerAvailable => LocalizationManager.T("Updates.NewerAvailable"),
                ConfigUpdateStatus.Unavailable => LocalizationManager.T("Updates.Unavailable"),
                ConfigUpdateStatus.Failed => LocalizationManager.T("Updates.CheckFailed"),
                _ => LocalizationManager.T("Updates.Status"),
            };
        }

        private Control BuildRoot()
        {
            var grid = new Grid { Margin = new Thickness(16) };
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            grid.RowDefinitions.Add(new RowDefinition(new GridLength(1, GridUnitType.Star)));
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

            var title = new TextBlock
            {
                Text = T("Updates.CheckTitle"),
                FontSize = 15,
                FontWeight = FontWeight.SemiBold
            };
            Grid.SetRow(title, 0);
            grid.Children.Add(title);

            // Карточка сведений о базе и результате проверки.
            var card = new Border
            {
                Margin = new Thickness(0, 12, 0, 0),
                Padding = new Thickness(14, 12),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6)
            };
            Themes.ThemeBrushes.Bind(card, Border.BackgroundProperty, "CardBackgroundColorBrush");
            Themes.ThemeBrushes.Bind(card, Border.BorderBrushProperty, "BorderColorBrush");

            var fields = new StackPanel { Spacing = 8 };
            fields.Children.Add(MakeFieldRow(T("Updates.CurrentVersion"), _currentVersionText));
            fields.Children.Add(MakeFieldRow(T("Updates.LatestVersion"), _latestVersionText));

            _baseNameText.FontWeight = FontWeight.SemiBold;
            _baseNameText.FontSize = 14;
            fields.Children.Add(MakeFieldRow(T("Updates.Name"), _baseNameText));

            fields.Children.Add(MakeFieldRow(T("Updates.UrlAuto"), _urlText));

            _statusText.FontWeight = FontWeight.SemiBold;
            fields.Children.Add(MakeFieldRow(T("Updates.Status"), _statusText));

            _errorText.TextWrapping = TextWrapping.Wrap;
            _errorText.Foreground = new SolidColorBrush(Color.Parse("#EF4444"));
            fields.Children.Add(MakeFieldRow(string.Empty, _errorText));

            // Индикатор прогресса загрузки.
            _progressBar.Height = 6;
            _progressText.FontSize = 12;
            Themes.ThemeBrushes.Bind(_progressText, TextBlock.ForegroundProperty, "TextSecondaryBrush");
            var progressStack = new StackPanel { Spacing = 4 };
            progressStack.Children.Add(_progressBar);
            progressStack.Children.Add(_progressText);
            _progressPanel.Children.Add(progressStack);
            fields.Children.Add(_progressPanel);

            card.Child = fields;
            Grid.SetRow(card, 1);
            grid.Children.Add(card);

            // Нижняя панель: «Проверить», «Скачать», закрыть.
            var bottom = new Grid { Margin = new Thickness(0, 14, 0, 0) };
            bottom.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
            bottom.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Spacing = 10
            };
            _checkButton = new Button { Content = T("Updates.Check"), Width = 120, Height = 36 };
            _checkButton.Styled(ControlThemes.SecondaryButton);
            _checkButton.Click += (_, _) => OnCheckClick();

            _downloadButton = new Button { Content = T("Updates.Download"), Width = 120, Height = 36, IsEnabled = false };
            _downloadButton.Styled(ControlThemes.DialogConfirmButton);
            _downloadButton.Click += (_, _) => OnDownloadRow(_row);

            var close = BuildCancelActionButton(140);
            close.Click += (_, _) => Close();

            buttons.Children.Add(_checkButton);
            buttons.Children.Add(_downloadButton);
            buttons.Children.Add(close);
            Grid.SetColumn(buttons, 1);
            bottom.Children.Add(buttons);

            Grid.SetRow(bottom, 2);
            grid.Children.Add(bottom);

            return grid;
        }

        /// <summary>Строка «подпись / значение» карточки.</summary>
        private static Grid MakeFieldRow(string labelKey, TextBlock value)
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));

            var label = new TextBlock
            {
                Text = labelKey,
                FontWeight = FontWeight.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 10, 0)
            };
            Themes.ThemeBrushes.Bind(label, TextBlock.ForegroundProperty, "TextSecondaryBrush");
            Grid.SetColumn(label, 0);
            grid.Children.Add(label);

            value.VerticalAlignment = VerticalAlignment.Center;
            value.TextTrimming = TextTrimming.CharacterEllipsis;
            value.TextWrapping = TextWrapping.Wrap;
            Grid.SetColumn(value, 1);
            grid.Children.Add(value);
            return grid;
        }
    }
}
#endif