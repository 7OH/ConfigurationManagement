using System.Collections.Generic;
using Configuration_Management.Models;

namespace Configuration_Management.Services;

/// <summary>
/// Хранилище учётных записей ИТС 1С (issue #333): отдельный читаемый JSON-файл
/// <c>its_accounts.json</c> рядом с настройками приложения, по образцу
/// <see cref="ICustomConfigTypesStore"/>. Текущие логин/пароль из старых настроек
/// (<see cref="AppSettings.UpdatesLogin"/>/<see cref="AppSettings.UpdatesPassword"/>)
/// переносятся в запись «Основная» при первом обращении (идемпотентно: файл создаётся
/// один раз, старые настройки не изменяются).
/// </summary>
public interface IItsAccountsStore
{
    /// <summary>Полный путь к файлу учётных записей.</summary>
    string FilePath { get; }

    /// <summary>Загружает учётные записи из файла (с миграцией из настроек при первом запуске).
    /// Правила «Основная»: если в файле 2+ основных — основной остаётся первый найденный;
    /// если 0 — основной становится первая запись.</summary>
    IReadOnlyList<ItsAccount> Load();

    /// <summary>Сохраняет учётные записи в файл (читаемый UTF-8, атомарная запись).
    /// Нормализует правило «ровно одна основная».</summary>
    void Save(IReadOnlyCollection<ItsAccount> accounts);

    /// <summary>Возвращает учётную запись по идентификатору (пустой id → null).</summary>
    ItsAccount? GetById(string id);

    /// <summary>Возвращает основную учётную запись (или null, если справочник пуст).</summary>
    ItsAccount? GetPrimary();

    /// <summary>Резолвит учётную запись для использования: запись по <paramref name="accountId"/>,
    /// если задан и существует, иначе основная (пусто → основная, правило типовых #321/#333).</summary>
    ItsAccount? Resolve(string? accountId);

    /// <summary>Добавляет новую или сохраняет правку существующей записи. Флаг «Основная»
    /// новой записи не устанавливается (кроме случая пустого справочника); смена основной —
    /// только через <see cref="SetPrimary"/>.</summary>
    void Upsert(ItsAccount account);

    /// <summary>Удаляет запись по идентификатору. Если удаляемая была основной — основной
    /// становится первая запись списка.</summary>
    void Delete(string id);

    /// <summary>Делает запись с указанным идентификатором основной (снимает флаг с остальных).</summary>
    void SetPrimary(string id);
}