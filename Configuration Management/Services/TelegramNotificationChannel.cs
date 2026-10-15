using System.Net.Http;
using System.Text;
using Configuration_Management.Models;

namespace Configuration_Management.Services;

/// <summary>
/// Канал уведомлений Telegram (функция №5, этап 2 — выпуск 0.3.12.69): сообщение
/// отправляется методом Bot API <c>sendMessage</c> в чат <see cref="NotificationChannelSettings.TelegramChatId"/>.
/// Текст — простой (без parse_mode), поэтому экранирование MarkdownV2 не требуется.
/// Тихая деградация: ошибки сети/API → false (диспетчер пишет предупреждение), токен
/// в журнале маскируется.
/// </summary>
public sealed class TelegramNotificationChannel : INotificationChannel
{
    /// <summary>Базовый адрес Bot API.</summary>
    public const string ApiBase = "https://api.telegram.org";

    /// <summary>Лимит длины сообщения Telegram (символов).</summary>
    public const int MaxMessageLength = 4096;

    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(15);

    private readonly INotificationSettingsStore _store;
    private readonly HttpMessageHandler? _handler;
    private readonly IAppLogger? _logger;

    /// <param name="store">Хранилище настроек каналов.</param>
    /// <param name="logger">Журнал (может быть null).</param>
    /// <param name="handler">Обработчик HTTP (подменяется в тестах; null — стандартный).</param>
    public TelegramNotificationChannel(INotificationSettingsStore store, IAppLogger? logger = null, HttpMessageHandler? handler = null)
    {
        _store = store;
        _logger = logger;
        _handler = handler;
    }

    /// <inheritdoc/>
    public string Name => "telegram";

    /// <inheritdoc/>
    public bool IsEnabled(AppSettings settings)
    {
        try { return IsConfigured(_store.Load()); }
        catch { return false; }
    }

    /// <summary>Канал включён и заполнены токен и чат.</summary>
    public static bool IsConfigured(NotificationChannelSettings s)
        => s is not null && s.TelegramEnabled
           && !string.IsNullOrWhiteSpace(s.TelegramBotToken)
           && !string.IsNullOrWhiteSpace(s.TelegramChatId);

    /// <inheritdoc/>
    public async Task<bool> SendAsync(NotificationMessage message, CancellationToken cancellationToken = default)
    {
        NotificationChannelSettings settings;
        try { settings = _store.Load(); }
        catch { return false; }
        if (!IsConfigured(settings) || !NotificationEventFilter.ShouldSend(settings, message))
            return true; // отфильтровано — это не ошибка канала
        var (ok, _) = await SendCoreAsync(settings, message, cancellationToken).ConfigureAwait(false);
        return ok;
    }

    /// <summary>
    /// Тестовая отправка с переданными (ещё не сохранёнными) настройками — для кнопки
    /// «Проверить Telegram». Возвращает признак успеха и текст ошибки.
    /// </summary>
    public async Task<(bool Ok, string Error)> TestAsync(NotificationChannelSettings settings, NotificationMessage message,
        CancellationToken cancellationToken = default)
    {
        if (settings is null || string.IsNullOrWhiteSpace(settings.TelegramBotToken) || string.IsNullOrWhiteSpace(settings.TelegramChatId))
            return (false, "token/chat_id");
        return await SendCoreAsync(settings, message, cancellationToken).ConfigureAwait(false);
    }

    private async Task<(bool Ok, string Error)> SendCoreAsync(NotificationChannelSettings settings, NotificationMessage message,
        CancellationToken cancellationToken)
    {
        var token = settings.TelegramBotToken.Trim();
        try
        {
            using var http = _handler is null ? new HttpClient() : new HttpClient(_handler, disposeHandler: false);
            http.Timeout = RequestTimeout;
            using var request = BuildRequest(token, settings.TelegramChatId, BuildText(message));
            using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
                return (true, string.Empty);
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var error = $"HTTP {(int)response.StatusCode}: {Truncate(body, 300)}";
            _logger?.Warn("Telegram: " + SensitiveDataMasker.MaskValue(error, token));
            return (false, SensitiveDataMasker.MaskValue(error, token));
        }
        catch (Exception ex)
        {
            var error = SensitiveDataMasker.MaskValue(ex.Message, token);
            _logger?.Warn("Telegram: " + error);
            return (false, error);
        }
    }

    /// <summary>Текст сообщения: маркер вида + заголовок, пустая строка, текст; обрезка до лимита.</summary>
    public static string BuildText(NotificationMessage message)
    {
        var marker = NotificationEventFilter.KindMarker(message?.Kind ?? NotificationKind.Info);
        var title = (message?.Title ?? string.Empty).Trim();
        var body = (message?.Message ?? string.Empty).Trim();
        var text = string.IsNullOrEmpty(title) ? $"{marker} {body}" : $"{marker} {title}\n\n{body}";
        return Truncate(text.Trim(), MaxMessageLength);
    }

    /// <summary>POST-запрос <c>/bot&lt;token&gt;/sendMessage</c> с form-urlencoded телом.</summary>
    public static HttpRequestMessage BuildRequest(string token, string chatId, string text)
    {
        var url = $"{ApiBase}/bot{token.Trim()}/sendMessage";
        var content = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("chat_id", (chatId ?? string.Empty).Trim()),
            new KeyValuePair<string, string>("text", text ?? string.Empty),
            new KeyValuePair<string, string>("disable_web_page_preview", "true"),
        });
        return new HttpRequestMessage(HttpMethod.Post, url) { Content = content };
    }

    private static string Truncate(string? text, int max)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= max)
            return text ?? string.Empty;
        return text.Substring(0, max - 1) + "…";
    }
}
