using System;

namespace Configuration_Management.Models;

/// <summary>
/// Параметры установки блокировки сеансов информационной базы (функция №20 StartManager):
/// время начала, длительность и текст сообщения пользователям.
/// Чистая .NET-модель без UI-зависимостей — используется и Windows/WPF, и Linux/Avalonia.
/// </summary>
public class SessionLockOptions
{
    /// <summary>
    /// Время начала блокировки. По умолчанию устанавливается на «+5 минут»
    /// от текущего момента (как в StartManager 1.4).
    /// </summary>
    public DateTime StartTime { get; set; }

    /// <summary>
    /// Длительность блокировки. По умолчанию 30 минут (как в StartManager 1.4).
    /// </summary>
    public TimeSpan Duration { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>
    /// Текст сообщения пользователям. Может содержать параметры-плейсхолдеры
    /// <c>{ДатаНач}</c> / <c>{ДатаКон}</c>, которые подставляются фактическими
    /// значениями дат начала и окончания блокировки перед запуском 1С.
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>Момент окончания блокировки = время начала + длительность.</summary>
    public DateTime EndTime => StartTime + Duration;

    /// <summary>
    /// Длительность в минутах (для передачи в командную строку 1С /LockIB).
    /// </summary>
    public int DurationMinutes => Math.Max(0, (int)Math.Round(Duration.TotalMinutes));

    /// <summary>
    /// Подставляет плейсхолдеры <c>{ДатаНач}</c> / <c>{ДатаКон}</c> в текст
    /// сообщения фактическими значениями дат. Если плейсхолдеры отсутствуют —
    /// возвращает текст без изменений.
    /// </summary>
    public string BuildFinalMessage()
    {
        var message = Message ?? string.Empty;
        message = message.Replace("{ДатаНач}", StartTime.ToString("dd.MM.yyyy HH:mm"), StringComparison.Ordinal);
        message = message.Replace("{ДатаКон}", EndTime.ToString("dd.MM.yyyy HH:mm"), StringComparison.Ordinal);
        return message;
    }

    /// <summary>
    /// Строка сеансов для командной строки 1С в формате /LockIB:
    /// «время начала;длительность в минутах;текст сообщения».
    /// Время — в формате ДД.ММ.ГГГГ ЧЧ:ММ:СС, принятом платформой 1С.
    /// </summary>
    public string BuildSessionLockString()
    {
        var start = StartTime.ToString("dd.MM.yyyy HH:mm:ss");
        return $"{start};{DurationMinutes};{BuildFinalMessage()}";
    }
}