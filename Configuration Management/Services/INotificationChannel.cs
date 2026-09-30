using Configuration_Management.Models;

namespace Configuration_Management.Services;

/// <summary>
/// Канал доставки уведомлений (функция №5): системные уведомления ОС, Telegram-бот,
/// email (SMTP). Диспетчер <see cref="NotificationDispatcher"/> рассылает сообщение
/// всем каналам, у которых <see cref="IsEnabled"/> возвращает true.
/// Реализации обязаны тихо деградировать: ошибки сети/платформы не всплывают наружу,
/// метод <see cref="SendAsync"/> возвращает false вместо исключения (диспетчер логирует).
/// </summary>
public interface INotificationChannel
{
    /// <summary>Отображаемое имя канала для журнала («system», «telegram», «email»).</summary>
    string Name { get; }

    /// <summary>
    /// true — канал включён и сконфигурирован в переданных настройках.
    /// Вызывается диспетчером один раз на сообщение до <see cref="SendAsync"/>.
    /// </summary>
    bool IsEnabled(AppSettings settings);

    /// <summary>
    /// Отправляет сообщение. Не должен блокировать вызывающий поток дольше собственного
    /// таймаута. Возвращает true при успешной отправке; false — канал не смог отправить
    /// (ошибка сети, недоступность платформы, невалидная конфигурация).
    /// </summary>
    Task<bool> SendAsync(NotificationMessage message, CancellationToken cancellationToken = default);
}