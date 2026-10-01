using Configuration_Management.Services;
using Xunit;

namespace ConfigurationManagement.Tests;

/// <summary>
/// Тесты условия «Enter = двойной клик» в списке баз (issue #328):
/// Enter запускает базу/сворачивает группу ТОЛЬКО когда фокус не в текстовом
/// вводе (инлайн-редактор тега, поиск, командная палитра) и не открыт модальный
/// диалог — иначе Enter работает как обычно.
/// </summary>
public sealed class EnterActivationHelperTests
{
    [Fact]
    public void CanHandleEnter_NoTextInputNoDialog_Allows()
    {
        Assert.True(EnterActivationHelper.CanHandleEnter(
            focusInTextInput: false, hasOpenModalDialog: false));
    }

    [Theory]
    [InlineData(true, false)]  // фокус в поле поиска / инлайн-редакторе тега
    [InlineData(false, true)]  // открыт модальный диалог (свойства базы, настройки)
    [InlineData(true, true)]   // оба условия сразу
    public void CanHandleEnter_TextInputOrDialog_Blocks(bool focusInTextInput, bool hasOpenModalDialog)
    {
        Assert.False(EnterActivationHelper.CanHandleEnter(focusInTextInput, hasOpenModalDialog));
    }
}