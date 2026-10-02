namespace Configuration_Management.Models;

/// <summary>Режим списка на главном окне: все базы / избранное / недавние / запущенные.</summary>
public enum ListViewMode
{
    All,
    Favorites,
    Recent,
    /// <summary>Показывать только запущенные базы (issue #339).</summary>
    Running
}
