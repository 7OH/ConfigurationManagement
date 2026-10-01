namespace Configuration_Management.Services;

/// <summary>
/// Чистая логика условия «Enter = двойной клик» в списке баз (issue #328):
/// Enter на строке запускает базу (или сворачивает/разворачивает группу) ТОЛЬКО
/// когда это безопасно. Если фокус в текстовом вводе (инлайн-редактор тега,
/// поле поиска, командная палитра) или открыт модальный диалог — Enter работает
/// как обычно и в навигацию не перехватывается.
/// </summary>
public static class EnterActivationHelper
{
    /// <summary>
    /// Можно ли обрабатывать Enter в дереве баз как «двойной клик».
    /// </summary>
    /// <param name="focusInTextInput">
    /// true, если клавиатурный фокус в текстовом поле/редакторе (TextBox,
    /// редактируемый ComboBox инлайн-редактора тега и т.п.).
    /// </param>
    /// <param name="hasOpenModalDialog">true, если открыт модальный диалог или иное видимое окно.</param>
    public static bool CanHandleEnter(bool focusInTextInput, bool hasOpenModalDialog)
        => !focusInTextInput && !hasOpenModalDialog;
}