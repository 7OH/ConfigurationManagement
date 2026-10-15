namespace Configuration_Management.Models;

/// <summary>
/// Настройки внешних каналов уведомлений (функция №5, этап 2 — выпуск 0.3.12.69):
/// Telegram-бот и электронная почта (SMTP), а также фильтр «какие события отправлять».
/// Хранится отдельным файлом <c>notifications.json</c> в каталоге данных профиля
/// (см. <see cref="Services.NotificationSettingsStore"/>), а не в <c>settings.json</c>:
/// окно настроек уведомлений сохраняет файл независимо от главного окна.
/// Секреты (токен бота, пароль SMTP) хранятся как пароли ИТС — открыто в файле профиля
/// и маскируются при журналировании.
/// </summary>
public sealed class NotificationChannelSettings
{
    // ---------- Telegram ----------

    /// <summary>Канал Telegram включён.</summary>
    public bool TelegramEnabled { get; set; }

    /// <summary>Токен бота (выдаёт @BotFather), вида <c>123456:ABC-DEF…</c>.</summary>
    public string TelegramBotToken { get; set; } = "";

    /// <summary>Идентификатор чата/группы/канала (число, для групп — отрицательное, или <c>@channel</c>).</summary>
    public string TelegramChatId { get; set; } = "";

    // ---------- Email (SMTP) ----------

    /// <summary>Канал email включён.</summary>
    public bool EmailEnabled { get; set; }

    /// <summary>Адрес SMTP-сервера (например <c>smtp.yandex.ru</c>).</summary>
    public string SmtpHost { get; set; } = "";

    /// <summary>Порт SMTP: 587 — STARTTLS (по умолчанию), 25 — без шифрования.</summary>
    public int SmtpPort { get; set; } = 587;

    /// <summary>Использовать STARTTLS (шифрование соединения).</summary>
    public bool SmtpUseStartTls { get; set; } = true;

    /// <summary>Логин SMTP (пусто — без авторизации).</summary>
    public string SmtpUser { get; set; } = "";

    /// <summary>Пароль SMTP (для почтовых сервисов — пароль приложения).</summary>
    public string SmtpPassword { get; set; } = "";

    /// <summary>Адрес отправителя (пусто — используется логин SMTP).</summary>
    public string EmailFrom { get; set; } = "";

    /// <summary>Получатели через запятую или точку с запятой.</summary>
    public string EmailTo { get; set; } = "";

    // ---------- Фильтр событий ----------

    /// <summary>Отправлять результаты резервного копирования.</summary>
    public bool NotifyOnBackup { get; set; } = true;

    /// <summary>Отправлять результаты заданий по расписанию (в т.ч. проверки обновлений).</summary>
    public bool NotifyOnScheduledTasks { get; set; } = true;

    /// <summary>Отправлять сообщение о новой версии приложения.</summary>
    public bool NotifyOnUpdates { get; set; } = true;

    /// <summary>Отправлять только ошибки и предупреждения (успешные операции пропускаются).</summary>
    public bool OnlyProblems { get; set; }

    /// <summary>Глубокая копия (для редактирования в окне без изменения исходника).</summary>
    public NotificationChannelSettings Clone() => (NotificationChannelSettings)MemberwiseClone();
}
