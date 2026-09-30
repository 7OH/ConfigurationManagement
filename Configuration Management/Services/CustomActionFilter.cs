using System;
using System.Collections.Generic;
using System.Linq;
using Configuration_Management.Models;

namespace Configuration_Management.Services;

/// <summary>Контекст вызова пользовательского действия (чем определяется набор целей и видимость).</summary>
public enum CustomActionContext
{
    /// <summary>Одиночная выбранная база (меню базы).</summary>
    SingleBase,

    /// <summary>Мультивыделение «Для выделенных (N)…».</summary>
    Batch,

    /// <summary>Текущая группа (действие выполняется для всех баз группы).</summary>
    Group
}

/// <summary>
/// Чистая логика отбора пользовательских действий и целей для контекстных меню
/// (0.3.9.195, функция 7): какие действия показывать в контексте (одиночная база /
/// мультивыделение / группа) и какие базы-цели видимы (приватные скрытые исключаются).
/// Без платформенных зависимостей; единая точка политики фильтрации целей —
/// <see cref="ConnectionReplacementPlanner.FilterVisibleInfobases"/>.
/// </summary>
public static class CustomActionFilter
{
    /// <summary>
    /// Отбирает действия для контекста:
    /// <list type="bullet">
    /// <item><see cref="CustomActionContext.SingleBase"/> → Scope in {Base, Both};</item>
    /// <item><see cref="CustomActionContext.Batch"/> → <see cref="CustomAction.SupportsBatch"/>
    /// == true (Scope не важен — применяется к выделенным базам);</item>
    /// <item><see cref="CustomActionContext.Group"/> → Scope in {Group, Both}.</item>
    /// </list>
    /// Результат — в исходном порядке (порядок списка действий из хранилища, без сортировки).
    /// Пустой список действий → пустой результат.
    /// </summary>
    public static IReadOnlyList<CustomAction> SelectActions(
        IReadOnlyList<CustomAction> actions, CustomActionContext context)
    {
        if (actions is null || actions.Count == 0)
            return Array.Empty<CustomAction>();

        return context switch
        {
            CustomActionContext.SingleBase => actions
                .Where(a => a.Scope == CustomActionScope.Base || a.Scope == CustomActionScope.Both)
                .ToList(),
            CustomActionContext.Batch => actions
                .Where(a => a.SupportsBatch)
                .ToList(),
            CustomActionContext.Group => actions
                .Where(a => a.Scope == CustomActionScope.Group || a.Scope == CustomActionScope.Both)
                .ToList(),
            _ => Array.Empty<CustomAction>()
        };
    }

    /// <summary>
    /// Фильтрует цели по видимости: приватные базы при заблокированном профиле
    /// (<paramref name="canShowPrivateBases"/> == false) исключаются. Оборачивает
    /// существующий <see cref="ConnectionReplacementPlanner.FilterVisibleInfobases"/> —
    /// единая точка политики приватности. Не мутирует входной список.
    /// </summary>
    public static IReadOnlyList<Infobase> FilterVisibleTargets(
        IReadOnlyList<Infobase> targets, bool canShowPrivateBases)
    {
        if (targets is null || targets.Count == 0)
            return Array.Empty<Infobase>();

        return ConnectionReplacementPlanner.FilterVisibleInfobases(targets, canShowPrivateBases);
    }
}