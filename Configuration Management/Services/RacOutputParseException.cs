using System;

namespace Configuration_Management.Services;

/// <summary>
/// Вывод утилиты rac не удалось распознать: rac завершился успешно (exit=0), вывод НЕ пуст,
/// но ни табличный разбор, ни формат блоков «ключ : значение» не дали ни одной строки данных
/// (issue #324). Обычно означает новую версию формата вывода rac — пользователю нужно понятное
/// сообщение и остановка бесконечного автообновления вместо тихих повторов каждые 5 с.
/// </summary>
public sealed class RacOutputParseException : Exception
{
    public RacOutputParseException(string message)
        : base(message)
    {
    }

    public RacOutputParseException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}