using System.Collections.Generic;
using System.Collections.ObjectModel;
using Configuration_Management.Localization;
using Configuration_Management.Models;
using Configuration_Management.Services;

namespace Configuration_Management.ViewModels;

/// <summary>
/// Строка выпадающего списка учётных записей ИТС в настройках и редакторе типовой
/// конфигурации (issue #333): виртуальный пункт «Основная» (Id == null) + реальные
/// записи справочника. DisplayMemberPath — <see cref="Name"/>.
/// </summary>
public sealed class ItsAccountSelectionItem
{
    /// <summary>Идентификатор записи справочника; null — виртуальный пункт «Основная».</summary>
    public string? Id { get; }

    /// <summary>Отображаемое имя пункта.</summary>
    public string Name { get; }

    public ItsAccountSelectionItem(string? id, string name)
    {
        Id = id;
        Name = name;
    }
}

/// <summary>
/// Список учётных записей ИТС для окна справочника «Учетные данные ИТС»
/// (issue #333): загрузка/сохранение через <see cref="IItsAccountsStore"/> и строки
/// <see cref="ItsAccountItemViewModel"/> с командами «Добавить»/«Изменить»/«Удалить»/
/// «Задать основным». Окно остаётся тонким и лишь показывает коллекцию.
/// </summary>
public class ItsAccountsViewModel : ViewModelBase
{
    private readonly IItsAccountsStore _store;

    /// <summary>Строки списка учётных записей (пересобираются после каждого изменения).</summary>
    public ObservableCollection<ItsAccountItemViewModel> Rows { get; } = new();

    /// <param name="store">Хранилище учётных записей ИТС.</param>
    public ItsAccountsViewModel(IItsAccountsStore store)
    {
        _store = store;
        Reload();
    }

    /// <summary>Перечитывает список из хранилища и пересобирает строки.</summary>
    public void Reload()
    {
        Rows.Clear();
        foreach (var account in _store.Load())
            Rows.Add(new ItsAccountItemViewModel(account, OnEdit, OnDelete, OnSetPrimary));
    }

    /// <summary>Добавляет новую учётную запись (IsPrimary назначается хранилищем: первая запись
    /// пустого справочника становится основной).</summary>
    public void Add(ItsAccount account)
    {
        if (account is null)
            return;
        _store.Upsert(account);
        Reload();
    }

    /// <summary>Сохраняет правку существующей записи (флаг «Основная» не изменяется).</summary>
    public void Update(ItsAccount account)
    {
        if (account is null)
            return;
        _store.Upsert(account);
        Reload();
    }

    /// <summary>Удаляет запись; если удаляемая была основной — основной становится первая запись.</summary>
    public void Delete(string id) => _store.Delete(id);

    /// <summary>Делает запись основной (снимает флаг с остальных).</summary>
    public void SetPrimary(string id) => _store.SetPrimary(id);

    /// <summary>Показывает имя записи по идентификатору (для журналирования), пароль маскируется.</summary>
    public string DescribeForLog(string? id)
    {
        var account = _store.GetById(id ?? string.Empty);
        return account?.Name ?? LocalizationManager.T("ItsAccounts.Primary");
    }

    private void OnEdit(ItsAccountItemViewModel row) => EditRequested?.Invoke(row);

    private void OnDelete(ItsAccountItemViewModel row) => DeleteRequested?.Invoke(row);

    private void OnSetPrimary(ItsAccountItemViewModel row)
    {
        _store.SetPrimary(row.Id);
        Reload();
    }

    /// <summary>Запрос открытия редактора записи (обрабатывается окном).</summary>
    public event System.Action<ItsAccountItemViewModel>? EditRequested;

    /// <summary>Запрос удаления записи с подтверждением (обрабатывается окном).</summary>
    public event System.Action<ItsAccountItemViewModel>? DeleteRequested;
}

/// <summary>
/// Строит список пунктов выбора учётной записи (записи справочника; виртуальный пункт
/// «Основная» с Id == null — только если в справочнике нет реальной записи с таким именем)
/// для ComboBox в настройках и редакторе типовой конфигурации (issue #333/#322).
/// </summary>
public static class ItsAccountSelectionBuilder
{
    /// <summary>
    /// Список пунктов выбора. Дедупликация «Основной» (issue #333/#322): виртуальный пункт
    /// «Основная» (Id == null) добавляется первым ТОЛЬКО если в справочнике нет записи
    /// с именем <see cref="ItsAccountsStore.PrimaryName"/> (без учёта регистра); если таких
    /// записей несколько (ручная правка файла) — в списке остаётся первая из них.
    /// </summary>
    public static List<ItsAccountSelectionItem> Build(IItsAccountsStore store)
    {
        var result = new List<ItsAccountSelectionItem>();

        var primaryNameSeen = false;
        foreach (var account in store.Load())
        {
            var isPrimaryNamed = !string.IsNullOrWhiteSpace(account.Name) &&
                                 string.Equals(account.Name!.Trim(), ItsAccountsStore.PrimaryName,
                                     System.StringComparison.OrdinalIgnoreCase);
            if (isPrimaryNamed)
            {
                // Защита от дублей реальных записей «Основная» в файле.
                if (primaryNameSeen)
                    continue;
                primaryNameSeen = true;
            }
            result.Add(new ItsAccountSelectionItem(account.Id, account.Name));
        }

        // Реальной «Основной» нет — добавляем виртуальный пункт «Основная» первым (прежнее поведение).
        if (!primaryNameSeen)
            result.Insert(0, new(null, LocalizationManager.T("ItsAccounts.Primary")));

        return result;
    }

    /// <summary>Индекс пункта по идентификатору записи (0 — «Основная», -1 — не найден).</summary>
    public static int IndexOf(List<ItsAccountSelectionItem> items, string? accountId)
    {
        if (!string.IsNullOrWhiteSpace(accountId))
        {
            var index = items.FindIndex(i => i.Id == accountId);
            if (index >= 0)
                return index;
        }
        return 0;
    }
}