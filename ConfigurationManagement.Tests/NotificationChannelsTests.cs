using System.Net;
using System.Net.Http;
using Configuration_Management.Models;
using Configuration_Management.Services;
using Xunit;

namespace ConfigurationManagement.Tests;

/// <summary>
/// Внешние каналы уведомлений (выпуск 0.3.12.69): фильтр событий, Telegram (Bot API
/// sendMessage через подменённый HTTP-обработчик), email (разбор получателей и письмо),
/// хранилище notifications.json.
/// </summary>
public sealed class NotificationChannelsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "cm-notify-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); } catch { }
    }

    private static NotificationMessage Msg(NotificationEvent evt, NotificationKind kind = NotificationKind.Success) => new()
    {
        Title = "Заголовок",
        Message = "Текст",
        Kind = kind,
        Event = evt
    };

    // ---------- Фильтр событий ----------

    [Fact]
    public void Filter_ManualTest_AlwaysSent()
    {
        var s = new NotificationChannelSettings { NotifyOnBackup = false, NotifyOnScheduledTasks = false, NotifyOnUpdates = false, OnlyProblems = true };
        Assert.True(NotificationEventFilter.ShouldSend(s, Msg(NotificationEvent.ManualTest, NotificationKind.Info)));
    }

    [Fact]
    public void Filter_General_NeverSent()
    {
        Assert.False(NotificationEventFilter.ShouldSend(new NotificationChannelSettings(), Msg(NotificationEvent.General)));
    }

    [Theory]
    [InlineData(NotificationEvent.Backup)]
    [InlineData(NotificationEvent.ScheduledTask)]
    [InlineData(NotificationEvent.Update)]
    public void Filter_EventFlagsRespected(NotificationEvent evt)
    {
        var on = new NotificationChannelSettings();
        Assert.True(NotificationEventFilter.ShouldSend(on, Msg(evt)));

        var off = new NotificationChannelSettings { NotifyOnBackup = false, NotifyOnScheduledTasks = false, NotifyOnUpdates = false };
        Assert.False(NotificationEventFilter.ShouldSend(off, Msg(evt)));
    }

    [Fact]
    public void Filter_OnlyProblems_SkipsSuccess_KeepsErrorsAndWarnings()
    {
        var s = new NotificationChannelSettings { OnlyProblems = true };
        Assert.False(NotificationEventFilter.ShouldSend(s, Msg(NotificationEvent.Backup, NotificationKind.Success)));
        Assert.False(NotificationEventFilter.ShouldSend(s, Msg(NotificationEvent.Backup, NotificationKind.Info)));
        Assert.True(NotificationEventFilter.ShouldSend(s, Msg(NotificationEvent.Backup, NotificationKind.Error)));
        Assert.True(NotificationEventFilter.ShouldSend(s, Msg(NotificationEvent.Backup, NotificationKind.Warning)));
    }

    // ---------- Хранилище ----------

    [Fact]
    public void Store_MissingFile_ReturnsDefaults()
    {
        var store = new NotificationSettingsStore(directoryOverride: _dir);
        var s = store.Load();
        Assert.False(s.TelegramEnabled);
        Assert.False(s.EmailEnabled);
        Assert.Equal(587, s.SmtpPort);
        Assert.True(s.NotifyOnBackup);
    }

    [Fact]
    public void Store_RoundTrip_TrimsAndKeepsValues()
    {
        var store = new NotificationSettingsStore(directoryOverride: _dir);
        store.Save(new NotificationChannelSettings
        {
            TelegramEnabled = true,
            TelegramBotToken = " 123:ABC ",
            TelegramChatId = " -100500 ",
            EmailEnabled = true,
            SmtpHost = "smtp.example.com",
            SmtpPort = 0,
            EmailTo = "a@b.ru",
            OnlyProblems = true
        });
        Assert.True(File.Exists(store.FilePath));

        var s = store.Load();
        Assert.True(s.TelegramEnabled);
        Assert.Equal("123:ABC", s.TelegramBotToken);
        Assert.Equal("-100500", s.TelegramChatId);
        Assert.Equal(587, s.SmtpPort); // некорректный порт нормализуется
        Assert.True(s.OnlyProblems);
    }

    [Fact]
    public void Store_CorruptFile_ReturnsDefaults()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, NotificationSettingsStore.FileName), "{ not json");
        var s = new NotificationSettingsStore(directoryOverride: _dir).Load();
        Assert.False(s.TelegramEnabled);
    }

    // ---------- Telegram ----------

    [Fact]
    public void Telegram_BuildText_HasMarkerTitleAndBody()
    {
        var text = TelegramNotificationChannel.BuildText(Msg(NotificationEvent.Backup, NotificationKind.Error));
        Assert.StartsWith("❌ Заголовок", text);
        Assert.Contains("Текст", text);
    }

    [Fact]
    public void Telegram_BuildText_TruncatesToLimit()
    {
        var msg = new NotificationMessage { Title = "T", Message = new string('x', 10000) };
        Assert.True(TelegramNotificationChannel.BuildText(msg).Length <= TelegramNotificationChannel.MaxMessageLength);
    }

    [Fact]
    public async Task Telegram_BuildRequest_UrlAndForm()
    {
        using var request = TelegramNotificationChannel.BuildRequest("123:ABC", "42", "hello");
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://api.telegram.org/bot123:ABC/sendMessage", request.RequestUri!.ToString());
        var body = await request.Content!.ReadAsStringAsync();
        Assert.Contains("chat_id=42", body);
        Assert.Contains("text=hello", body);
    }

    [Fact]
    public void Telegram_IsConfigured_RequiresTokenAndChat()
    {
        Assert.False(TelegramNotificationChannel.IsConfigured(new NotificationChannelSettings { TelegramEnabled = true }));
        Assert.False(TelegramNotificationChannel.IsConfigured(new NotificationChannelSettings { TelegramBotToken = "t", TelegramChatId = "1" }));
        Assert.True(TelegramNotificationChannel.IsConfigured(new NotificationChannelSettings { TelegramEnabled = true, TelegramBotToken = "t", TelegramChatId = "1" }));
    }

    [Fact]
    public async Task Telegram_SendAsync_PostsToApi()
    {
        var store = new NotificationSettingsStore(directoryOverride: _dir);
        store.Save(new NotificationChannelSettings { TelegramEnabled = true, TelegramBotToken = "123:ABC", TelegramChatId = "42" });
        var handler = new RecordingHandler(HttpStatusCode.OK);
        var channel = new TelegramNotificationChannel(store, null, handler);

        Assert.True(channel.IsEnabled(new AppSettings()));
        var ok = await channel.SendAsync(Msg(NotificationEvent.Backup));

        Assert.True(ok);
        Assert.Equal(1, handler.Calls);
        Assert.Contains("/bot123:ABC/sendMessage", handler.LastUri);
    }

    [Fact]
    public async Task Telegram_SendAsync_FilteredEvent_NoRequest()
    {
        var store = new NotificationSettingsStore(directoryOverride: _dir);
        store.Save(new NotificationChannelSettings { TelegramEnabled = true, TelegramBotToken = "t", TelegramChatId = "1", NotifyOnBackup = false });
        var handler = new RecordingHandler(HttpStatusCode.OK);
        var channel = new TelegramNotificationChannel(store, null, handler);

        Assert.True(await channel.SendAsync(Msg(NotificationEvent.Backup)));
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task Telegram_TestAsync_HttpError_ReturnsFalseAndMasksToken()
    {
        var store = new NotificationSettingsStore(directoryOverride: _dir);
        var handler = new RecordingHandler(HttpStatusCode.Unauthorized, "{\"ok\":false,\"description\":\"Unauthorized SECRET\"}");
        var channel = new TelegramNotificationChannel(store, null, handler);

        var (ok, error) = await channel.TestAsync(
            new NotificationChannelSettings { TelegramBotToken = "SECRET", TelegramChatId = "1" },
            Msg(NotificationEvent.ManualTest));

        Assert.False(ok);
        Assert.Contains("401", error);
        Assert.DoesNotContain("SECRET", error);
    }

    // ---------- Email ----------

    [Fact]
    public void Email_ParseRecipients_SplitsAndDeduplicates()
    {
        var list = EmailNotificationChannel.ParseRecipients("a@b.ru; c@d.ru,  A@B.RU\nnot-an-address");
        Assert.Equal(new[] { "a@b.ru", "c@d.ru" }, list);
        Assert.Empty(EmailNotificationChannel.ParseRecipients(null));
    }

    [Fact]
    public void Email_ResolveFrom_FallsBackToLogin()
    {
        Assert.Equal("user@x.ru", EmailNotificationChannel.ResolveFrom(new NotificationChannelSettings { SmtpUser = "user@x.ru" }));
        Assert.Equal("bot@x.ru", EmailNotificationChannel.ResolveFrom(new NotificationChannelSettings { SmtpUser = "user@x.ru", EmailFrom = "bot@x.ru" }));
    }

    [Fact]
    public void Email_IsConfigured_RequiresHostFromAndRecipients()
    {
        var s = new NotificationChannelSettings { EmailEnabled = true, SmtpHost = "smtp.x.ru", SmtpUser = "u@x.ru" };
        Assert.False(EmailNotificationChannel.IsConfigured(s));
        s.EmailTo = "admin@x.ru";
        Assert.True(EmailNotificationChannel.IsConfigured(s));
        s.EmailEnabled = false;
        Assert.False(EmailNotificationChannel.IsConfigured(s));
    }

    [Fact]
    public void Email_BuildMailMessage_SubjectBodyRecipients()
    {
        var s = new NotificationChannelSettings { SmtpUser = "u@x.ru", EmailTo = "a@x.ru, b@x.ru" };
        using var mail = EmailNotificationChannel.BuildMailMessage(s, Msg(NotificationEvent.Backup, NotificationKind.Error));
        Assert.Equal("u@x.ru", mail.From!.Address);
        Assert.Equal(2, mail.To.Count);
        Assert.Contains("Заголовок", mail.Subject);
        Assert.StartsWith("❌", mail.Subject);
        Assert.Contains("Текст", mail.Body);
        Assert.False(mail.IsBodyHtml);
    }

    [Fact]
    public async Task Email_TestAsync_Incomplete_ReturnsFalseWithoutNetwork()
    {
        var channel = new EmailNotificationChannel(new NotificationSettingsStore(directoryOverride: _dir));
        var (ok, _) = await channel.TestAsync(new NotificationChannelSettings { SmtpHost = "" }, Msg(NotificationEvent.ManualTest));
        Assert.False(ok);
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly string _body;

        public RecordingHandler(HttpStatusCode status, string body = "{\"ok\":true}")
        {
            _status = status;
            _body = body;
        }

        public int Calls { get; private set; }
        public string LastUri { get; private set; } = "";

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            LastUri = request.RequestUri?.ToString() ?? "";
            return Task.FromResult(new HttpResponseMessage(_status) { Content = new StringContent(_body) });
        }
    }
}
