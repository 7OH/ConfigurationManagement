using System;
using System.IO;
using System.Windows;
using Configuration_Management.Localization;
using Configuration_Management.Models;
using Configuration_Management.Services;
using Configuration_Management.ViewModels;

namespace Configuration_Management
{
    /// <summary>
    /// Окно отчёта о сравнении конфигураций (0.3.9.99, функция №9, Windows/WPF):
    /// дерево «Тип метаданных → объекты» со статусами (добавлен/изменён/удалён/
    /// без изменений), сводка и экспорт в CSV (через <see cref="CsvExporter"/>)
    /// и TXT. Вся логика — в чистой ViewModel <see cref="ConfigDiffResultViewModel"/>.
    /// </summary>
    public partial class ConfigDiffResultWindow : Window
    {
        private readonly ConfigDiffResultViewModel _vm;

        /// <summary>
        /// Левая база для кнопки «Обозреватель метаданных…» (этап 5 цикла 0.3.9.132–0.3.9.136):
        /// режим «База ↔ .cf» — передаётся из <see cref="ConfigDiffSetupWindow"/>, для
        /// CfVsCf — null. Параметр опционален — существующие вызовы не ломаются.
        /// </summary>
        private readonly Infobase? _baseForExplorer;

        public ConfigDiffResultWindow(ConfigurationDiffResult result, Infobase? baseForExplorer = null)
        {
            InitializeComponent();

            _baseForExplorer = baseForExplorer;
            OpenExplorerButton.Visibility = baseForExplorer is null
                ? Visibility.Collapsed
                : Visibility.Visible;

            _vm = new ConfigDiffResultViewModel(result);
            DiffTree.ItemsSource = _vm.Types;
            HeaderTextBlock.Text = _vm.HeaderText;
            SummaryTextBlock.Text = _vm.SummaryText;
            ElapsedTextBlock.Text = _vm.ElapsedText;
            RootChangedTextBlock.Text = _vm.RootChangedText;
        }

        private void OnExportCsv_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = LocalizationManager.T("ConfigDiff.ExportCsv"),
                Filter = LocalizationManager.T("ConfigDiff.CsvFileFilter"),
                DefaultExt = ".csv",
                FileName = $"ConfigDiff_{DateTime.Now:yyyy-MM-dd}.csv",
                AddExtension = true
            };
            if (dialog.ShowDialog(this) != true)
                return;

            try
            {
                CsvExporter.WriteFile(dialog.FileName, _vm.BuildCsvRows());
            }
            catch (Exception ex)
            {
                MessageBox.Show(this,
                    string.Format(LocalizationManager.T("ConfigDiff.ExportFailedFormat"), ex.Message),
                    LocalizationManager.T("ConfigDiff.Title"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void OnExportTxt_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = LocalizationManager.T("ConfigDiff.ExportTxt"),
                Filter = LocalizationManager.T("ConfigDiff.TxtFileFilter"),
                DefaultExt = ".txt",
                FileName = $"ConfigDiff_{DateTime.Now:yyyy-MM-dd}.txt",
                AddExtension = true
            };
            if (dialog.ShowDialog(this) != true)
                return;

            try
            {
                File.WriteAllText(dialog.FileName, _vm.BuildTextReport(), System.Text.Encoding.UTF8);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this,
                    string.Format(LocalizationManager.T("ConfigDiff.ExportFailedFormat"), ex.Message),
                    LocalizationManager.T("ConfigDiff.Title"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// «Обозреватель метаданных…» (этап 5 цикла 0.3.9.132–0.3.9.136): открывает
        /// <see cref="MetadataExplorerWindow"/> с предвыбранной левой базой (режим
        /// «База ↔ .cf») и сразу запускает выгрузку (<c>LoadCommand.Execute(null)</c>).
        /// Кнопка доступна только при наличии базы (скрыта для CfVsCf).
        /// </summary>
        private void OnOpenMetadataExplorer_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var bases = AppServices.GetRequiredService<IInfobaseRepository>().Load();
                var window = new MetadataExplorerWindow(bases, _baseForExplorer)
                {
                    Owner = this
                };
                // Сразу загрузка предвыбранной базы: результат — дерево обозревателя.
                window.LoadCommand.Execute(null);
                window.ShowDialog();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this,
                    ex.Message,
                    LocalizationManager.T("MetadataExplorer.Title"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void OnClose_Click(object sender, RoutedEventArgs e) => Close();
    }
}