namespace Configuration_Management.Services;

/// <summary>
/// Вид уведомления (функция №5). Используется каналами для оформления сообщения:
/// например, пометки эмодзи/заголовка письма или цвета системного уведомления.
/// </summary>
public enum NotificationKind
{
    /// <summary>Информационное уведомление (по умолчанию).</summary>
    Info,

    /// <summary>Успешное завершение фоновой операции.</summary>
    Success,

    /// <summary>Предупреждение (частичный успех, требующее внимания).</summary>
    Warning,

    /// <summary>Ошибка фоновой операции.</summary>
    Error
}

/// <summary>
/// Категория события-источника уведомления (функция №5). Используется для фильтра
/// «какие события отправлять» (NotifyOnBackup/NotifyOnScheduledTasks/NotifyOnUpdates):
/// внешние каналы получают только события, включённые в настройках.
/// </summary>
public enum NotificationEvent
{
    /// <summary>Резервная копия (завершение успехом/ошибкой).</summary>
    Backup,

    /// <summary>Задание по расписанию (в т.ч. догоняющее выполнение пропусков).</summary>
    ScheduledTask,

    /// <summary>Обнаружение новой версии приложения.</summary>
    Update,

    /// <summary>Тестовое сообщение («Проверить подключение»); не фильтруется.</summary>
    ManualTest
}

/// <summary>
/// Сообщение уведомления, рассылаемое диспетчером по включённым каналам (функция №5).
/// Формируется в точке вызова и далее не меняется: каналы сами оформляют текст
/// (Telegram — MarkdownV2, email — письмо) и обрезают по своим лимитам.
/// </summary>
public sealed class NotificationMessage
{
    /// <summary>Заголовок уведомления (например, имя приложения).</summary>
    public string Title { get; init; } = "";

    /// <summary>Текст уведомления (может содержать форматные аргументы уже подставленными).</summary>
    public string Message { get; init; } = "";

    /// <summary>Вид уведомления (успех/ошибка/информация).</summary>
    public NotificationKind Kind { get; init; } = NotificationKind.Info;

    /// <summary>Категория события-источника (для фильтра NotifyOn*).</summary>
    public NotificationEvent Event { get; init; } = NotificationEvent.ManualTest;

    /// <summary>Метка времени события (UTC). Для шапки письма и логов.</summary>
    public DateTimeOffset UtcTimestamp { get; init; } = DateTimeOffset.UtcNow;
}