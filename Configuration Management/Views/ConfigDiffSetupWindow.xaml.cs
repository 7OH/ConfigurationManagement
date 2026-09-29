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

        /// <summary>
        /// Выбранная база как экземпляр из <see cref="_infobases"/> (по Id). Приходит из
        /// списка главного окна, поэтому сравнение по ссылке с элементами комбобокса не
        /// сработало бы — ищем по Id (issue #316).
        /// </summary>
        private readonly Infobase? _preselectedBase;

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

            var selectedId = selectedBase?.Id;
            _preselectedBase = !string.IsNullOrEmpty(selectedId)
                ? _infobases.FirstOrDefault(ib => string.Equals(
                    ib.Id, selectedId, System.StringComparison.OrdinalIgnoreCase))
                : null;
            if (_preselectedBase is not null)
                BasesBox.SelectedItem = _preselectedBase;
            else if (BasesBox.Items.Count > 0)
                BasesBox.SelectedIndex = 0;

            // Режим по умолчанию — «База ↔ .cf».
            ModeBaseVsCfRadio.IsChecked = true;

            // Явный ItemTemplate задан в XAML, но закрытый комбобокс рисует выбранный
            // элемент до применения шаблона и мог бы показать ToString() (имя типа
            // Infobase, «ключ»). Переустанавливаем выбор после показа окна — контейнер
            // пересоздаётся уже с применённым шаблоном (схема ScriptScenarioEditWindow,
            // issue #308; здесь issue #316).
            Loaded += (_, _) => RestoreBaseSelection();

            // Высота — явная (не SizeToContent=Height): после первой отрисовки один раз
            // подстраиваемся под содержимое, чтобы внизу не оставалось пустого места
            // (issue #316). Клампинг — чистый WindowSizeMath (общий с Avalonia, покрыт тестами).
            ContentRendered += (_, _) => FitHeightToContent();
        }

        /// <summary>
        /// Переустанавливает выбранную базу после показа окна (issue #316): повторный
        /// <c>SelectedItem</c> пересоздаёт контейнер с применённым <c>ItemTemplate</c>,
        /// и закрытый комбобокс показывает имя базы, а не имя типа.
        /// </summary>
        private void RestoreBaseSelection()
        {
            if (_preselectedBase is not null && _infobases.Contains(_preselectedBase))
                BasesBox.SelectedItem = _preselectedBase;
            else if (BasesBox.Items.Count > 0 && BasesBox.SelectedItem is null)
                BasesBox.SelectedIndex = 0;
        }

        /// <summary>
        /// Подгоняет высоту окна под содержимое (явно, без <c>SizeToContent=Height</c>) и не
        /// даёт окну уйти за нижний край рабочей области при переключении режима (issue #316).
        /// Логика клампинга — чистый <see cref="WindowSizeMath"/> (общий с Avalonia, покрыт тестами).
        /// </summary>
        private void FitHeightToContent()
        {
            if (RootPanel is null || !IsLoaded || !IsVisible)
                return;

            // Измеряем контент при бесконечной высоте: сколько места нужно, чтобы содержимое
            // полностью поместилось (высоты видимых блоков текущего режима).
            var availableWidth = RootPanel.ActualWidth > 0 ? RootPanel.ActualWidth : Math.Max(400, Width);
            RootPanel.Measure(new System.Windows.Size(availableWidth, double.PositiveInfinity));
            var desired = RootPanel.DesiredSize.Height;
            if (desired <= 0)
                return;

            // Хром (заголовок окна + рамки) — разница между полной высотой и клиентской областью.
            var chrome = Math.Max(0, ActualHeight - (RootPanel.ActualHeight > 0 ? RootPanel.ActualHeight : desired));
            var target = WindowSizeMath.ClampHeight(desired + chrome, MinHeight, MaxHeight);
            if (Math.Abs(target - Height) > 1)
                Height = target;

            // Окно стояло у нижнего края экрана и выросло — поднимаем его, чтобы нижняя
            // часть не уходила за экран.
            var wa = SystemParameters.WorkArea;
            var newTop = WindowSizeMath.FitTop(Top, Height, wa.Top, wa.Bottom);
            if (Math.Abs(newTop - Top) > 1)
                Top = newTop;
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
            // Состав видимых блоков изменился — высота окна тоже должна измениться,
            // чтобы внизу не оставалось пустого места (issue #316).
            FitHeightToContent();
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
                // Этап 5 (0.3.9.136): в режиме «База ↔ .cf» левая база передаётся в окно
                // отчёта — кнопка «Обозреватель метаданных…» откроет обозреватель
                // с предвыбранной базой и сразу загрузкой; для CfVsCf базы нет (null).
                Close();
                var resultWindow = new ConfigDiffResultWindow(
                    result,
                    SelectedMode == ConfigDiffMode.BaseVsCf ? baseSelected : null)
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