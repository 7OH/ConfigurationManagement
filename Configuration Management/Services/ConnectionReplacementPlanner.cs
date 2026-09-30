using Configuration_Management.Models;

namespace Configuration_Management.Services;

/// <summary>
/// Область применения массовой замены в строках подключения.
/// </summary>
public enum ConnectionReplaceScope
{
    /// <summary>Все видимые базы.</summary>
    AllBases,

    /// <summary>Базы из набора Id мультивыделения (<see cref="Infobase.Id"/>).</summary>
    BatchSelected,

    /// <summary>Базы текущей группы (по значению <see cref="Infobase.Group"/>).</summary>
    CurrentGroup
}

/// <summary>
/// Строка предпросмотра массовой замены: база, поле и «было → станет».
/// </summary>
/// <param name="Infobase">База, к которой применяется замена.</param>
/// <param name="Field">Поле строки подключения (для <see cref="ConnectionField.Any"/> — вся строка).</param>
/// <param name="BeforeText">Текущее значение поля (или вся строка для <see cref="ConnectionField.Any"/>).</param>
/// <param name="AfterText">Новое значение после замены.</param>
/// <param name="Changed">true — значение реально изменится.</param>
public sealed record ConnectionReplacePreviewRow(
    Infobase Infobase,
    ConnectionField Field,
    string BeforeText,
    string AfterText,
    bool Changed);

/// <summary>
/// План замены: затронутые строки предпросмотра и сводка счётчиков.
/// </summary>
/// <param name="Rows">Строки предпросмотра; только с <see cref="ConnectionReplacePreviewRow.Changed"/> == true.</param>
/// <param name="AffectedCount">Число уникальных изменяемых баз (без дублей по экземпляру базы).</param>
/// <param name="NoMatchCount">Число кандидатов с подключением, у которых совпадение не найдено.</param>
/// <param name="EmptyConnectionCount">Число кандидатов без строки подключения (не участвуют).</param>
public sealed record ConnectionReplacePlan(
    IReadOnlyList<ConnectionReplacePreviewRow> Rows,
    int AffectedCount,
    int NoMatchCount,
    int EmptyConnectionCount);

/// <summary>
/// Запись для отката одной замены: прежние и новые настройки подключения базы.
/// <see cref="Before"/> — глубокая копия прежних настроек (снята до мутации), чтобы
/// откат не зависел от последующих правок полей базы.
/// </summary>
/// <param name="Infobase">База, чьё подключение было изменено.</param>
/// <param name="Before">Глубокая копия прежних настроек подключения.</param>
/// <param name="After">Копия новых настроек подключения.</param>
public sealed record ConnectionReplaceUndoEntry(
    Infobase Infobase,
    ConnectionSettings Before,
    ConnectionSettings After);

/// <summary>
/// Чистый планировщик массовой замены в строках подключения (0.3.9.188, функция 6
/// «Массовая замена в строке подключения баз»): отбор кандидатов области, построение
/// плана предпросмотра <strong>без мутаций</strong>, применение с формированием снапшота
/// для отката и восстановление через <see cref="Undo"/>. Не имеет платформенных
/// зависимостей и не знает про приватность баз — получает уже видимый список от
/// вызывающей стороны (MainViewModel фильтрует скрытые приватные базы).
/// </summary>
public static class ConnectionReplacementPlanner
{
    /// <summary>
    /// Отбирает кандидатов области из переданного «видимого» списка баз.
    /// <see cref="ConnectionReplaceScope.AllBases"/> — весь список;
    /// <see cref="ConnectionReplaceScope.BatchSelected"/> — базы, чей
    /// <see cref="Infobase.Id"/> входит в набор <paramref name="batchIds"/> (порядок —
    /// как в списке, неизвестные Id игнорируются);
    /// <see cref="ConnectionReplaceScope.CurrentGroup"/> — базы, чья группа равна
    /// <paramref name="currentGroup"/> (пустой/null текущей группы → пустой результат).
    /// </summary>
    /// <param name="visibleInfobases">Уже отфильтрованный «видимый» список баз (приватные скрыты вызывающей стороной).</param>
    /// <param name="batchIds">Набор Id для области «выделенные» (может быть null).</param>
    /// <param name="currentGroup">Группа для области «текущая группа» (может быть null/пустой).</param>
    /// <param name="scope">Область применения.</param>
    /// <returns>Новый список кандидатов (исходный список не мутируется).</returns>
    public static IReadOnlyList<Infobase> SelectCandidates(
        IReadOnlyList<Infobase> visibleInfobases,
        IReadOnlyCollection<string>? batchIds,
        string? currentGroup,
        ConnectionReplaceScope scope)
    {
        ArgumentNullException.ThrowIfNull(visibleInfobases);

        var result = new List<Infobase>();
        switch (scope)
        {
            case ConnectionReplaceScope.AllBases:
                foreach (var ib in visibleInfobases)
                {
                    if (ib is not null)
                        result.Add(ib);
                }
                break;

            case ConnectionReplaceScope.BatchSelected:
                if (batchIds is { Count: > 0 })
                {
                    var ids = new HashSet<string>(batchIds, StringComparer.Ordinal);
                    foreach (var ib in visibleInfobases)
                    {
                        if (ib is not null && ids.Contains(ib.Id))
                            result.Add(ib);
                    }
                }
                break;

            case ConnectionReplaceScope.CurrentGroup:
                if (!string.IsNullOrEmpty(currentGroup))
                {
                    foreach (var ib in visibleInfobases)
                    {
                        if (ib is not null && string.Equals(ib.Group, currentGroup, StringComparison.Ordinal))
                            result.Add(ib);
                    }
                }
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(scope));
        }

        return result;
    }

    /// <summary>
    /// Фильтрует «видимый» список баз по приватности (0.3.9.189, функция 6): приватные
    /// базы скрываются, когда профиль не разблокирован (<paramref name="canShowPrivateBases"/>
    /// == false), — единая точка фильтрации для моста в MainViewModel (см.
    /// <see cref="ConnectionReplaceScope"/>). Null-элементы отбрасываются. Не мутирует
    /// входной список.
    /// </summary>
    /// <param name="infobases">Исходный список (включая приватные базы).</param>
    /// <param name="canShowPrivateBases">true — приватные базы показываются (профиль разблокирован или без пароля).</param>
    /// <returns>Новый список видимых баз.</returns>
    public static IReadOnlyList<Infobase> FilterVisibleInfobases(
        IEnumerable<Infobase> infobases,
        bool canShowPrivateBases)
    {
        ArgumentNullException.ThrowIfNull(infobases);

        var result = new List<Infobase>();
        foreach (var infobase in infobases)
        {
            if (infobase is null)
                continue;
            if (!infobase.IsPrivate || canShowPrivateBases)
                result.Add(infobase);
        }

        return result;
    }

    /// <summary>
    /// Строит план замены БЕЗ мутации баз: для каждого кандидата применяет правило к
    /// копии настроек через <see cref="ConnectionStringEditor.TryApply"/>. Базы без
    /// строки подключения (ни одно из полей Server/Ref/FilePath/WebUrl не заполнено —
    /// каноническая строка вырожденная «Srvr="";Ref=""»/«File=""»/«WS=""») попадают в
    /// <see cref="ConnectionReplacePlan.EmptyConnectionCount"/> и в Rows не включаются.
    /// Невалидный regex-паттерн пробрасывает <see cref="ArgumentException"/> наружу
    /// (его перехватывает VM и превращает в сообщение об ошибке).
    /// </summary>
    /// <param name="candidates">Кандидаты области (результат <see cref="SelectCandidates"/>).</param>
    /// <param name="rule">Правило замены.</param>
    /// <returns>План замены: строки с Changed == true и сводка счётчиков.</returns>
    public static ConnectionReplacePlan Plan(
        IReadOnlyList<Infobase> candidates,
        ConnectionStringReplaceRule rule)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(rule);

        var rows = new List<ConnectionReplacePreviewRow>();
        var affected = new HashSet<Infobase>();
        var noMatchCount = 0;
        var emptyConnectionCount = 0;

        foreach (var infobase in candidates)
        {
            if (infobase is null)
                continue;

            if (IsEmptyConnection(infobase))
            {
                emptyConnectionCount++;
                continue;
            }

            if (TryMatch(infobase, rule, out _, out var beforeValue, out var afterValue))
            {
                rows.Add(new ConnectionReplacePreviewRow(infobase, rule.Field, beforeValue, afterValue, true));
                affected.Add(infobase);
            }
            else
            {
                noMatchCount++;
            }
        }

        return new ConnectionReplacePlan(rows, affected.Count, noMatchCount, emptyConnectionCount);
    }

    /// <summary>
    /// Применяет правило: мутирует <see cref="Infobase.Connection"/> затронутых баз
    /// (заменяет на копию новых настроек из <see cref="ConnectionStringEditor.TryApply"/>)
    /// и возвращает записи отката. Для каждой записи <see cref="ConnectionReplaceUndoEntry.Before"/>
    /// — глубокая копия ВСЕХ полей прежних настроек (Type/Server/DatabaseName/FilePath/
    /// BlockScheduledJobs/ForbidSpeechRecognition/User/Password/AuthenticationMode/Port/
    /// WebUrl), чтобы откат не зависел от последующих правок базы; <see cref="ConnectionReplaceUndoEntry.After"/>
    /// — копия новых настроек. Если правило не совпало ни в одной базе — возвращает пустой список.
    /// </summary>
    /// <param name="candidates">Кандидаты области.</param>
    /// <param name="rule">Правило замены.</param>
    /// <returns>Записи отката для затронутых баз.</returns>
    public static IReadOnlyList<ConnectionReplaceUndoEntry> Apply(
        IReadOnlyList<Infobase> candidates,
        ConnectionStringReplaceRule rule)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(rule);

        var entries = new List<ConnectionReplaceUndoEntry>();

        foreach (var infobase in candidates)
        {
            if (infobase is null || IsEmptyConnection(infobase))
                continue;

            if (TryMatch(infobase, rule, out var updated, out _, out _))
            {
                var before = DeepCopy(infobase.Connection);
                infobase.Connection = updated;
                entries.Add(new ConnectionReplaceUndoEntry(infobase, before, DeepCopy(updated)));
            }
        }

        return entries;
    }

    /// <summary>
    /// Откат последней замены: для каждой записи восстанавливает прежние настройки
    /// (мутация объектов баз на месте — свойству <see cref="Infobase.Connection"/> присваивается
    /// снапшот <see cref="ConnectionReplaceUndoEntry.Before"/>). Пустой/null список — no-op.
    /// </summary>
    /// <param name="entries">Записи отката из <see cref="Apply"/>.</param>
    public static void Undo(IEnumerable<ConnectionReplaceUndoEntry> entries)
    {
        if (entries is null)
            return;

        foreach (var entry in entries)
        {
            if (entry?.Infobase is null || entry.Before is null)
                continue;

            entry.Infobase.Connection = entry.Before;
        }
    }

    // ---------- Вспомогательные ----------

    /// <summary>
    /// Единая точка сопоставления правила с базой: Plan (предпросмотр) и Apply (применение)
    /// используют один и тот же вызов <see cref="ConnectionStringEditor.TryApply"/>, поэтому
    /// план и результат применения не расходятся.
    /// </summary>
    private static bool TryMatch(
        Infobase infobase,
        ConnectionStringReplaceRule rule,
        out ConnectionSettings updated,
        out string beforeValue,
        out string afterValue)
        => ConnectionStringEditor.TryApply(infobase.Connection, rule, out updated, out beforeValue, out afterValue);

    /// <summary>
    /// База «без подключения»: ни одно из полей подключения Server/DatabaseName/FilePath/
    /// WebUrl не заполнено — каноническая строка вырожденная и не содержит реального
    /// подключения (Srvr="";Ref="" / File="" / WS=""). Такие базы не участвуют в замене
    /// (счётчик <see cref="ConnectionReplacePlan.EmptyConnectionCount"/>).
    /// </summary>
    private static bool IsEmptyConnection(Infobase infobase)
    {
        var connection = infobase.Connection;
        return string.IsNullOrWhiteSpace(connection.Server)
            && string.IsNullOrWhiteSpace(connection.DatabaseName)
            && string.IsNullOrWhiteSpace(connection.FilePath)
            && string.IsNullOrWhiteSpace(connection.WebUrl);
    }

    /// <summary>
    /// Независимая копия всех полей настроек подключения (строки неизменяемы — копирование
    /// по значению достаточно; снапшот не ссылается на живой объект базы).
    /// </summary>
    private static ConnectionSettings DeepCopy(ConnectionSettings source)
    {
        if (source is null)
            return new ConnectionSettings();

        return new ConnectionSettings
        {
            Type = source.Type,
            Server = source.Server,
            DatabaseName = source.DatabaseName,
            FilePath = source.FilePath,
            BlockScheduledJobs = source.BlockScheduledJobs,
            ForbidSpeechRecognition = source.ForbidSpeechRecognition,
            User = source.User,
            Password = source.Password,
            AuthenticationMode = source.AuthenticationMode,
            Port = source.Port,
            WebUrl = source.WebUrl
        };
    }
}