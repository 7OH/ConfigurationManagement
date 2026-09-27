using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using Configuration_Management.Localization;
using Configuration_Management.Models;
using Configuration_Management.ViewModels;

namespace Configuration_Management
{
    /// <summary>
    /// Окно «Обновление из хранилищ» (0.3.9.88, Windows/WPF): список баз с
    /// заполненным хранилищем конфигурации, чекбоксы, последовательный прогон
    /// выбранных через конфигуратор в пакетном режиме и построчный лог.
    /// Вся логика — в чистой ViewModel <see cref="RepositoryBatchUpdateViewModel"/>.
    /// </summary>
    public partial class RepositoryBatchUpdateWindow : Window
    {
        private readonly RepositoryBatchUpdateViewModel _vm;

        public RepositoryBatchUpdateWindow(IEnumerable<Infobase> infobases)
        {
            InitializeComponent();
            _vm = new RepositoryBatchUpdateViewModel(infobases);
            DataContext = _vm;

            Title = LocalizationManager.T("RepoUpdate.Title");
            HintText.Text = LocalizationManager.T("RepoUpdate.Hint");
            LogHeader.Text = LocalizationManager.T("RepoUpdate.Log");

            ItemsList.ItemsSource = _vm.Items;
            LogList.ItemsSource = _vm.LogLines;
            _vm.PropertyChanged += OnVmPropertyChanged;
            ((INotifyCollectionChanged)_vm.LogLines).CollectionChanged += OnLogLinesChanged;

            // Если баз с хранилищем нет — окно сразу сообщает об этом.
            if (!_vm.HasItems)
                HintText.Text = LocalizationManager.T("RepoUpdate.NoBases");
        }

        private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(RepositoryBatchUpdateViewModel.IsRunning))
            {
                UpdateButton.IsEnabled = !_vm.IsRunning;
                // Пока идёт прогон, нельзя менять состав выбранных баз.
                foreach (var item in _vm.Items)
                    item.IsBusy = _vm.IsRunning;
            }
        }

        private void OnLogLinesChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action == NotifyCollectionChangedAction.Add && _vm.LogLines.Count > 0)
            {
                var last = _vm.LogLines[_vm.LogLines.Count - 1];
                LogList.ScrollIntoView(last);
            }
        }

        private void OnSelectAll_Click(object sender, RoutedEventArgs e) => _vm.SetAll(true);

        private void OnSelectNone_Click(object sender, RoutedEventArgs e) => _vm.SetAll(false);

        private async void OnUpdateSelected_Click(object sender, RoutedEventArgs e) => await _vm.RunAsync();

        private void OnClose_Click(object sender, RoutedEventArgs e) => Close();
    }
}