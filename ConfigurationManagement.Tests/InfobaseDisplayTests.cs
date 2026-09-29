using System;
using Configuration_Management.Models;
using Xunit;

namespace ConfigurationManagement.Tests;

/// <summary>
/// Тесты отображаемой колонки «Сервер/База» (<see cref="Infobase.ServerDatabaseDisplay"/>).
/// Покрывают issue #319: для баз вида «Веб-сервер» должен показываться URL публикации
/// (<see cref="ConnectionSettings.WebUrl"/>), а не пустота/имя базы. Файловые и
/// клиент-серверные базы отображаются как раньше.
/// </summary>
public sealed class InfobaseDisplayTests
{
    [Fact]
    public void ServerDatabaseDisplay_WebServerWithWebUrl_ReturnsWebUrl()
    {
        const string url = "http://host/name";

        var ib = new Infobase
        {
            Name = "Веб-база",
            Connection = new ConnectionSettings
            {
                Type = ConnectionType.WebServer,
                WebUrl = url,
                DatabaseName = "ignored"
            }
        };

        Assert.Equal(url, ib.ServerDatabaseDisplay);
    }

    [Fact]
    public void ServerDatabaseDisplay_WebServerWithoutWebUrl_ReturnsDatabaseName()
    {
        var ib = new Infobase
        {
            Name = "Веб-база",
            Connection = new ConnectionSettings
            {
                Type = ConnectionType.WebServer,
                WebUrl = "   ",
                DatabaseName = "web"
            }
        };

        Assert.Equal("web", ib.ServerDatabaseDisplay);
    }

    [Fact]
    public void ServerDatabaseDisplay_WebServerWithoutWebUrlAndDatabaseName_ReturnsDash()
    {
        var ib = new Infobase
        {
            Name = "Веб-база",
            Connection = new ConnectionSettings { Type = ConnectionType.WebServer }
        };

        Assert.Equal("—", ib.ServerDatabaseDisplay);
    }

    [Fact]
    public void ServerDatabaseDisplay_File_ReturnsPath()
    {
        var ib = new Infobase
        {
            Name = "Файловая",
            Connection = new ConnectionSettings
            {
                Type = ConnectionType.File,
                FilePath = @"C:\bases\demo"
            }
        };

        Assert.Equal(@"C:\bases\demo", ib.ServerDatabaseDisplay);
    }

    [Fact]
    public void ServerDatabaseDisplay_FileWithoutPath_ReturnsDash()
    {
        var ib = new Infobase
        {
            Connection = new ConnectionSettings { Type = ConnectionType.File }
        };

        Assert.Equal("—", ib.ServerDatabaseDisplay);
    }

    [Fact]
    public void ServerDatabaseDisplay_ClientServer_ReturnsServerAndDatabase()
    {
        var ib = new Infobase
        {
            Name = "Серверная",
            Connection = new ConnectionSettings
            {
                Type = ConnectionType.ClientServer,
                Server = "srv-1c",
                DatabaseName = "Accounting"
            }
        };

        Assert.Equal(@"srv-1c\Accounting", ib.ServerDatabaseDisplay);
    }

    [Fact]
    public void ServerDatabaseDisplay_ClientServerWithoutServer_ReturnsDatabaseName()
    {
        var ib = new Infobase
        {
            Name = "Серверная",
            Connection = new ConnectionSettings
            {
                Type = ConnectionType.ClientServer,
                DatabaseName = "ZUP"
            }
        };

        Assert.Equal("ZUP", ib.ServerDatabaseDisplay);
    }
}