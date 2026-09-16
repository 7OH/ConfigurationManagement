#if WINDOWS
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Configuration_Management.Localization;
using Configuration_Management.Models;
using Configuration_Management.Services;
using Configuration_Management.ViewModels;

namespace Configuration_Management
{
    /// <summary>
    /// Диалог «Поиск потерянных и забытых баз 1С 8» (issue #247): выбор дисков/корней
    /// поиска, фоновое рекурсивное сканирование в поисках файлов <c>1Cv8.1CD</c> с
    /// кнопкой «Прекратить», таблица найденных баз и добавление отсутствующих в списке
    /// приложения баз. Признаки «В приложении» и «В ibases.v8i» определяются по путям
    /// файловых баз приложения и записям реестра 1С.
    /// </summary>
    public partial class FindLostBasesWindow : Window
    {
        private readonly IReadOnlyList<Infobase> _infobases;
        private readonly Action<Infobase> _addBase;
        private readonly HashSet<string> _appFileDirs = new(FoundBaseRowViewModel.DirComparer);
        private readonly HashSet<string> _v8iFileDirs = new(FoundBaseRowViewModel.DirComparer);
        private readonly List<FoundBaseRowViewModel> _rows = new();
        private readonly IAppLogger _logger = AppServices.GetRequiredService<IAppLogger>();

        private CancellationTokenSource? _cts;

        /// <summary>Признак того, что хотя бы одна база была добавлена (для персиста в настройках).</summary>
        public bool DataChanged { get; private set; }

        /// <param name="infobases">Список баз приложения (для признака «В приложении»).</param>
        /// <param name="addBase">Обратный вызов добавления новой базы в список приложения.</param>
        public FindLostBasesWindow(IReadOnlyList<Infobase> infobases, Action<Infobase> addBase)
        {
            InitializeComponent();
            _infobases = infobases;
            _addBase = addBase;

            BuildAppFileDirs();
            BuildV8iFileDirs();
            PopulateDriveChecks();

            ProgressText.Text = string.Empty;
            SummaryText.Text = string.Empty;
            UpdateCheckedHeader();
            UpdateAddEnabled();
        }

        private static string T(string key) => LocalizationManager.T(key);

        /// <summary>Собирает пути каталогов файловых баз, уже присутствующих в приложении.</summary>
        private void BuildAppFileDirs()
        {
            foreach (var ib in _infobases)
            {
                var dir = InfobaseMaintenanceService.GetFileBaseDirectory(ib);
                if (!string.IsNullOrWhiteSpace(dir))
                    _appFileDirs.Add(FoundBaseRowViewModel.NormalizeDir(dir));
            }
        }

        /// <summary>Собирает пути файловых баз из реестра 1С (ibases.v8i).</summary>
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

        /// <summary>Заполняет панель выбора корней поиска флажками (все отмечены).</summary>
        private void PopulateDriveChecks()
        {
            var roots = InfobaseDiskScanner.EnumerateSearchRoots();
            if (roots.Count == 0)
            {
                DrivesPanel.Children.Add(new TextBlock
                {
                    Text = T("FindLostBases.NoRoots"),
                    Foreground = System.Windows.Media.Brushes.Gray,
                    Margin = new Thickness(4, 4, 4, 4)
                });
                SearchButton.IsEnabled = false;
                return;
            }

            foreach (var root in roots)
            {
                DrivesPanel.Children.Add(new CheckBox
                {
                    Content = root,
                    Tag = root,
                    IsChecked = true,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(6, 4, 6, 4),
                    Cursor = System.Windows.Input.Cursors.Hand
                });
            }
        }

        private void OnDriveCheckAll(object sender, RoutedEventArgs e)
        {
            foreach (var cb in DrivesPanel.Children.OfType<CheckBox>())
                cb.IsChecked = true;
        }

        private void OnDriveCheckNone(object sender, RoutedEventArgs e)
        {
            foreach (var cb in DrivesPanel.Children.OfType<CheckBox>())
                cb.IsChecked = false;
        }

        /// <summary>Выбранные пользователем корни поиска.</summary>
        private List<string> GetSelectedRoots()
        {
            return DrivesPanel.Children.OfType<CheckBox>()
                .Where(c => c.IsChecked == true && c.Tag is string s && !string.IsNullOrWhiteSpace(s))
                .Select(c => (string)c.Tag!)
                .ToList();
        }

        private void OnStop_Click(object sender, RoutedEventArgs e)
            => _cts?.Cancel();

        private void OnClose_Click(object sender, RoutedEventArgs e)
            => Close();

        /// <summary>Обновляет заголовок колонки-флажка счётчиком отмеченных элементов.</summary>
        private void UpdateCheckedHeader()
        {
            CheckedHeaderText.Text = string.Format(
                LocalizationManager.T("FindLostBases.Column.CheckedHeaderFormat"),
                _rows.Count(r => r.IsChecked));
        }

        /// <summary>Обновляет доступность кнопки «Добавить в список баз».</summary>
        private void UpdateAddEnabled()
        {
            AddButton.IsEnabled = _rows.Any(r => r.IsChecked && !r.InApp);
        }

        /// <summary>Показывает ход сканирования на UI-потоке.</summary>
        private void OnScanProgress(int found, int dirs)
        {
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background,
                new Action(() =>
                {
                    ProgressText.Text = string.Format(
                        LocalizationManager.T("FindLostBases.ProgressFormat"), found, dirs);
                }));
        }

        private async void OnSearch_Click(object sender, RoutedEventArgs e)
        {
            var roots = GetSelectedRoots();
            if (roots.Count == 0)
            {
                SummaryText.Text = LocalizationManager.T("FindLostBases.NoneSelected");
                return;
            }

            SearchButton.IsEnabled = false;
            StopButton.IsEnabled = true;
            SetDriveChecksEnabled(false);
            _rows.Clear();
            BasesGrid.ItemsSource = null;
            ProgressText.Text = string.Empty;
            SummaryText.Text = string.Empty;
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
                }

                BasesGrid.ItemsSource = _rows;
                SummaryText.Text = string.Format(
                    LocalizationManager.T("FindLostBases.FoundFormat"), _rows.Count);
                UpdateCheckedHeader();
                UpdateAddEnabled();
            }
            catch (OperationCanceledException)
            {
                SummaryText.Text = LocalizationManager.T("FindLostBases.Stopped");
            }
            catch (Exception ex)
            {
                _logger.Error("Ошибка сканирования дисков в поисках баз 1С", ex);
                SummaryText.Text = string.Format(LocalizationManager.T("FindLostBases.ErrorFormat"), ex.Message);
            }
            finally
            {
                _cts.Dispose();
                _cts = null;
                SearchButton.IsEnabled = true;
                StopButton.IsEnabled = false;
                SetDriveChecksEnabled(true);
                ProgressText.Text = string.Empty;
            }
        }

        /// <summary>Добавляет отмеченные базы, отсутствующие в приложении, в список баз.</summary>
        private void OnAdd_Click(object sender, RoutedEventArgs e)
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
            }

            SummaryText.Text = string.Format(
                LocalizationManager.T("FindLostBases.AddedFormat"), added, alreadyInList);
            UpdateCheckedHeader();
            UpdateAddEnabled();
        }

        private void OnCellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            if (e.EditAction != DataGridEditAction.Commit)
                return;
            UpdateCheckedHeader();
            UpdateAddEnabled();
        }

        private void SetDriveChecksEnabled(bool enabled)
        {
            foreach (var cb in DrivesPanel.Children.OfType<CheckBox>())
                cb.IsEnabled = enabled;
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            _cts?.Cancel();
            base.OnClosing(e);
        }
    }
}
#endif