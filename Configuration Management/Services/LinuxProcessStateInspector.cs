using System;

namespace Configuration_Management.Services;

/// <summary>
/// Эвристика «отвечает ли процесс» по содержимому /proc/<pid>/stat (Linux,
/// issue #310). Чистая логика без I/O — покрыта юнит-тестами.
/// Формат записи: «pid (comm) state ppid …» — имя в скобках может содержать
/// пробелы, поэтому состояние ищется за последней закрывающей скобкой.
/// State 'D' (uninterruptible sleep — обычно ожидание диска/NFS) означает, что
/// процесс не отвечает; 'Z' (зомби) тоже не отвечает. Прочие состояния (R, S,
/// Ss, Sl, T, I и т.п.) считаются отвечающими.
/// Ограничение: надёжного универсального аналога Process.Responding на Linux нет —
/// 'D' ловится только когда процесс реально заблокирован ядром; «зависший» в
/// пользовательском режиме процесс (бесконечный цикл) выглядит как 'R' и
/// останется зелёной точкой. Для таких случаев полагаемся на Windows-путь.
/// </summary>
public static class LinuxProcessStateInspector
{
    /// <summary>
    /// Отвечает ли процесс по содержимому /proc/<pid>/stat.
    /// Некорректные/пустые данные трактуются как «отвечает» (тихая деградация),
    /// чтобы индикатор не мигал ложными тревогами при сбое чтения.
    /// </summary>
    public static bool StatIndicatesResponding(string? statContent)
    {
        if (string.IsNullOrEmpty(statContent))
            return true;

        var closeParen = statContent.LastIndexOf(')');
        if (closeParen < 0 || closeParen + 1 >= statContent.Length)
            return true;

        var tail = statContent[(closeParen + 1)..];
        var fields = tail.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length == 0)
            return true;

        var state = fields[0];
        return state != "D" && state != "Z";
    }
}