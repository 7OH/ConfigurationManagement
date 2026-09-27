#if WINDOWS
using System;
using System.Windows;
using System.Windows.Input;
using Configuration_Management.Localization;
using Configuration_Management.Services;

namespace Configuration_Management
{
    /// <summary>
    /// Окно разблокировки приватных баз (0.3.9.85) по образцу <see cref="AppLockWindow"/>.
    /// Работает в двух режимах:
    ///  — «разблокировать» (у профиля уже есть пароль): одно поле — ввод пароля профиля;
    ///  — «задать пароль» (у профиля пароля нет): два поля — новый пароль и его повтор,
    ///    после чего пароль профиля сохраняется (PBKDF2) и приватные базы разблокируются.
    /// Разблокировка действует на текущую сессию: при смене профиля или перезапуске
    /// приложения приватные базы снова скрыты, пока не введён пароль.
    /// </summary>
    public partial class PrivateBasesUnlockWindow : Window
    {
        private readonly bool _setupMode;

        /// <summary>
        /// Режим «задать пароль»: true, если пароль профиля успешно установлен
        /// (окно закрыто с результатом). Считывается после ShowDialog.
        /// </summary>
        public bool PasswordSet { get; private set; }

        /// <summary>
        /// Режим «разблокировать»: true, если введён верный пароль профиля.
        /// </summary>
        public bool Unlocked { get; private set; }

        /// <summary>
        /// Создаёт окно разблокировки/установки пароля для приватных баз.
        /// </summary>
        /// <param name="setupMode">
        /// true — у профиля нет пароля: окно предлагает задать пароль (два поля);
        /// false — окно запрашивает пароль профиля (одно поле).
        /// </param>
        public PrivateBasesUnlockWindow(bool setupMode = false)
        {
            InitializeComponent();
            _setupMode = setupMode;

            if (setupMode)
            {
                Title = LocalizationManager.T("Private.SetupTitle");
                TitleLabel.Text = LocalizationManager.T("Private.SetupTitle");
                PromptLabel.Text = LocalizationManager.T("Private.SetupPrompt");
                OkTextBlock.Text = LocalizationManager.T("Common.Save");
                NewPasswordLabel.Text = LocalizationManager.T("AppLock.NewPassword");
                ConfirmLabel.Text = LocalizationManager.T("AppLock.ConfirmPassword");
                PasswordBox2.Visibility = Visibility.Visible;
                ConfirmLabel.Visibility = Visibility.Visible;
            }
            else
            {
                Title = LocalizationManager.T("Private.UnlockTitle");
                TitleLabel.Text = LocalizationManager.T("Private.UnlockTitle");
                PromptLabel.Text = LocalizationManager.T("Private.UnlockPrompt");
                OkTextBlock.Text = LocalizationManager.T("Private.Unlock");
                // Режим разблокировки: одно видимое поле с подписью «Пароль».
                NewPasswordLabel.Text = LocalizationManager.T("AppLock.Password");
                ConfirmLabel.Visibility = Visibility.Collapsed;
                PasswordBox2.Visibility = Visibility.Collapsed;
            }

            Loaded += (_, _) =>
            {
                PasswordBox1.Focus();
                // Пост с низким приоритетом страхует фокус, снятый активацией окна (issue #294).
                Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background,
                    new Action(() => PasswordBox1.Focus()));
            };
        }

        private void OnOk_Click(object sender, RoutedEventArgs e)
        {
            var profileService = AppServices.GetRequiredService<IProfileService>();
            var current = profileService.CurrentProfile;

            if (_setupMode)
            {
                var pwd = PasswordBox1.Password ?? string.Empty;
                var confirm = PasswordBox2.Password ?? string.Empty;
                if (string.IsNullOrEmpty(pwd))
                {
                    MessageBox.Show(LocalizationManager.T("AppLock.PasswordEmpty"),
                        Title, MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                if (!string.Equals(pwd, confirm, StringComparison.Ordinal))
                {
                    MessageBox.Show(LocalizationManager.T("AppLock.PasswordMismatch"),
                        Title, MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (current is null)
                {
                    MessageBox.Show(LocalizationManager.T("Private.NoProfile"),
                        Title, MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // Сохраняем пароль профиля (PBKDF2) и сразу разблокируем приватные базы.
                profileService.SetPassword(current.Id, pwd);
                profileService.MarkPrivateBasesUnlocked();
                PasswordSet = true;
                DialogResult = true;
                return;
            }

            var entered = PasswordBox1.Password ?? string.Empty;
            if (profileService.UnlockPrivateBases(entered))
            {
                Unlocked = true;
                DialogResult = true;
            }
            else
            {
                MessageBox.Show(LocalizationManager.T("Private.WrongPassword"),
                    Title, MessageBoxButton.OK, MessageBoxImage.Error);
                PasswordBox1.Clear();
                PasswordBox1.Focus();
            }
        }

        private void OnCancel_Click(object sender, RoutedEventArgs e) => Close();

        private void OnPassword_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                OnOk_Click(sender, e);
                e.Handled = true;
            }
        }
    }
}
#endif