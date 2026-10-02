using Configuration_Management.Models;
using Configuration_Management.ViewModels;
using Xunit;

namespace ConfigurationManagement.Tests;

/// <summary>
/// Тесты строки выпадающего списка «Конфигурация» окна «Связать с конфигурацией»
/// (issue #322): пометки происхождения (типовая ★ / пользовательская / пользовательская
/// копия ✎★, перекрывающая типовую) и безопасное ToString без расширения суффиксами.
/// </summary>
public sealed class ConfigLinkItemViewModelTests
{
    private static ConfigLinkItemViewModel Create(
        string code = "X",
        string name = "Конфигурация",
        bool isBuiltIn = false,
        bool overrides = false,
        string urlCode = "") =>
        new(new OneCConfigType
        {
            Code = code,
            Name = name,
            UrlCode = urlCode,
            IsBuiltIn = isBuiltIn,
            OverridesBuiltIn = overrides,
        });

    [Fact]
    public void StandardEntry_HasStarBadge()
    {
        var item = Create(code: "ZUP", name: "Зарплата и управление персоналом", isBuiltIn: true);

        Assert.True(item.IsBuiltIn);
        Assert.False(item.IsOverride);
        Assert.Contains("★", item.OriginBadge);
    }

    [Fact]
    public void CustomEntry_HasNoStarBadge()
    {
        var item = Create(code: "MY", name: "Моя конфигурация");

        Assert.False(item.IsBuiltIn);
        Assert.False(item.IsOverride);
        Assert.DoesNotContain("★", item.OriginBadge);
        Assert.NotEmpty(item.OriginBadge);
    }

    [Fact]
    public void OverrideCopy_HasPencilStarBadge()
    {
        // Пользовательская копия предопределённой (правка встроенной, issue #321):
        // метка должна отличаться и от типовой, и от обычной пользовательской.
        var item = Create(code: "ZUP", name: "ЗУП (копия)", overrides: true);

        Assert.False(item.IsBuiltIn);
        Assert.True(item.IsOverride);
        Assert.Contains("✎", item.OriginBadge);
    }

    [Fact]
    public void ToString_ReturnsNameWithoutSuffixes()
    {
        var item = Create(code: "ZUP", name: "Зарплата и управление персоналом", isBuiltIn: true);

        // ToString не расширяется пометками (его используют и другие окна) —
        // метка живёт только в OriginBadge.
        Assert.Equal("Зарплата и управление персоналом", item.ToString());
    }

    [Fact]
    public void ToolTipText_ContainsNameCodeAndUrlSegment()
    {
        var item = Create(code: "MY", name: "Моя ЗУП", urlCode: "ЗарплатаИУправлениеПерсоналом");

        Assert.Contains("Моя ЗУП", item.ToolTipText);
        Assert.Contains("MY", item.ToolTipText);
        Assert.Contains("ЗарплатаИУправлениеПерсоналом", item.ToolTipText);
    }

    [Fact]
    public void Model_IsExposed()
    {
        var model = new OneCConfigType { Code = "MY", Name = "Моя конфигурация" };
        var item = new ConfigLinkItemViewModel(model);

        Assert.Same(model, item.Model);
    }
}