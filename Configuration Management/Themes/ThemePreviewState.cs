namespace Configuration_Management.Themes;

/// <summary>
/// Чистое состояние режима «предпросмотр → Применить/Отмена» выбора полноценной темы
/// (issue #378). Не требует Application.Current: хранит тему до входа в галерею,
/// тему в предпросмотре и последний сохранённый FullTheme; методы возвращают, какую
/// тему применить/сохранить. <see cref="MainViewModel"/> обеих платформ использует
/// этот класс, чтобы выбор темы не применялся и не сохранялся сразу.
/// </summary>
public sealed class ThemePreviewState
{
    private string _savedFullThemeId = string.Empty;
    private string _themeIdBeforePreview = string.Empty;
    private string? _previewedThemeId;

    /// <summary>Последний сохранённый идентификатор полноценной темы (AppSettings.FullTheme).</summary>
    public string SavedFullThemeId => _savedFullThemeId;

    /// <summary>Тема до входа в галерею (возврат по «Отмена»).</summary>
    public string ThemeIdBeforePreview => _themeIdBeforePreview;

    /// <summary>Тема в предпросмотре (не сохранена); null — предпросмотра нет.</summary>
    public string? PreviewedThemeId => _previewedThemeId;

    /// <summary>true, если есть тема в предпросмотре (не закоммичена и не отменена).</summary>
    public bool HasPreview => _previewedThemeId is not null;

    /// <summary>
    /// Начинает режим предпросмотра: запоминает тему до входа в галерею и последний
    /// сохранённый FullTheme. Сбрасывает текущий предпросмотр.
    /// </summary>
    public void Begin(string currentThemeId, string savedFullThemeId)
    {
        _themeIdBeforePreview = Normalize(currentThemeId);
        _savedFullThemeId = string.IsNullOrWhiteSpace(savedFullThemeId)
            ? _themeIdBeforePreview
            : savedFullThemeId.Trim();
        _previewedThemeId = null;
    }

    /// <summary>
    /// Переводит тему в предпросмотр (не сохраняется). Возвращает идентификатор темы,
    /// который вызывающая сторона должна применить (persist:false).
    /// </summary>
    public string Preview(string id)
    {
        var resolved = Normalize(id);
        _previewedThemeId = resolved;
        return resolved;
    }

    /// <summary>
    /// Отменяет предпросмотр: возвращает идентификатор темы до входа в галерею
    /// (для применения без сохранения). null — предпросмотра не было: «Отмена»
    /// ничего не меняет (issue #381).
    /// </summary>
    public string? Revert()
    {
        if (_previewedThemeId is null)
            return null;
        var target = _themeIdBeforePreview;
        _previewedThemeId = null;
        return string.IsNullOrEmpty(target) ? null : target;
    }

    /// <summary>
    /// Подтверждает предпросмотр: возвращает идентификатор темы для сохранения
    /// (обновляет <see cref="SavedFullThemeId"/>). null — предпросмотра не было.
    /// </summary>
    public string? Commit()
    {
        var id = _previewedThemeId;
        if (id is not null)
            _savedFullThemeId = id;
        _previewedThemeId = null;
        return id;
    }

    private static string Normalize(string? id)
        => string.IsNullOrWhiteSpace(id) ? "light" : id.Trim();
}