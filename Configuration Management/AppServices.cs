using Configuration_Management.Services;
using Configuration_Management.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace Configuration_Management;

/// <summary>
/// Корневой контейнер зависимостей приложения.
/// </summary>
public static class AppServices
{
    public static IServiceProvider Services { get; private set; } = null!;

    public static void Configure()
    {
        var services = new ServiceCollection();

        // Общие регистрации для обеих платформ (Windows/WPF и Linux/Avalonia):
        // реализации сервисов платформы 1С не зависят от UI-фреймворка.
        services.AddSingleton<IProfileService, ProfileService>();
        services.AddSingleton<IAppLogger, FileAppLogger>();
        services.AddSingleton<IInfobaseRepository, InfobaseRepository>();
        services.AddSingleton<IOneCLauncher, OneCLauncherService>();
        services.AddSingleton<IOneCComConnector, OneCComConnector>();
        services.AddSingleton<IPlatformVersionService, PlatformVersionServiceAdapter>();
        services.AddSingleton<IIbasesSyncService, IbasesSyncService>();
        services.AddSingleton<ICreateInfobaseService, CreateInfobaseService>();
        // Проверка обновлений конфигураций 1С по web-ресурсу обновлений (функции №21/№22).
        services.AddSingleton<IOneCUpdatesService, OneCUpdatesService>();
        // Сценарии резервирования и восстановление (функции №16/№18): хранилище сценариев,
        // архивация ZIP/RAR и оркестратор выполнения. Чистые сервисы — без UI-зависимостей.
        services.AddSingleton<IBackupScenarioStore, BackupScenarioStore>();
        // Сценарии запуска скриптов (issue #308): JSON-хранилище и чистая подстановка
        // параметров. Чистые сервисы — без UI-зависимостей.
        services.AddSingleton<IScriptScenarioStore, ScriptScenarioStore>();
        services.AddSingleton<IArchiveService, ArchiveService>();
        services.AddSingleton<IBackupService, BackupService>();
        // Задания по расписанию (issue #286): хранилище заданий, обновление конфигурации ИБ
        // из .cf (/LoadCfg + /UpdateDBCfg) и фоновый планировщик. Чистые сервисы.
        services.AddSingleton<IScheduledTaskStore, ScheduledTaskStore>();
        services.AddSingleton<IConfigUpdateService, ConfigUpdateService>();
        services.AddSingleton<SchedulerService>();
        // Блокировка сеансов файловой ИБ (функция №20): пакетный запуск конфигуратора
        // (/LockIB) без открытия «1С:Предприятия». Чистый сервис — без UI-зависимостей.
        services.AddSingleton<ISessionLockService, SessionLockService>();
        // Интеграция с проводником Windows (функция №12): ассоциация .1CD и команды
        // контекстного меню. На Windows/WPF — реализация на реестре HKCU; на Linux/Avalonia —
        // заглушка (IExplorerIntegrationService.IsAvailable == false). Тип один, реализация
        // выбирается символами условной компиляции (#if WINDOWS / #if LINUX).
        services.AddSingleton<IExplorerIntegrationService, ExplorerIntegrationService>();
        // Автозапуск при старте ОС (функция №31 StartManager): на Windows/WPF — ключ реестра
        // HKCU\...\Run; на Linux/Avalonia — автозапуск десктоп-окружения (~/.config/autostart).
        services.AddSingleton<IAutoStartService, AutoStartService>();
        // Сохранение копии экрана по хоткею (функция №30 StartManager): на Windows — захват
        // всего виртуального рабочего стола (CopyFromScreen); на Linux/Avalonia — снимок окна.
        services.AddSingleton<IScreenshotService, ScreenshotService>();
        // Администрирование ИБ (Этап 6, функция №29 + консоль серверов): запуск chdbfl
        // для проверки целостности файловой ИБ и консоли администрирования серверов 1С
        // для клиент-серверных баз. Чистый сервис — без UI-зависимостей.
        services.AddSingleton<IInfobaseAdminService, InfobaseAdminService>();
        // Индикатор «база сейчас запущена» и инспектор процессов: список процессов 1С
        // с командными строками и подробностями (PID, время старта, владелец).
        // Windows — WMI (Win32_Process), Linux — обход /proc; тип один, реализация
        // выбирается символами условной компиляции.
        services.AddSingleton<IRunningInfobasesService, RunningInfobasesService>();
        // Системные уведомления ОС (функция №4): balloon-tip трея на Windows/WPF,
        // notify-send на Linux/Avalonia. Тип один, реализация выбирается символами
        // условной компиляции (#if WINDOWS / #if LINUX), как RunningInfobasesService.
        services.AddSingleton<INotificationService, NotificationService>();
        // Завершение процесса 1С по PID (инспектор процессов): Windows — Process.Kill
        // вместе с деревом потомков, Linux — kill через /proc со сверкой времени старта.
        services.AddSingleton<IOneCProcessKiller, OneCProcessKiller>();
        services.AddTransient<MainViewModel>();
        services.AddTransient<MainWindow>();

#if WINDOWS
        // Windows — приоритетная платформа (WPF). Здесь дополнительно регистрируются
        // WPF-диалоги, регистратор COM-коннектора и Windows-only модели представления окон.
        services.AddSingleton<IDialogService, WpfDialogService>();
        services.AddSingleton<IOneCComConnectorRegistrar, OneCComConnectorRegistrar>();
        services.AddTransient<SettingsViewModel>();
        services.AddTransient<ProfilesViewModel>();
        // Проверка обновлений из GitHub Releases (Windows-only, автообновление).
        services.AddSingleton<GitHubReleaseService>();
        // Подсистема автообновления: фоновая проверка и диалог «Доступна новая версия».
        services.AddSingleton<UpdateService>();
#else
        // Linux (Avalonia): диалоги Avalonia. Регистратор COM-коннектора не подключается —
        // на Linux COM отсутствует (чтение конфигурации выполняется без COM: 1Cv8.1CD / DESIGNER).
        services.AddSingleton<IDialogService, AvaloniaDialogService>();
        // Проверка обновлений из GitHub Releases + подсистема автообновления (Linux/Avalonia).
        services.AddSingleton<GitHubReleaseService>();
        services.AddSingleton<UpdateService>();
#endif

        Services = services.BuildServiceProvider();
    }

    public static T GetRequiredService<T>() where T : notnull =>
        Services.GetRequiredService<T>();

    /// <summary>
    /// Служба, если контейнер уже настроен и она зарегистрирована, иначе null.
    /// Для вспомогательных фоновых задач (мониторы), которые обязаны тихо
    /// пропускать работу до инициализации контейнера.
    /// </summary>
    public static T? TryGetService<T>() where T : notnull
    {
        try
        {
            return Services is null ? default : Services.GetService<T>();
        }
        catch
        {
            return default;
        }
    }
}
