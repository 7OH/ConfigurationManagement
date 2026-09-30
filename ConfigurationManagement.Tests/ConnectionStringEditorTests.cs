using Configuration_Management.Models;
using Configuration_Management.Services;
using Xunit;

namespace ConfigurationManagement.Tests;

/// <summary>
/// Тесты чистого редактора строки подключения (0.3.9.187, функция 6 «Массовая замена
/// в строке подключения баз»): разбор всех типов строк через
/// <see cref="ConnectionStringEditor.Parse"/>, нормализация сборки через
/// <see cref="ConnectionStringEditor.Build"/> и применение правил замены по полям
/// (<see cref="ConnectionStringEditor.TryApply"/>/<see cref="ConnectionStringEditor.TryApplyRaw"/>).
/// </summary>
public sealed class ConnectionStringEditorTests
{
    // ---------- Разбор всех типов строк ----------

    [Fact]
    public void Parse_File()
    {
        var s = ConnectionStringEditor.Parse(@"File=""C:\base""");

        Assert.Equal(ConnectionType.File, s.Type);
        Assert.Equal(@"C:\base", s.FilePath);
        Assert.Equal(@"File=""C:\base""", ConnectionStringEditor.Build(s));
    }

    [Fact]
    public void Parse_ClientServer()
    {
        var s = ConnectionStringEditor.Parse(@"Srvr=""host"";Ref=""База""");

        Assert.Equal(ConnectionType.ClientServer, s.Type);
        Assert.Equal("host", s.Server);
        Assert.Equal(1541, s.Port); // порт по умолчанию, в строке опущен
        Assert.Equal("База", s.DatabaseName);
        Assert.Equal(@"Srvr=""host"";Ref=""База""", ConnectionStringEditor.Build(s));
    }

    [Fact]
    public void Parse_ClientServer_NonStandardPort()
    {
        var s = ConnectionStringEditor.Parse(@"Srvr=""host:2541"";Ref=""База""");

        Assert.Equal(ConnectionType.ClientServer, s.Type);
        Assert.Equal("host", s.Server);
        Assert.Equal(2541, s.Port);
        Assert.Equal("База", s.DatabaseName);
        Assert.Equal(@"Srvr=""host:2541"";Ref=""База""", ConnectionStringEditor.Build(s));
    }

    [Fact]
    public void Parse_WebServer()
    {
        var s = ConnectionStringEditor.Parse(@"WS=""http://host/base""");

        Assert.Equal(ConnectionType.WebServer, s.Type);
        Assert.Equal("http://host/base", s.WebUrl);
        Assert.Equal(@"WS=""http://host/base""", ConnectionStringEditor.Build(s));
    }

    [Fact]
    public void Parse_Empty_ReturnsDefaults()
    {
        var s = ConnectionStringEditor.Parse(string.Empty);

        Assert.Equal(ConnectionType.ClientServer, s.Type);
        Assert.Equal(string.Empty, s.Server);
        Assert.Equal(string.Empty, s.DatabaseName);
        Assert.Equal(string.Empty, s.FilePath);
        Assert.Equal(string.Empty, s.WebUrl);
    }

    [Fact]
    public void Parse_Garbage_ReturnsEmptyClientServer()
    {
        // «abc» не содержит ни одного известного ключа — разбор падает в ветку
        // клиент-сервер с пустыми полями (фактическое поведение ParseConnectionString).
        var s = ConnectionStringEditor.Parse("abc");

        Assert.Equal(ConnectionType.ClientServer, s.Type);
        Assert.Equal(string.Empty, s.Server);
        Assert.Equal(string.Empty, s.DatabaseName);
        Assert.Equal(@"Srvr="""";Ref=""""", ConnectionStringEditor.Build(s));
    }

    [Fact]
    public void Parse_SpacesAroundEquals()
    {
        var s = ConnectionStringEditor.Parse(@"Srvr = ""host""");

        Assert.Equal(ConnectionType.ClientServer, s.Type);
        Assert.Equal("host", s.Server);
        Assert.Equal(@"Srvr=""host"";Ref=""""", ConnectionStringEditor.Build(s));
    }

    [Fact]
    public void Parse_UnquotedValues()
    {
        var s = ConnectionStringEditor.Parse("Srvr=host;Ref=База");

        Assert.Equal("host", s.Server);
        Assert.Equal("База", s.DatabaseName);
        Assert.Equal(@"Srvr=""host"";Ref=""База""", ConnectionStringEditor.Build(s));
    }

    // ---------- Нормализация сборки ----------

    [Fact]
    public void Build_OmitsDefaultPort1541()
    {
        var s = ConnectionStringEditor.Parse(@"Srvr=""host:1541"";Ref=""База""");

        // Порт 1541 — стандартный: в канонической строке опускается (GetServerWithPort).
        Assert.Equal(@"Srvr=""host"";Ref=""База""", ConnectionStringEditor.Build(s));
    }

    [Fact]
    public void Build_KeepsNonStandardPort()
    {
        var s = ConnectionStringEditor.Parse(@"Srvr=""host:2541"";Ref=""База""");

        Assert.Equal(@"Srvr=""host:2541"";Ref=""База""", ConnectionStringEditor.Build(s));
    }

    [Fact]
    public void Build_Ipv6_KeepsBrackets()
    {
        var s = ConnectionStringEditor.Parse(@"Srvr=""[2001:db8::1]:1541"";Ref=""База""");

        Assert.Equal("[2001:db8::1]", s.Server);
        Assert.Equal(1541, s.Port);
        Assert.Equal(@"Srvr=""[2001:db8::1]"";Ref=""База""", ConnectionStringEditor.Build(s));
    }

    [Fact]
    public void Build_Quotes_RoundTrip()
    {
        const string raw = @"Srvr=""srv"";Ref=""База """"кавычка""""""";
        var s = ConnectionStringEditor.Parse(raw);

        Assert.Equal(@"База ""кавычка""", s.DatabaseName);
        // Симметрия Parse → Build: удвоенная кавычка внутри значения сохраняется.
        Assert.Equal(raw, ConnectionStringEditor.Build(s));
    }

    // ---------- Замена по полям: Server ----------

    [Fact]
    public void Replace_Server_Exact()
    {
        var s = ConnectionStringEditor.Parse(@"Srvr=""server1"";Ref=""База""");

        var ok = ConnectionStringEditor.TryApply(
            s, Rule("server1", "server2", ConnectionField.Server),
            out var updated, out var before, out var after);

        Assert.True(ok);
        Assert.Equal("server1", before);
        Assert.Equal("server2", after);
        Assert.Equal("server2", updated.Server);
        Assert.Equal("База", updated.DatabaseName);
        Assert.Equal(@"Srvr=""server2"";Ref=""База""", ConnectionStringEditor.Build(updated));
        // Входные настройки не мутируются.
        Assert.Equal("server1", s.Server);
    }

    [Fact]
    public void Replace_Server_Substring()
    {
        var s = ConnectionStringEditor.Parse(@"Srvr=""server-01"";Ref=""Б""");

        var ok = ConnectionStringEditor.TryApply(
            s, Rule("server", "server2", ConnectionField.Server, ConnectionMatchMode.Substring),
            out var updated, out _, out _);

        Assert.True(ok);
        Assert.Equal("server2-01", updated.Server);
    }

    [Fact]
    public void Replace_Server_Prefix()
    {
        var s = ConnectionStringEditor.Parse(@"Srvr=""server-01"";Ref=""Б""");

        var ok = ConnectionStringEditor.TryApply(
            s, Rule("server", "server2", ConnectionField.Server, ConnectionMatchMode.Prefix),
            out var updated, out _, out _);

        Assert.True(ok);
        Assert.Equal("server2-01", updated.Server);
    }

    [Fact]
    public void Replace_Server_CaseSensitive_NoMatch()
    {
        var s = ConnectionStringEditor.Parse(@"Srvr=""SERVER1"";Ref=""Б""");

        var ok = ConnectionStringEditor.TryApply(
            s, Rule("server1", "server2", ConnectionField.Server, ignoreCase: false),
            out var updated, out var before, out var after);

        Assert.False(ok);
        Assert.Equal(string.Empty, before);
        Assert.Equal(string.Empty, after);
        Assert.Equal(string.Empty, updated.Server);
    }

    [Fact]
    public void Replace_Server_IgnoreCase()
    {
        var s = ConnectionStringEditor.Parse(@"Srvr=""SERVER1"";Ref=""Б""");

        var ok = ConnectionStringEditor.TryApply(
            s, Rule("server1", "server2", ConnectionField.Server, ignoreCase: true),
            out var updated, out _, out _);

        Assert.True(ok);
        Assert.Equal("server2", updated.Server);
    }

    [Fact]
    public void Replace_Server_PreservesOtherFields()
    {
        var s = ConnectionStringEditor.Parse(
            @"Srvr=""srv1"";Ref=""База"";Usr=user;Pwd=pass;SchJobDn=Y;disstt=Y");

        Assert.Equal("user", s.User);
        Assert.Equal("pass", s.Password);
        Assert.True(s.BlockScheduledJobs);
        Assert.True(s.ForbidSpeechRecognition);

        var ok = ConnectionStringEditor.TryApply(
            s, Rule("srv1", "srv2", ConnectionField.Server),
            out var updated, out _, out _);

        Assert.True(ok);
        Assert.Equal("srv2", updated.Server);
        Assert.Equal("База", updated.DatabaseName);
        // Прочие параметры (Usr/Pwd/SchJobDn/disstt) на месте.
        Assert.Equal("user", updated.User);
        Assert.Equal("pass", updated.Password);
        Assert.True(updated.BlockScheduledJobs);
        Assert.True(updated.ForbidSpeechRecognition);
    }

    // ---------- Замена по полям: Port ----------

    [Fact]
    public void Replace_Port_AddsPortToServerString()
    {
        var s = ConnectionStringEditor.Parse(@"Srvr=""server1"";Ref=""Б""");
        Assert.Equal(1541, s.Port); // порт опущен

        var ok = ConnectionStringEditor.TryApply(
            s, Rule("1541", "2541", ConnectionField.Port),
            out var updated, out var before, out var after);

        Assert.True(ok);
        Assert.Equal("1541", before);
        Assert.Equal("2541", after);
        Assert.Equal(2541, updated.Port);
        // Нестандартный порт добавляется в строку при сборке.
        Assert.Equal(@"Srvr=""server1:2541"";Ref=""Б""", ConnectionStringEditor.Build(updated));
    }

    [Fact]
    public void Replace_Port_RemovesPortFromServerString()
    {
        var s = ConnectionStringEditor.Parse(@"Srvr=""server1:2541"";Ref=""Б""");
        Assert.Equal(2541, s.Port);

        var ok = ConnectionStringEditor.TryApply(
            s, Rule("2541", "1541", ConnectionField.Port),
            out var updated, out _, out _);

        Assert.True(ok);
        Assert.Equal(1541, updated.Port);
        // Стандартный порт 1541 опускается при сборке.
        Assert.Equal(@"Srvr=""server1"";Ref=""Б""", ConnectionStringEditor.Build(updated));
    }

    [Fact]
    public void Replace_Port_NoChange()
    {
        var s = ConnectionStringEditor.Parse(@"Srvr=""server1"";Ref=""Б""");
        Assert.Equal(1541, s.Port);

        var ok = ConnectionStringEditor.TryApply(
            s, Rule("1541", "1541", ConnectionField.Port),
            out var updated, out var before, out var after);

        Assert.False(ok);
        Assert.Equal(string.Empty, before);
        Assert.Equal(string.Empty, after);
        Assert.Equal(1541, updated.Port); // валидный, но неизменный дефолт
    }

    // ---------- Замена по полям: Ref / FilePath / WebUrl ----------

    [Fact]
    public void Replace_Ref()
    {
        var s = ConnectionStringEditor.Parse(@"Srvr=""server1"";Ref=""База""");

        var ok = ConnectionStringEditor.TryApply(
            s, Rule("База", "База_new", ConnectionField.Ref),
            out var updated, out var before, out var after);

        Assert.True(ok);
        Assert.Equal("База", before);
        Assert.Equal("База_new", after);
        Assert.Equal("База_new", updated.DatabaseName);
        Assert.Equal("server1", updated.Server); // Srvr не трогается
        Assert.Equal(@"Srvr=""server1"";Ref=""База_new""", ConnectionStringEditor.Build(updated));
    }

    [Fact]
    public void Replace_FilePath()
    {
        var s = ConnectionStringEditor.Parse(@"File=""C:\base""");

        var ok = ConnectionStringEditor.TryApply(
            s, Rule(@"C:\base", @"D:\base", ConnectionField.FilePath),
            out var updated, out _, out _);

        Assert.True(ok);
        Assert.Equal(@"D:\base", updated.FilePath);
        Assert.Equal(@"File=""D:\base""", ConnectionStringEditor.Build(updated));
    }

    [Fact]
    public void Replace_WebUrl()
    {
        var s = ConnectionStringEditor.Parse(@"WS=""http://old.example.com/base""");

        var ok = ConnectionStringEditor.TryApply(
            s, Rule("old.example.com", "new.example.com", ConnectionField.WebUrl, ConnectionMatchMode.Substring),
            out var updated, out var before, out var after);

        Assert.True(ok);
        // «было → станет» — старое и новое значение всего поля WebUrl.
        Assert.Equal("http://old.example.com/base", before);
        Assert.Equal("http://new.example.com/base", after);
        Assert.Equal("http://new.example.com/base", updated.WebUrl);
        Assert.Equal(@"WS=""http://new.example.com/base""", ConnectionStringEditor.Build(updated));
    }

    // ---------- Замена по полям: Any (вся строка) ----------

    [Fact]
    public void Replace_Any_Raw_PreservesSegments()
    {
        const string raw = @"Srvr=""server1"";Ref=""База"";Usr=user;Pwd=pass;SchJobDn=Y";

        var ok = ConnectionStringEditor.TryApplyRaw(
            raw, Rule("server1", "server2", ConnectionField.Any, ConnectionMatchMode.Substring),
            out var updated);

        Assert.True(ok);
        // Сегментная замена значения: кавычки, экранирование и прочие параметры целы.
        Assert.Contains(@"Srvr=""server2""", updated);
        Assert.Contains("Ref=\"База\"", updated);
        Assert.Contains(";Usr=user", updated);
        Assert.Contains(";Pwd=pass", updated);
        Assert.Contains(";SchJobDn=Y", updated);
    }

    [Fact]
    public void Replace_Any_Settings()
    {
        var s = ConnectionStringEditor.Parse(@"Srvr=""server1"";Ref=""База""");

        var ok = ConnectionStringEditor.TryApply(
            s, Rule("server1", "server2", ConnectionField.Any, ConnectionMatchMode.Substring),
            out var updated, out var before, out var after);

        Assert.True(ok);
        Assert.Equal(@"Srvr=""server1"";Ref=""База""", before);
        Assert.Equal(@"Srvr=""server2"";Ref=""База""", after);
        Assert.Equal("server2", updated.Server);
        Assert.Equal("База", updated.DatabaseName);
    }

    // ---------- Regex ----------

    [Fact]
    public void Replace_Regex_Pattern()
    {
        var s = ConnectionStringEditor.Parse(@"Srvr=""host01"";Ref=""Б""");

        var ok = ConnectionStringEditor.TryApply(
            s, Rule("^host\\d+", "host02", ConnectionField.Server, ConnectionMatchMode.Regex),
            out var updated, out var before, out var after);

        Assert.True(ok);
        Assert.Equal("host01", before);
        Assert.Equal("host02", after);
        Assert.Equal("host02", updated.Server);
    }

    [Fact]
    public void Replace_Regex_IgnoreCase()
    {
        var s = ConnectionStringEditor.Parse(@"Srvr=""HOST01"";Ref=""Б""");

        var ok = ConnectionStringEditor.TryApply(
            s, Rule("^host\\d+", "host02", ConnectionField.Server, ConnectionMatchMode.Regex, ignoreCase: true),
            out var updated, out _, out _);

        Assert.True(ok);
        Assert.Equal("host02", updated.Server);
    }

    [Fact]
    public void Replace_Regex_InvalidPattern_Throws()
    {
        var s = ConnectionStringEditor.Parse(@"Srvr=""host01"";Ref=""Б""");
        var rule = Rule("(", "host02", ConnectionField.Server, ConnectionMatchMode.Regex);

        var ex = Assert.Throws<ArgumentException>(
            () => ConnectionStringEditor.TryApply(s, rule, out _, out _, out _));

        Assert.Equal("rule", ex.ParamName);
    }

    [Fact]
    public void ReplaceRaw_Regex_InvalidPattern_Throws()
    {
        var rule = Rule("(", "x", ConnectionField.Any, ConnectionMatchMode.Regex);

        var ex = Assert.Throws<ArgumentException>(
            () => ConnectionStringEditor.TryApplyRaw(@"Srvr=""host01""", rule, out _));

        Assert.Equal("rule", ex.ParamName);
    }

    // ---------- Крайние случаи ----------

    [Fact]
    public void Edge_EmptyFind_NotApplied()
    {
        var s = ConnectionStringEditor.Parse(@"Srvr=""server1"";Ref=""Б""");

        var ok = ConnectionStringEditor.TryApply(
            s, Rule(string.Empty, "server2", ConnectionField.Server, ConnectionMatchMode.Substring),
            out var updated, out _, out _);

        Assert.False(ok);
        Assert.Equal(string.Empty, updated.Server);
    }

    [Fact]
    public void Edge_FieldNotApplicable_FilePathOnServerBase()
    {
        var s = ConnectionStringEditor.Parse(@"Srvr=""server1"";Ref=""Б""");

        var ok = ConnectionStringEditor.TryApply(
            s, Rule("C:", "D:", ConnectionField.FilePath, ConnectionMatchMode.Substring),
            out var updated, out _, out _);

        Assert.False(ok);
        Assert.Equal(string.Empty, updated.FilePath);
    }

    [Fact]
    public void Edge_FieldNotApplicable_ServerOnFileBase()
    {
        var s = ConnectionStringEditor.Parse(@"File=""C:\base""");

        var ok = ConnectionStringEditor.TryApply(
            s, Rule("server", "server2", ConnectionField.Server, ConnectionMatchMode.Substring),
            out var updated, out _, out _);

        Assert.False(ok);
        Assert.Equal(string.Empty, updated.Server);
    }

    [Fact]
    public void Edge_BaseWithoutConnection_NotApplied()
    {
        var s = ConnectionStringEditor.Parse(string.Empty);

        var okServer = ConnectionStringEditor.TryApply(
            s, Rule("server1", "server2", ConnectionField.Server),
            out _, out _, out _);
        var okPort = ConnectionStringEditor.TryApply(
            s, Rule("1541", "2541", ConnectionField.Port),
            out _, out _, out _);
        var okAny = ConnectionStringEditor.TryApply(
            s, Rule("Srvr", "Srvr2", ConnectionField.Any, ConnectionMatchMode.Substring),
            out _, out _, out _);

        Assert.False(okServer);
        Assert.False(okPort);
        Assert.False(okAny);
    }

    [Fact]
    public void Edge_RawEmpty_NotApplied()
    {
        var ok = ConnectionStringEditor.TryApplyRaw(
            string.Empty, Rule("a", "b", ConnectionField.Any, ConnectionMatchMode.Substring), out var updated);

        Assert.False(ok);
        Assert.Equal(string.Empty, updated);
    }

    [Fact]
    public void Edge_RawNoMatch_NotApplied()
    {
        var ok = ConnectionStringEditor.TryApplyRaw(
            @"Srvr=""server1""", Rule("other", "x", ConnectionField.Any, ConnectionMatchMode.Substring), out var updated);

        Assert.False(ok);
        Assert.Equal(string.Empty, updated);
    }

    [Fact]
    public void Edge_Substring_Metacharacters_Literal()
    {
        // Точка в искомом тексте трактуется буквально: «srv1.test» совпадает только с точкой.
        var withDot = ConnectionStringEditor.Parse(@"Srvr=""srv1.test"";Ref=""Б""");
        var ok = ConnectionStringEditor.TryApply(
            withDot, Rule("srv1.test", "srv2.test", ConnectionField.Server, ConnectionMatchMode.Substring),
            out var updated, out _, out _);
        Assert.True(ok);
        Assert.Equal("srv2.test", updated.Server);

        // Без экранирования «.» совпала бы с «X»; с буквальным сопоставлением — нет совпадения.
        var withoutDot = ConnectionStringEditor.Parse(@"Srvr=""srv1Xtest"";Ref=""Б""");
        var noMatch = ConnectionStringEditor.TryApply(
            withoutDot, Rule("srv1.test", "srv2.test", ConnectionField.Server, ConnectionMatchMode.Substring),
            out var unchanged, out _, out _);
        Assert.False(noMatch);
        Assert.Equal(string.Empty, unchanged.Server);
    }

    // ---------- Помощники ----------

    private static ConnectionStringReplaceRule Rule(
        string find,
        string replace,
        ConnectionField field,
        ConnectionMatchMode mode = ConnectionMatchMode.Exact,
        bool ignoreCase = false)
        => new(find, replace, field, mode, ignoreCase);
}