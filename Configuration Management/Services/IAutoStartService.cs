namespace Configuration_Management.Services;

/// <summary>
/// Автозапуск приложения при старте операционной системы (функция №31 дорожной карты
/// StartManager). На Windows/WPF реализация пишет в ключ реестра
/// <c>HKCU\Software\Microsoft\Windows\CurrentVersion\Run</c> (без прав администратора);
/// на Linux/Avalonia — создаёт файл автозапуска десктоп-окружения
/// <c>~/.config/autostart/*.desktop</c>. Тип один, реализация выбирается символами
/// условной компиляции (#if WINDOWS / #if LINUX).
/// </summary>
public interface IAutoStartService
{
    /// <summary>Доступен ли автозапуск на текущей платформе. Всегда <c>true</c> (и Windows, и Linux).</summary>
    bool IsAvailable { get; }

    /// <summary>true, если автозапуск приложения уже включён.</summary>
    bool IsEnabled();

    /// <summary>Включает автозапуск приложения.</summary>
    void Enable();

    /// <summary>Отключает автозапуск приложения.</summary>
    void Disable();
}