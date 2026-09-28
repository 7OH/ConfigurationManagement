using Configuration_Management.Models;
using Configuration_Management.Services;
using Xunit;

namespace ConfigurationManagement.Tests;

/// <summary>
/// Тесты флажка «Запретить локальное распознавание речи» (issue #307):
/// проброс из модели запроса в строку CREATEINFOBASE (документированный
/// параметр подключения <c>disstt="Y"</c>) и обратный разбор из строки
/// подключения в <see cref="ConnectionSettings"/>. Без реальной платформы 1С.
/// </summary>
public sealed class ForbidSpeechRecognitionTests
{
    // ======================= BuildClientServerCreateConnectionString =======================

    [Fact]
    public void BuildConnectionString_WithForbidSpeechRecognition_AddsDisstt()
    {
        var cs = OneCLauncher.BuildClientServerCreateConnectionString(
            "srv", "base", forbidSpeechRecognition: true);

        Assert.Contains(";disstt=\"Y\"", cs);
        Assert.Contains("Srvr=\"srv\";Ref=\"base\"", cs);
    }

    [Fact]
    public void BuildConnectionString_WithoutForbidSpeechRecognition_NoDisstt()
    {
        var cs = OneCLauncher.BuildClientServerCreateConnectionString("srv", "base");

        Assert.DoesNotContain("disstt", cs);
    }

    [Fact]
    public void BuildConnectionString_CombinesSchJobDnAndDisstt()
    {
        var cs = OneCLauncher.BuildClientServerCreateConnectionString(
            "srv", "base",
            dbms: "PostgreSQL",
            createSqlDatabase: true,
            blockScheduledJobs: true,
            forbidSpeechRecognition: true);

        Assert.Contains(";CrSQLDB=\"Y\"", cs);
        Assert.Contains(";SchJobDn=\"Y\"", cs);
        Assert.Contains(";disstt=\"Y\"", cs);
        Assert.EndsWith(";disstt=\"Y\"", cs);
    }

    // ======================= ParseConnectionString =======================

    [Fact]
    public void ParseConnectionString_WithDissttQuoted_SetsForbidSpeechRecognition()
    {
        var settings = ConnectionSettings.ParseConnectionString(
            "Srvr=\"srv\";Ref=\"base\";disstt=\"Y\"");

        Assert.True(settings.ForbidSpeechRecognition);
    }

    [Fact]
    public void ParseConnectionString_WithDissttUnquoted_SetsForbidSpeechRecognition()
    {
        var settings = ConnectionSettings.ParseConnectionString(
            "Srvr=\"srv\";Ref=\"base\";disstt=Y");

        Assert.True(settings.ForbidSpeechRecognition);
    }

    [Fact]
    public void ParseConnectionString_WithoutDisstt_DefaultsToFalse()
    {
        var settings = ConnectionSettings.ParseConnectionString("Srvr=\"srv\";Ref=\"base\"");

        Assert.False(settings.ForbidSpeechRecognition);
    }

    [Fact]
    public void ParseConnectionString_DissttN_MeansAllowed()
    {
        var settings = ConnectionSettings.ParseConnectionString(
            "Srvr=\"srv\";Ref=\"base\";disstt=\"N\"");

        Assert.False(settings.ForbidSpeechRecognition);
    }

    // ======================= Модель запроса =======================

    [Fact]
    public void CreateInfobaseRequest_ForbidSpeechRecognition_DefaultsToFalse()
    {
        var request = new CreateInfobaseRequest();

        Assert.False(request.ForbidSpeechRecognition);
    }

    [Fact]
    public void CreateInfobaseRequest_ForbidSpeechRecognition_RoundTrips()
    {
        var request = new CreateInfobaseRequest { ForbidSpeechRecognition = true };

        Assert.True(request.ForbidSpeechRecognition);
    }
}