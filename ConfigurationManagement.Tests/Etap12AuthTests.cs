using Configuration_Management.Models;
using Configuration_Management.Services;
using Xunit;

namespace ConfigurationManagement.Tests;

/// <summary>
/// Тесты Этапа 12 дорожной карты StartManager (функция №7): раздельные учётные данные
/// для режимов «Конфигуратор», «1С:Предприятие» и Хранилища конфигурации.
/// Единый выбор учётных данных выполняется <see cref="InfobaseAuthResolver"/>.
/// </summary>
public sealed class Etap12AuthTests
{
    private static Infobase CreateInfobase(
        string connUser, string connPass, AuthenticationMode connMode,
        string entUser, string entPass,
        string cfgUser, string cfgPass,
        string repoUser, string repoPass)
    {
        return new Infobase
        {
            Connection = new ConnectionSettings
            {
                User = connUser,
                Password = connPass,
                AuthenticationMode = connMode
            },
            EnterpriseAuth = new InfobaseAuthSettings
            {
                AuthenticationMode = AuthenticationMode.Credentials,
                User = entUser,
                Password = entPass
            },
            ConfiguratorAuth = new InfobaseAuthSettings
            {
                AuthenticationMode = AuthenticationMode.Credentials,
                User = cfgUser,
                Password = cfgPass
            },
            Repository = new RepositorySettings
            {
                Server = "tcp://server:1542",
                RepositoryName = "Repo",
                User = repoUser,
                Password = repoPass
            }
        };
    }

    [Fact]
    public void Resolve_EnterpriseMode_UsesEnterpriseAuth()
    {
        var ib = CreateInfobase("connU", "connP", AuthenticationMode.Credentials,
            "entU", "entP", "cfgU", "cfgP", "repoU", "repoP");

        InfobaseAuthResolver.Resolve(ib, OneCLaunchMode.Enterprise,
            out var mode, out var user, out var pass);

        Assert.Equal(AuthenticationMode.Credentials, mode);
        Assert.Equal("entU", user);
        Assert.Equal("entP", pass);
    }

    [Fact]
    public void Resolve_ConfiguratorMode_UsesConfiguratorAuth()
    {
        var ib = CreateInfobase("connU", "connP", AuthenticationMode.Credentials,
            "entU", "entP", "cfgU", "cfgP", "repoU", "repoP");

        InfobaseAuthResolver.Resolve(ib, OneCLaunchMode.Configurator,
            out var mode, out var user, out var pass);

        Assert.Equal(AuthenticationMode.Credentials, mode);
        Assert.Equal("cfgU", user);
        Assert.Equal("cfgP", pass);
    }

    [Fact]
    public void Resolve_MissingEnterpriseAuth_FallsBackToConnection()
    {
        var ib = new Infobase
        {
            Connection = new ConnectionSettings
            {
                User = "connU",
                Password = "connP",
                AuthenticationMode = AuthenticationMode.Credentials
            }
            // EnterpriseAuth / ConfiguratorAuth == null -> fallback to Connection.
        };

        InfobaseAuthResolver.Resolve(ib, OneCLaunchMode.Enterprise,
            out var mode, out var user, out var pass);

        Assert.Equal(AuthenticationMode.Credentials, mode);
        Assert.Equal("connU", user);
        Assert.Equal("connP", pass);
    }

    [Fact]
    public void Resolve_MissingConfiguratorAuth_FallsBackToConnection()
    {
        var ib = new Infobase
        {
            Connection = new ConnectionSettings
            {
                User = "connU",
                Password = "connP",
                AuthenticationMode = AuthenticationMode.Windows
            }
        };

        InfobaseAuthResolver.Resolve(ib, OneCLaunchMode.Configurator,
            out var mode, out var user, out var pass);

        Assert.Equal(AuthenticationMode.Windows, mode);
        Assert.Equal("connU", user);
        Assert.Equal("connP", pass);
    }

    [Fact]
    public void Resolve_NoConnectionOrDefault_DefaultsToPrompt()
    {
        var ib = new Infobase(); // пустые Connection и авторизации.

        InfobaseAuthResolver.Resolve(ib, OneCLaunchMode.Enterprise,
            out var mode, out var user, out var pass);

        Assert.Equal(AuthenticationMode.Prompt, mode);
        Assert.Equal(string.Empty, user);
        Assert.Equal(string.Empty, pass);
    }

    [Fact]
    public void ResolveRepository_ReturnsSeparateRepositoryCredentials()
    {
        var ib = CreateInfobase("connU", "connP", AuthenticationMode.Credentials,
            "entU", "entP", "cfgU", "cfgP", "repoU", "repoP");

        InfobaseAuthResolver.ResolveRepository(ib, out var user, out var pass);

        // Логин/пароль хранилища — отдельный набор, независимый от Предприятия/Конфигуратора.
        Assert.Equal("repoU", user);
        Assert.Equal("repoP", pass);
    }

    [Fact]
    public void ResolveRepository_EmptyRepository_ReturnsEmptyCredentials()
    {
        var ib = new Infobase { Repository = new RepositorySettings() };

        InfobaseAuthResolver.ResolveRepository(ib, out var user, out var pass);

        Assert.Equal(string.Empty, user);
        Assert.Equal(string.Empty, pass);
    }

    [Fact]
    public void AllThreeSets_AreIndependentAndSeparate()
    {
        // Три набора должны различаться между собой: это суть функции №7 StartManager.
        var ib = CreateInfobase("connU", "connP", AuthenticationMode.Credentials,
            "entU", "entP", "cfgU", "cfgP", "repoU", "repoP");

        InfobaseAuthResolver.Resolve(ib, OneCLaunchMode.Enterprise,
            out _, out var entUser, out _);
        InfobaseAuthResolver.Resolve(ib, OneCLaunchMode.Configurator,
            out _, out var cfgUser, out _);
        InfobaseAuthResolver.ResolveRepository(ib, out var repoUser, out _);

        Assert.Equal("entU", entUser);
        Assert.Equal("cfgU", cfgUser);
        Assert.Equal("repoU", repoUser);
        Assert.NotEqual(entUser, cfgUser);
        Assert.NotEqual(entUser, repoUser);
        Assert.NotEqual(cfgUser, repoUser);
    }
}