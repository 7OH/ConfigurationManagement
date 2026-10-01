using Configuration_Management.Services;
using Xunit;

namespace ConfigurationManagement.Tests;

/// <summary>
/// Тесты сохранения семейства шрифта по введённому тексту (issue #329):
/// произвольное имя из редактируемого поля сохраняется как есть, пустой
/// текст заменяется шрифтом по умолчанию.
/// </summary>
public sealed class FontFamilyResolverTests
{
    [Fact]
    public void Resolve_TypedName_ReturnsItAsIs()
    {
        // Введённое вручную имя системного шрифта, которого может не быть в списке,
        // не должно сбрасываться на шрифт по умолчанию.
        Assert.Equal("IBM Plex Sans", FontFamilyResolver.Resolve("IBM Plex Sans", "Segoe UI"));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("   ")]
    public void Resolve_EmptyOrWhitespace_ReturnsFallback(string? text)
    {
        Assert.Equal("Segoe UI", FontFamilyResolver.Resolve(text, "Segoe UI"));
    }

    [Fact]
    public void Resolve_SurroundingSpaces_AreTrimmed()
    {
        Assert.Equal("Consolas", FontFamilyResolver.Resolve("  Consolas  ", "Segoe UI"));
    }

    [Fact]
    public void Resolve_SelectedKnownFont_BehavesLikeTyped()
    {
        // Выбранный из списка элемент тоже попадает в Text редактируемого поля.
        Assert.Equal("Arial", FontFamilyResolver.Resolve("Arial", "Segoe UI"));
    }
}