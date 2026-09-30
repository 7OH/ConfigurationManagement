using System.Windows;
using Configuration_Management.Localization;

namespace Configuration_Management
{
    /// <summary>
    /// Окно «Детали регламентного задания» (0.3.9.178, Windows/WPF): просмотр полного
    /// текста деталей задания (все поля из вывода rac «job list», включая расписание,
    /// результат и описание ошибки). Тонкая обёртка: принимает готовый текст
    /// (<see cref="ViewModels.RacJobRow.DetailsText"/>), вся логика — в ViewModel.
    /// </summary>
    public partial class JobDetailsWindow : Window
    {
        public JobDetailsWindow(string detailsText)
        {
            InitializeComponent();
            Title = LocalizationManager.T("ServerMonitor.JobDetails.Title");
            DetailsText.Text = detailsText ?? string.Empty;
        }

        private void OnClose_Click(object sender, RoutedEventArgs e) => Close();
    }
}