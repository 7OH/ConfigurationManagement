using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Configuration_Management.ViewModels;

/// <summary>
/// Строка окна выбора баз (выборочный экспорт/импорт): подпись, пояснение
/// и флажок. Чистая модель без платформенных зависимостей — подключается
/// в Linux-сборку явно (не совпадает с глобом *.Avalonia.cs).
/// </summary>
public sealed class BaseSelectionItem : INotifyPropertyChanged
{
    private bool _isChecked;

    public BaseSelectionItem(string title, string subtitle = "", bool isChecked = true)
    {
        Title = title;
        Subtitle = subtitle;
        _isChecked = isChecked;
    }

    /// <summary>Подпись строки (имя базы).</summary>
    public string Title { get; }

    /// <summary>Пояснение (группа, подключение или причина пропуска).</summary>
    public string Subtitle { get; }

    /// <summary>Связанный объект (например, экземпляр Infobase).</summary>
    public object? Tag { get; init; }

    /// <summary>Отмечена ли строка.</summary>
    public bool IsChecked
    {
        get => _isChecked;
        set
        {
            if (_isChecked != value)
            {
                _isChecked = value;
                OnPropertyChanged();
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName!));
}
