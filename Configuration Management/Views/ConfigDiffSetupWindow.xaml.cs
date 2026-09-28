using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using Configuration_Management.Localization;
using Configuration_Management.Models;
using Configuration_Management.Services;

namespace Configuration_Management
{
    /// <summary>
    /// Окно настройки сравнения конфигураций (0.3.9.99, функция №9, Windows/WPF):
    /// выбор режима («База ↔ .cf» / «.cf ↔ .cf»), базы, файлов .cf и платформы 1С.
    /// Сравнение выполняется в фоновом потоке с окном прогресса; результат открывается
    /// в <see cref="ConfigDiffResultWindow"/>. Вся логика сравнения — в чистом сервисе
    /// <see cref="ConfigurationDiffService"/>.
    /// </summary>
    public partial class ConfigDiffSetupWindow : Window
    {
        private readonly IReadOnlyList<Infobase> _infobases;
        private readonly IReadOnlyList<string> _installedPlatforms;
        private readonly string _defaultPlatform;

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
            InitializeComponent();

            _infobases = infobases?.Where(ib => ib is not null).ToList()
                ?? new List<Infobase>();
            _installedPlatforms = installedPlatforms?
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Distinct()
                .ToList()
                ?? new List<string>();
            _defaultPlatform = defaultPlatform ?? string.Empty;

            foreach (var ib in _infobases)
                BasesBox.Items.Add(ib);
            if (selectedBase is not null && _infobases.Contains(selectedBase))
                BasesBox.SelectedItem = selectedBase;
            else if (BasesBox.Items.Count > 0)
                BasesBox.SelectedIndex = 0;

            // Режим по умолчанию — «База ↔ .cf».
            ModeBaseVsCfRadio.IsChecked = true;
        }

        private ConfigDiffMode SelectedMode =>
            ModeCfVsCfRadio.IsChecked == true ? ConfigDiffMode.CfVsCf : ConfigDiffMode.BaseVsCf;

        /// <summary>Переключение режима: видимость блоков и состав списка платформ.</summary>
        private void OnModeChanged(object sender, RoutedEventArgs e)
        {
            var isCfVsCf = SelectedMode == ConfigDiffMode.CfVsCf;
            BasePanel.Visibility = isCfVsCf ? Visibility.Collapsed : Visibility.Visible;
            CfLeftPanel.Visibility = isCfVsCf ? Visibility.Visible : Visibility.Collapsed;
            RebuildPlatformList();
            ErrorText.Visibility = Visibility.Collapsed;
        }

        /// <summary>Смена базы: подставляем её платформу.</summary>
        private void OnBaseChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            RebuildPlatformList();
            ErrorText.Visibility = Visibility.Collapsed;
        }

        /// <summary>
        /// Собирает список платформ: установленные + платформа выбранной базы
        /// (в режиме «База ↔ .cf»); выбор — платформа базы или значение по умолчанию.
        /// </summary>
        private void RebuildPlatformList()
        {
            var items = _installedPlatforms.ToList();

            var baseVersion = (SelectedMode == ConfigDiffMode.BaseVsCf)
                ? (BasesBox.SelectedItem as Infobase)?.PlatformVersion
                : null;
            if (!string.IsNullOrWhiteSpace(baseVersion) &&
                !items.Contains(baseVersion, System.StringComparer.OrdinalIgnoreCase))
                items.Insert(0, baseVersion);

            PlatformBox.ItemsSource = null;
            PlatformBox.Items.Clear();
            foreach (var p in items)
                PlatformBox.Items.Add(p);

            var prefer = baseVersion ?? _defaultPlatform;
            PlatformBox.SelectedItem = items.FirstOrDefault(i =>
                string.Equals(i, prefer, System.StringComparison.OrdinalIgnoreCase));
            if (PlatformBox.SelectedItem is null && items.Count > 0)
                PlatformBox.SelectedIndex = 0;

            PlatformHint.Text = SelectedMode == ConfigDiffMode.BaseVsCf
                ? LocalizationManager.T("ConfigDiff.PlatformFromBaseHint")
                : LocalizationManager.T("ConfigDiff.PlatformHint");
        }

        private void OnBrowseLeft_Click(object sender, RoutedEventArgs e)
            => BrowseCf(LeftCfBox);

        private void OnBrowseRight_Click(object sender, RoutedEventArgs e)
            => BrowseCf(RightCfBox);

        private void BrowseCf(System.Windows.Controls.TextBox target)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = LocalizationManager.T("ConfigDiff.SelectCfFile"),
                Filter = LocalizationManager.T("ConfigDiff.CfFileFilter"),
                DefaultExt = ".cf"
            };
            if (dialog.ShowDialog(this) == true)
                target.Text = dialog.FileName;
            ErrorText.Visibility = Visibility.Collapsed;
        }

        private void OnCancel_Click(object sender, RoutedEventArgs e) => Close();

        private async void OnCompare_Click(object sender, RoutedEventArgs e)
        {
            ErrorText.Visibility = Visibility.Collapsed;

            // ---- Валидация входных данных ----
            var platform = (PlatformBox.SelectedItem as string)?.Trim();
            if (string.IsNullOrWhiteSpace(platform))
            {
                ShowError(LocalizationManager.T("ConfigDiff.ErrNoPlatform"));
                return;
            }

            var rightCf = RightCfBox.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(rightCf) || !File.Exists(rightCf))
            {
                ShowError(LocalizationManager.T("ConfigDiff.ErrNoCf"));
                return;
            }

            Infobase? baseSelected = null;
            string? leftCf = null;
            if (SelectedMode == ConfigDiffMode.BaseVsCf)
            {
                baseSelected = BasesBox.SelectedItem as Infobase;
                if (baseSelected is null)
                {
                    ShowError(LocalizationManager.T("ConfigDiff.ErrNoBase"));
                    return;
                }
            }
            else
            {
                leftCf = LeftCfBox.Text?.Trim() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(leftCf) || !File.Exists(leftCf))
                {
                    ShowError(LocalizationManager.T("ConfigDiff.ErrNoCf"));
                    return;
                }
            }

            var request = new ConfigurationDiffRequest
            {
                Mode = SelectedMode,
                Base = baseSelected,
                LeftCfPath = leftCf,
                RightCfPath = rightCf,
                PlatformVersion = platform,
                LeftLabel = baseSelected?.Name ?? Path.GetFileName(leftCf) ?? string.Empty,
                RightLabel = Path.GetFileName(rightCf) ?? string.Empty
            };

            // ---- Прогресс и запуск сравнения ----
            var progress = new ConfigDiffProgressWindow { Owner = this };
            progress.SetStage(LocalizationManager.T("ConfigDiff.StageCreateTemp"));
            progress.Show();
            IsEnabled = false;
            try
            {
                var service = new ConfigurationDiffService();
                var progressAdapter = new Progress<string>(progress.SetStage);
                var result = await System.Threading.Tasks.Task.Run(
                    () => service.CompareAsync(request, progressAdapter));

                // После успешного сравнения setup-окно закрывается, открывается отчёт.
                Close();
                var resultWindow = new ConfigDiffResultWindow(result)
                {
                    Owner = System.Windows.Application.Current.MainWindow
                };
                resultWindow.ShowDialog();
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

        private void ShowError(string message)
        {
            ErrorText.Text = message;
            ErrorText.Visibility = Visibility.Visible;
        }
    }
}