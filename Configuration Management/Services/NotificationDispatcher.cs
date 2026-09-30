using Configuration_Management.Models;

namespace Configuration_Management.Services;

/// <summary>
/// Мультиканальный диспетчер уведомлений (функция №5): рассылает сообщение всем включённым
/// каналам (<see cref="INotificationChannel"/>) параллельно. Остаётся единственной
/// реализацией <see cref="INotificationService"/> в контейнере — все существующие точки
/// вызова (резервная копия, задания по расписанию, обновление приложения) продолжают
/// работать через него без изменений.
///
/// Гарантии:
/// - не блокирует вызывающий поток: <see cref="Show(string,string)"/> и перегрузка —
///   fire-and-forget, весь сетевой/платформенный ввод-вывод выполняется в фоне;
/// - тихо деградирует: ошибки отдельного канала не всплывают наружу и не влияют на
///   остальные каналы; канал возвращает false — диспетчер только пишет предупреждение
///   в журнал с маскированием секретов (<see cref="SensitiveDataMasker"/>, этап 0.3.9.182);
/// - настройки читаются один раз на сообщение (<see cref="IInfobaseRepository.LoadSettings"/>),
///   поэтому изменения в окне настроек применяются без перезапуска.
/// </summary>
public sealed class NotificationDispatcher : INotificationService
{
    private readonly IEnumerable<INotificationChannel> _channels;
    private readonly IInfobaseRepository _repository;
    private readonly IAppLogger? _logger;

    /// <summary>
    /// Создаёт диспетчер. Каналы резолвятся контейнером из всех регистраций
    /// <see cref="INotificationChannel"/> (множественная регистрация в AppServices).
    /// </summary>
    public NotificationDispatcher(
        IEnumerable<INotificationChannel> channels,
        IInfobaseRepository repository,
        IAppLogger? logger = null)
    {
        _channels = channels;
        _repository = repository;
        _logger = logger;
    }

    /// <inheritdoc/>
    public void Show(string title, string message)
    {
        Show(title, message, NotificationKind.Info, NotificationEvent.ManualTest);
    }

    /// <inheritdoc/>
    public void Show(string title, string message, NotificationKind kind, NotificationEvent evt = NotificationEvent.ManualTest)
    {
        try
        {
            var notification = new NotificationMessage
            {
                Title = title ?? string.Empty,
                Message = message ?? string.Empty,
                Kind = kind,
                Event = evt
            };

            // Fire-and-forget: уведомление не должно блокировать фоновую операцию
            // (планировщик, автообновление) даже на время сетевых таймаутов.
            _ = SendCoreAsync(notification);
        }
        catch (Exception ex)
        {
            // Исключение до первого await (например, отказ контейнера) — гасим.
            _logger?.Warn($"Уведомления не отправлены: {ex.Message}");
        }
    }

    /// <summary>
    /// Рассылает сообщение включённым каналам. Ошибки каналов логируются и не влияют
    /// друг на друга; все сетевые операции используют ConfigureAwait(false).
    /// </summary>
    private async Task SendCoreAsync(NotificationMessage message)
    {
        try
        {
            AppSettings settings;
            try
            {
                settings = _repository.LoadSettings();
            }
            catch (Exception ex)
            {
                _logger?.Warn($"Не удалось прочитать настройки для уведомлений: {ex.Message}");
                return;
            }

            var enabled = _channels.Where(c => c.IsEnabled(settings)).ToList();
            if (enabled.Count == 0)
                return;

            await Task.WhenAll(enabled.Select(c => SendSafelyAsync(c, message))).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Непредвиденная ошибка оркестрации — не должна ронять фоновый поток.
            _logger?.Warn($"Ошибка рассылки уведомлений: {ex.Message}");
        }
    }

    /// <summary>
    /// Отправляет сообщение одним каналом с полной изоляцией ошибок: исключение канала
    /// или возврат false превращаются в предупреждение журнала, наружу не всплывают.
    /// </summary>
    private async Task SendSafelyAsync(INotificationChannel channel, NotificationMessage message)
    {
        try
        {
            var sent = await channel.SendAsync(message).ConfigureAwait(false);
            if (!sent)
                _logger?.Warn($"Канал «{channel.Name}» не смог отправить уведомление.");
        }
        catch (Exception ex)
        {
            _logger?.Warn($"Канал «{channel.Name}»: ошибка отправки: {ex.Message}");
        }
    }
}