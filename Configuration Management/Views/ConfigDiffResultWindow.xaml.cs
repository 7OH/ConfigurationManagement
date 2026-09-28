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

        public ConfigDiffResultWindow(ConfigurationDiffResult result)
        {
            InitializeComponent();

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

        private void OnClose_Click(object sender, RoutedEventArgs e) => Close();
    }
}