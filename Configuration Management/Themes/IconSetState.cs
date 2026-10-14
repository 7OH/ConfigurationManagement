using System.ComponentModel;

namespace Configuration_Management.Themes;

/// <summary>
/// Наблюдаемое состояние активного набора иконок (0.3.12.60). Менеджер тем обеих
/// платформ обновляет <see cref="Current"/> при каждом применении темы, а привязки
/// (шаблон materialDesign:PackIcon, конвертер ключей иконок в WPF) подписаны на
/// <see cref="INotifyPropertyChanged.PropertyChanged"/> и перерисовывают значки сразу,
/// без перезапуска приложения и пересоздания окон.
/// </summary>
public sealed class IconSetState : INotifyPropertyChanged
{
    /// <summary>Единственный экземпляр (источник привязок x:Static).</summary>
    public static IconSetState Instance { get; } = new();

    private IconSet _current = IconSet.Fluent;

    private IconSetState()
    {
    }

    /// <summary>Активный набор иконок.</summary>
    public IconSet Current
    {
        get => _current;
        set
        {
            if (_current == value)
                return;
            _current = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Current)));
        }
    }

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;
}
