using System.ComponentModel;
using System.Runtime.CompilerServices;
using Configuration_Management.Models;

namespace Configuration_Management.ViewModels;

/// <summary>
/// Строка чеклиста окна «Импорт из кластера 1С» (0.3.9.173, цикл 0.3.9.172–0.3.9.175):
/// информационная база кластера с флажком импорта. Чистая модель без платформенных
/// зависимостей — обе платформы (WPF и Avalonia); UI только привязывается.
/// Дубликаты уже существующих в списке баз и файловые базы кластера приходят со
/// снятым флажком и причиной пропуска; повторный чек не приводит к импорту
/// (фильтр выполняется на этапе «Импортировать», см. <see cref="ClusterImportViewModel"/>).
/// </summary>
public sealed class ClusterImportRow : INotifyPropertyChanged
{
    private bool _isChecked;

    public ClusterImportRow(
        string name,
        string subtitle,
        string clusterName,
        string connectionString,
        bool isChecked,
        bool isDuplicate,
        string? skipReason,
        Infobase tag)
    {
        Name = name;
        Subtitle = subtitle;
        ClusterName = clusterName;
        ConnectionString = connectionString;
        IsDuplicate = isDuplicate;
        SkipReason = skipReason;
        Tag = tag;
        _isChecked = isChecked;
    }

    /// <summary>Имя информационной базы (совпадает с <c>Ref</c> строки подключения).</summary>
    public string Name { get; }

    /// <summary>Подпись строки: описание / тип СУБД и сервер / имя БД в СУБД.</summary>
    public string Subtitle { get; }

    /// <summary>Имя кластера, из которого импортируется база (колонка «Кластер»).</summary>
    public string ClusterName { get; }

    /// <summary>Строка подключения базы 1С (<c>Srvr="host[:port]";Ref="name"</c>) — колонка «Подключение».</summary>
    public string ConnectionString { get; }

    /// <summary>
    /// Признак дубликата: база уже присутствует в списке приложения (совпадение по
    /// строке подключения, <see cref="RacInfobaseMapper.IsDuplicate"/>).
    /// </summary>
    public bool IsDuplicate { get; }

    /// <summary>
    /// Причина, по которой база не импортируется: «уже есть в списке»
    /// (ключ <c>ClusterImport.AlreadyExists</c>) или «файловая база кластера»
    /// (ключ <c>ClusterImport.SkipFileBase</c>); null — базу можно импортировать.
    /// </summary>
    public string? SkipReason { get; }

    /// <summary>Смапленная база списка приложения (результат импорта для MainViewModel).</summary>
    public Infobase Tag { get; }

    /// <summary>Отмечена ли строка для импорта.</summary>
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