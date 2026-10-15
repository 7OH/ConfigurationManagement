using System.Net;
using System.Net.Mail;
using Configuration_Management.Models;

namespace Configuration_Management.Services;

/// <summary>
/// Канал уведомлений по электронной почте (функция №5, этап 2 — выпуск 0.3.12.69):
/// письмо отправляется через SMTP (<see cref="SmtpClient"/>, без внешних зависимостей).
/// Поддерживаются STARTTLS (порт 587, по умолчанию) и соединение без шифрования (25);
/// неявный SSL (порт 465) <see cref="SmtpClient"/> не поддерживает — в окне настроек
/// есть подсказка. Тихая деградация: ошибки → false, пароль в журнале маскируется.
/// </summary>
public sealed class EmailNotificationChannel : INotificationChannel
{
    private const int TimeoutMs = 20000;

    private readonly INotificationSettingsStore _store;
    private readonly IAppLogger? _logger;

    /// <param name="store">Хранилище настроек каналов.</param>
    /// <param name="logger">Журнал (может быть null).</param>
    public EmailNotificationChannel(INotificationSettingsStore store, IAppLogger? logger = null)
    {
        _store = store;
        _logger = logger;
    }

    /// <inheritdoc/>
    public string Name => "email";

    /// <inheritdoc/>
    public bool IsEnabled(AppSettings settings)
    {
        try { return IsConfigured(_store.Load()); }
        catch { return false; }
    }

    /// <summary>Канал включён, задан SMTP-сервер, есть отправитель и хотя бы один получатель.</summary>
    public static bool IsConfigured(NotificationChannelSettings s)
        => s is not null && s.EmailEnabled
           && !string.IsNullOrWhiteSpace(s.SmtpHost)
           && !string.IsNullOrWhiteSpace(ResolveFrom(s))
           && ParseRecipients(s.EmailTo).Count > 0;

    /// <inheritdoc/>
    public async Task<bool> SendAsync(NotificationMessage message, CancellationToken cancellationToken = default)
    {
        NotificationChannelSettings settings;
        try { settings = _store.Load(); }
        catch { return false; }
        if (!IsConfigured(settings) || !NotificationEventFilter.ShouldSend(settings, message))
            return true;
        var (ok, _) = await SendCoreAsync(settings, message, cancellationToken).ConfigureAwait(false);
        return ok;
    }

    /// <summary>Тестовая отправка с переданными (ещё не сохранёнными) настройками.</summary>
    public async Task<(bool Ok, string Error)> TestAsync(NotificationChannelSettings settings, NotificationMessage message,
        CancellationToken cancellationToken = default)
    {
        if (settings is null)
            return (false, "settings");
        var probe = settings.Clone();
        probe.EmailEnabled = true;
        if (!IsConfigured(probe))
            return (false, "smtp/from/to");
        return await SendCoreAsync(probe, message, cancellationToken).ConfigureAwait(false);
    }

    private async Task<(bool Ok, string Error)> SendCoreAsync(NotificationChannelSettings settings, NotificationMessage message,
        CancellationToken cancellationToken)
    {
        try
        {
            using var mail = BuildMailMessage(settings, message);
            using var client = new SmtpClient(settings.SmtpHost.Trim(), settings.SmtpPort)
            {
                EnableSsl = settings.SmtpUseStartTls,
                DeliveryMethod = SmtpDeliveryMethod.Network,
                Timeout = TimeoutMs,
            };
            if (!string.IsNullOrWhiteSpace(settings.SmtpUser))
            {
                client.UseDefaultCredentials = false;
                client.Credentials = new NetworkCredential(settings.SmtpUser.Trim(), settings.SmtpPassword ?? string.Empty);
            }
            await client.SendMailAsync(mail, cancellationToken).ConfigureAwait(false);
            return (true, string.Empty);
        }
        catch (Exception ex)
        {
            var text = ex.InnerException is null ? ex.Message : $"{ex.Message} ({ex.InnerException.Message})";
            var error = SensitiveDataMasker.MaskValue(text, settings.SmtpPassword);
            _logger?.Warn("Email: " + error);
            return (false, error);
        }
    }

    /// <summary>Адрес отправителя: явный «От кого» либо логин SMTP.</summary>
    public static string ResolveFrom(NotificationChannelSettings s)
        => !string.IsNullOrWhiteSpace(s.EmailFrom) ? s.EmailFrom.Trim() : (s.SmtpUser ?? string.Empty).Trim();

    /// <summary>Разбор списка получателей (разделители «,» «;» и пробельные символы), без дублей и пустых.</summary>
    public static IReadOnlyList<string> ParseRecipients(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return Array.Empty<string>();
        return text
            .Split(new[] { ',', ';', ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(a => a.Trim())
            .Where(a => a.Contains('@'))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Тема письма: «[Управление конфигурациями 1С] ❌ Заголовок».</summary>
    public static string BuildSubject(NotificationMessage message)
    {
        var marker = NotificationEventFilter.KindMarker(message?.Kind ?? NotificationKind.Info);
        var title = string.IsNullOrWhiteSpace(message?.Title) ? "Configuration Management" : message!.Title.Trim();
        return $"{marker} {title}";
    }

    /// <summary>Тело письма: текст сообщения и время события (локальное).</summary>
    public static string BuildBody(NotificationMessage message)
    {
        var body = (message?.Message ?? string.Empty).Trim();
        var at = (message?.UtcTimestamp ?? DateTimeOffset.UtcNow).ToLocalTime();
        return $"{body}\n\n—\n{at:yyyy-MM-dd HH:mm:ss} · {Environment.MachineName}";
    }

    /// <summary>Готовое письмо (UTF-8, простой текст).</summary>
    public static MailMessage BuildMailMessage(NotificationChannelSettings settings, NotificationMessage message)
    {
        var mail = new MailMessage
        {
            From = new MailAddress(ResolveFrom(settings)),
            Subject = BuildSubject(message),
            Body = BuildBody(message),
            IsBodyHtml = false,
            SubjectEncoding = System.Text.Encoding.UTF8,
            BodyEncoding = System.Text.Encoding.UTF8,
        };
        foreach (var to in ParseRecipients(settings.EmailTo))
            mail.To.Add(new MailAddress(to));
        return mail;
    }
}
