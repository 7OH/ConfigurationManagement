using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Configuration_Management.Services;
using Xunit;

namespace ConfigurationManagement.Tests;

/// <summary>
/// Тесты чистой сериализации/усечения диагностики <c>menuclose_trace.json</c>
/// (issue #340, план 0.3.9.306, раздел 2.6): одна JSONL-запись — валидный JSON
/// с ключами латиницей; круговое усечение при превышении лимита (остаётся хвост +
/// маркер <c>truncated</c> первой строкой); startup-запись содержит версию/платформу;
/// чувствительные поля (пароли/токены) в записи не выводятся.
/// </summary>
public sealed class MenuCloseTraceFormatTests
{
    private static readonly DateTimeOffset Ts =
        new DateTimeOffset(2026, 10, 4, 20, 30, 0, TimeSpan.FromHours(3));

    // ============ BuildLine: одна JSON-строка, ключи латиницей ============

    [Fact]
    public void BuildLine_SingleValidJsonLine_LatinKeys()
    {
        var line = MenuCloseTraceFormat.BuildLine(
            "log",
            Ts,
            threadId: 12,
            new Dictionary<string, object?>
            {
                ["message"] = "EnsureStable: target=abc, done=true"
            });

        // Одна строка без переводов — каждая строка файла является валидным JSON.
        Assert.DoesNotContain('\n', line);
        using var doc = JsonDocument.Parse(line);
        var root = doc.RootElement;
        Assert.Equal(JsonValueKind.Object, root.ValueKind);

        var keys = root.EnumerateObject().Select(p => p.Name).ToArray();
        Assert.Equal(new[] { "ts", "thread", "event", "data" }, keys);
        Assert.All(keys, key => Assert.Matches("^[a-zA-Z]+$", key));

        Assert.Equal("log", root.GetProperty("event").GetString());
        Assert.Equal(12, root.GetProperty("thread").GetInt32());
        Assert.Equal(Ts.ToString("yyyy-MM-dd'T'HH:mm:ss.fffK"), root.GetProperty("ts").GetString());
        Assert.Equal(
            "EnsureStable: target=abc, done=true",
            root.GetProperty("data").GetProperty("message").GetString());
    }

    [Fact]
    public void BuildLine_MessagesWithQuotesNewlinesAndCyrillic_StayValidJson()
    {
        // Кавычки, перевод строки и кириллица в сообщении не должны ломать JSON.
        const string message = "Fallback: ran=false (\"флаг\" снят штатной\nдоставкой — \"ok\")";
        var line = MenuCloseTraceFormat.BuildLine("log", Ts, 1,
            new Dictionary<string, object?> { ["message"] = message });

        using var doc = JsonDocument.Parse(line);
        Assert.Equal(message, doc.RootElement.GetProperty("data").GetProperty("message").GetString());
    }

    [Fact]
    public void BuildLine_NoData_OmitsDataField()
    {
        var line = MenuCloseTraceFormat.BuildLine("truncated", Ts, 0);

        Assert.DoesNotContain("data", line, StringComparison.Ordinal);
        using var doc = JsonDocument.Parse(line);
        Assert.False(doc.RootElement.TryGetProperty("data", out _));
    }

    // ============ Усечение: хвост последних записей + маркер truncated ============

    [Fact]
    public void TruncateToLimit_KeepsTailAndWritesMarkerFirst()
    {
        var lines = Enumerable.Range(1, 10)
            .Select(i => MenuCloseTraceFormat.BuildLine("log", Ts, i,
                new Dictionary<string, object?> { ["message"] = $"line-{i}" }))
            .ToArray();
        var content = string.Join("\n", lines) + "\n";

        // Лимит, в который помещается только маркер + пара последних строк.
        const int budget = 260;

        var result = MenuCloseTraceFormat.TruncateToLimit(content, budget, Ts);

        Assert.True(System.Text.Encoding.UTF8.GetByteCount(result) <= budget);
        var resultLines = result.TrimEnd('\n').Split('\n');

        // Первая строка — маркер усечения (валидный JSON, event == "truncated");
        // остальные строки хвоста — тоже валидный JSON.
        using (var marker = JsonDocument.Parse(resultLines[0]))
        {
            Assert.Equal("truncated", marker.RootElement.GetProperty("event").GetString());
        }
        foreach (var line in resultLines.Skip(1))
        {
            using var _ = JsonDocument.Parse(line);
        }

        // Хвост сохраняет ПОСЛЕДНИЕ записи исходного содержимого
        // (строка JSON, внутри неё — сообщение последней записи).
        Assert.Contains("line-10", resultLines[^1], StringComparison.Ordinal);
    }

    [Fact]
    public void TruncateToLimit_SmallLimit_ReturnsOnlyMarker()
    {
        // Содержимое заметно больше лимита (строки — полные JSONL-записи),
        // лимит лишь чуть больше одного маркера — хвост не помещается.
        var content = string.Join("\n",
                Enumerable.Range(1, 5).Select(i => MenuCloseTraceFormat.BuildLine(
                    "log", Ts, i, new Dictionary<string, object?> { ["message"] = $"line-{i}" })))
            + "\n";
        var markerLen = MenuCloseTraceFormat.BuildLine("truncated", Ts, 0).Length;
        Assert.True(System.Text.Encoding.UTF8.GetByteCount(content) > markerLen + 2);

        var result = MenuCloseTraceFormat.TruncateToLimit(content, markerLen + 2, Ts);

        var lines = result.TrimEnd('\n').Split('\n');
        Assert.Single(lines);
        using (var marker = JsonDocument.Parse(lines[0]))
        {
            Assert.Equal("truncated", marker.RootElement.GetProperty("event").GetString());
        }
    }

    [Fact]
    public void TruncateToLimit_UnderLimit_Unchanged()
    {
        var content = "line-1\nline-2\n";
        var result = MenuCloseTraceFormat.TruncateToLimit(content, 10_000, Ts);

        Assert.Equal(content, result);
    }

    // ============ Startup-запись: версия, платформа, ОС ============

    [Fact]
    public void BuildStartupLine_ContainsVersionPlatformAndOs()
    {
        var line = MenuCloseTraceFormat.BuildStartupLine(
            "0.3.9.306",
            "WPF",
            "Microsoft Windows 11 Pro",
            @"C:\Users\user\AppData\Roaming\ConfigurationManagement",
            Ts,
            threadId: 1);

        using var doc = JsonDocument.Parse(line);
        Assert.Equal("startup", doc.RootElement.GetProperty("event").GetString());
        var data = doc.RootElement.GetProperty("data");
        Assert.Equal("0.3.9.306", data.GetProperty("version").GetString());
        Assert.Equal("WPF", data.GetProperty("platform").GetString());
        Assert.Contains("Windows", data.GetProperty("os").GetString(), StringComparison.Ordinal);
        Assert.EndsWith("ConfigurationManagement", data.GetProperty("appDataDir").GetString(), StringComparison.Ordinal);
    }

    // ============ Секреты: пароли/токены не выводятся ============

    [Fact]
    public void MaskSensitive_HidesUsernamePasswordAndTokens()
    {
        var source = new Dictionary<string, object?>
        {
            ["username"] = "admin",
            ["password"] = "s3cr3t",
            ["token"] = "abc123",
            ["login"] = "admin",
            ["user"] = "admin",
            ["authorization"] = "Bearer xyz",
            ["message"] = "EnsureStable: target=abc, done=true",
            ["selectedItemId"] = "b1"
        };

        var masked = MenuCloseTraceFormat.MaskSensitive(source);

        Assert.Equal("***", masked["username"]);
        Assert.Equal("***", masked["password"]);
        Assert.Equal("***", masked["token"]);
        Assert.Equal("***", masked["login"]);
        Assert.Equal("***", masked["user"]);
        Assert.Equal("***", masked["authorization"]);
        // Диагностические поля не затрагиваются.
        Assert.Equal(source["message"], masked["message"]);
        Assert.Equal(source["selectedItemId"], masked["selectedItemId"]);
    }

    [Fact]
    public void MaskSensitive_DoesNotTouchDiagnosticUserKeys()
    {
        // Короткий маркер "user" маскируется только как ЦЕЛОЕ имя ключа:
        // "userReselected" — диагностическое поле и должно сохраниться.
        var source = new Dictionary<string, object?>
        {
            ["userReselected"] = "true",
            ["message"] = "user reselected row"
        };

        var masked = MenuCloseTraceFormat.MaskSensitive(source);

        Assert.Equal("true", masked["userReselected"]);
        Assert.Equal("user reselected row", masked["message"]);
    }

    [Fact]
    public void BuildLine_AfterMasking_DoesNotContainSecretValues()
    {
        var data = MenuCloseTraceFormat.MaskSensitive(new Dictionary<string, object?>
        {
            ["username"] = "ivanov",
            ["password"] = "P@ss!",
            ["token"] = "tok-12345"
        });
        var line = MenuCloseTraceFormat.BuildLine("log", Ts, 1, data);

        Assert.DoesNotContain("ivanov", line, StringComparison.Ordinal);
        Assert.DoesNotContain("P@ss!", line, StringComparison.Ordinal);
        Assert.DoesNotContain("tok-12345", line, StringComparison.Ordinal);

        // Запись остаётся валидным JSON с маской на месте значений.
        using var doc = JsonDocument.Parse(line);
        var maskedData = doc.RootElement.GetProperty("data");
        Assert.Equal("***", maskedData.GetProperty("username").GetString());
        Assert.Equal("***", maskedData.GetProperty("password").GetString());
        Assert.Equal("***", maskedData.GetProperty("token").GetString());
    }

    // ============ Выбор имени файла: trace.json / legacy menuclose_trace.json (0.3.9.308) ============

    [Fact]
    public void PrimaryFileName_MatchesUserConvention_TraceJson()
    {
        // Соглашение с пользователем (issue #340, последний комментарий 7OH): основной
        // файл диагностики называется trace.json, а не menuclose_trace.json (0.3.9.306).
        Assert.Equal("trace.json", MenuCloseTraceFormat.PrimaryFileName);
        Assert.Equal("trace.json", MenuCloseTrace.FileName);
    }

    [Fact]
    public void LegacyFileName_IsMenuCloseTraceJson()
    {
        Assert.Equal("menuclose_trace.json", MenuCloseTraceFormat.LegacyFileName);
        Assert.Equal("menuclose_trace.json", MenuCloseTrace.LegacyFileName);
    }

    [Fact]
    public void ResolveFileName_NoLegacyFile_UsesPrimaryTraceJson()
    {
        // Нет legacy-файла от 0.3.9.306 — журнал пишется в основной trace.json.
        Assert.Equal("trace.json", MenuCloseTraceFormat.ResolveFileName(legacyExists: false));
    }

    [Fact]
    public void ResolveFileName_LegacyExists_UsesLegacyMenuCloseTraceJson()
    {
        // Legacy-файл от 0.3.9.306 существует — дописываем в него (непрерывность
        // диагностики пользователя), а не создаём рядом второй файл.
        Assert.Equal("menuclose_trace.json", MenuCloseTraceFormat.ResolveFileName(legacyExists: true));
    }
}