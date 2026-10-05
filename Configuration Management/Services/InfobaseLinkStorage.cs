using System;
using System.Linq;
using Configuration_Management.Models;

namespace Configuration_Management.Services;

/// <summary>
/// Сохранение связи ИБ ↔ типовая конфигурация в репозиторий: переносит три поля привязки
/// (<see cref="Infobase.UpdateConfigCode"/>/<see cref="Infobase.UpdateUrlOverride"/>/
/// <see cref="Infobase.UpdateUrlSegment"/>) из объекта базы в найденную в списке запись и
/// сохраняет список. Общая логика, дублирующая проверенный <c>PersistLink</c> окна
/// «Связать с конфигурацией» (кластер C, issue #346 — кнопка «Очистить» в свойствах базы).
/// Если объект ещё не в репозитории (новая база) — сохранение пропускается: поля перетекут
/// в репозиторий штатно через <c>ApplyTo</c> при сохранении свойств базы.
/// </summary>
public static class InfobaseLinkStorage
{
    /// <summary>
    /// Сохраняет три поля привязки <paramref name="infobase"/> в репозиторий.
    /// Исключения не пробрасываются — при неудаче пишется предупреждение в лог (если передан).
    /// </summary>
    public static void Save(Infobase infobase, IInfobaseRepository repository, IAppLogger? logger = null)
    {
        if (infobase is null)
            throw new ArgumentNullException(nameof(infobase));
        if (repository is null)
            throw new ArgumentNullException(nameof(repository));

        try
        {
            var all = repository.Load();
            var match = all.FirstOrDefault(x => ReferenceEquals(x, infobase));
            if (match is null)
                return;
            match.UpdateConfigCode = infobase.UpdateConfigCode;
            match.UpdateUrlOverride = infobase.UpdateUrlOverride;
            match.UpdateUrlSegment = infobase.UpdateUrlSegment;
            repository.Save(all);
        }
        catch (Exception ex)
        {
            logger?.Warn("Не удалось сохранить связь ИБ ↔ конфигурация в репозиторий: " + ex.Message);
        }
    }
}