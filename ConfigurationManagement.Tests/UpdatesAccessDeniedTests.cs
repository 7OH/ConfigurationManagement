using Configuration_Management.Services;
using Xunit;

namespace ConfigurationManagement.Tests;

/// <summary>
/// Тесты распознавания страницы «У Вас нет доступа» портала releases.1c.ru (issue #371):
/// HTTP 200 с заглушкой для продуктов, не входящих в подписку ИТС учётной записи.
/// Логика — статический метод <see cref="OneCUpdatesService.LooksLikeAccessDeniedPage"/>,
/// покрыты русские/английские маркеры и отрицательные случаи (обычный каталог, страница
/// входа, личный кабинет, пустое/нерелевантное содержимое).
/// </summary>
public sealed class UpdatesAccessDeniedTests
{
    // ======================= русская разметка =======================

    [Theory]
    [InlineData("У Вас нет доступа к обновлениям")]
    [InlineData("у вас нет доступа к обновлениям")]
    [InlineData("у вас нет доступа")]
    [InlineData("Нет доступа к обновлениям")]
    [InlineData("Доступ к обновлениям запрещён")]
    [InlineData("Доступ ограничен")]
    [InlineData("Отказано в доступе")]
    [InlineData("Ваша подписка не включает обновления этой конфигурации")]
    [InlineData("Этот продукт не входит в вашу подписку")]
    public void RussianMarkers_Detected(string marker)
    {
        var html = $"<html><head><title>403 Forbidden</title></head>" +
                   $"<body><h1>Доступ к сайту обновлений</h1><p>{marker}</p></body></html>";

        Assert.True(OneCUpdatesService.LooksLikeAccessDeniedPage(html), marker);
    }

    // ======================= английская разметка =======================

    [Theory]
    [InlineData("You don't have access to updates")]
    [InlineData("You do not have access to this update")]
    [InlineData("Access is denied")]
    [InlineData("Access denied")]
    [InlineData("No access to updates")]
    [InlineData("Access is restricted")]
    [InlineData("Permission denied")]
    [InlineData("This product is not included in your subscription")]
    [InlineData("not covered by your subscription")]
    public void EnglishMarkers_Detected(string marker)
    {
        var html = $"<html><head><title>403</title></head>" +
                   $"<body><p>{marker}</p></body></html>";

        Assert.True(OneCUpdatesService.LooksLikeAccessDeniedPage(html), marker);
    }

    // ======================= реалистичный фрагмент страницы =======================

    [Fact]
    public void RealisticAccessDeniedPage_Detected()
    {
        var html = """
            <html><head><title>У Вас нет доступа</title></head>
            <body>
              <div class="error">У Вас нет доступа к обновлениям данной конфигурации.
              Убедитесь, что вы подключены к сервису ИТС.</div>
            </body></html>
            """;

        Assert.True(OneCUpdatesService.LooksLikeAccessDeniedPage(html));
    }

    // ======================= отрицательные случаи =======================

    [Fact]
    public void RegularCatalogTable_NotDetected()
    {
        // Обычный каталог: таблица версий, «Доступ» упоминается только в нейтральном
        // контексте (колонка лицензий), специфичных фраз отказа нет.
        var html = """
            <html><body>
              <h1>Управление торговлей, редакция 11</h1>
              <table id="versionsTable">
                <tr><td><a href="/version_file?nick=Trade110&ver=11.5.26.118">11.5.26.118</a></td>
                    <td>Доступ к обновлениям предоставлен</td></tr>
                <tr><td><a href="/version_file?nick=Trade110&ver=11.5.26.116">11.5.26.116</a></td>
                    <td>Релиз</td></tr>
              </table>
            </body></html>
            """;

        Assert.False(OneCUpdatesService.LooksLikeAccessDeniedPage(html));
    }

    [Fact]
    public void LoginForm_NotDetected()
    {
        // Страница входа CAS: поля username/password/execution, но без фраз отказа.
        var html = """
            <html><body>
              <form id="fm1" action="/login" method="post">
                <input name="username" type="text"/>
                <input name="password" type="password"/>
                <input name="execution" type="hidden" value="e1s2"/>
              </form>
            </body></html>
            """;

        Assert.False(OneCUpdatesService.LooksLikeAccessDeniedPage(html));
    }

    [Fact]
    public void PersonalAreaPage_NotDetected()
    {
        // Личный кабинет после успешного входа: слово «доступ» встречается в меню,
        // но специфичных фраз отказа нет.
        var html = """
            <html><head><title>Личный кабинет</title></head>
            <body><h1>Личные данные</h1><p>Доступ к сервисам: активен</p></body></html>
            """;

        Assert.False(OneCUpdatesService.LooksLikeAccessDeniedPage(html));
    }

    [Fact]
    public void VersionFilesPage_NotDetected()
    {
        // Страница файлов релиза: имена файлов (updsetup/updstpp) и обычный текст.
        var html = """
            <html><body>
              <h1>Trade 11.5.26.118</h1>
              <table>
                <tr><td><a href="/version_file?nick=Trade110&ver=11.5.26.118">Дистрибутив обновления</a></td></tr>
                <tr><td><a href="/version_file?nick=Trade110&ver=11.5.26.118">Полный дистрибутив</a></td></tr>
              </table>
            </body></html>
            """;

        Assert.False(OneCUpdatesService.LooksLikeAccessDeniedPage(html));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("<html><body>Мир труда</body></html>")]
    public void EmptyOrIrrelevantHtml_NotDetected(string html)
    {
        Assert.False(OneCUpdatesService.LooksLikeAccessDeniedPage(html));
    }

    [Fact]
    public void NullHtml_NotDetected()
    {
        Assert.False(OneCUpdatesService.LooksLikeAccessDeniedPage(null));
    }
}