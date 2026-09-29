using System.Threading;
using System.Threading.Tasks;
using Configuration_Management.Models;

namespace Configuration_Management.Services;

/// <summary>
/// Сервис проверки обновлений типовых конфигураций 1С по web-ресурсу обновлений.
/// Формирует web-адрес по правилу 1С
/// <c>downloads.1c.ru/ipp/.../Configs/<Конфигурация>/<Ред>/<Подред>/</c>,
/// проверяет наличие новых релизов и скачивает дистрибутив с прогрессом.
/// Кроссплатформенный — не зависит от UI-фреймворка.
/// </summary>
public interface IOneCUpdatesService
{
    /// <summary>
    /// Формирует адрес каталога релизов по правилу 1С. Если задан <paramref name="urlOverride"/> —
    /// возвращается он (ручная корректировка), иначе адрес строится из ника конфигурации
    /// (<c>releases.1c.ru/project/<ник></c>). Каждый сегмент экранируется, итог проверяется
    /// на валидность URI.
    /// </summary>
    /// <param name="config">Типовая конфигурация (нужен ник каталога релизов).</param>
    /// <param name="edition">Редакция (может переопределять ссылку целиком). Может быть null.</param>
    /// <param name="urlOverride">Полностью переопределённая ссылка. Пустая строка/null — автоформирование.</param>
    /// <param name="urlSegment">Персональный сегмент (ник) базы. Если задан — используется вместо
    /// ника типовой конфигурации (issue #322). Пустая строка/null — ник конфигурации.</param>
    string BuildUpdateUrl(OneCConfigType? config, OneCConfigEdition? edition, string? urlOverride, string? urlSegment = null);

    /// <summary>
    /// Проверяет наличие обновлений по заданному URL каталога релизов: загружает страницу,
    /// устойчиво ищет ссылки на архивы дистрибутивов и определяет максимальную версию.
    /// Ошибки сети/HTTP не бросают исключение — результат возвращается со статусом
    /// <see cref="ConfigUpdateStatus.Failed"/>.
    /// </summary>
    Task<ConfigUpdateCheckResult> CheckForUpdatesAsync(
        string configName, string currentVersion, string url, CancellationToken ct = default);

    /// <summary>
    /// Скачивает дистрибутив обновления по прямой ссылке в целевой файл с прогрессом.
    /// Возвращает полный путь сохранённого файла или null при ошибке/отмене.
    /// </summary>
    Task<string?> DownloadUpdateAsync(
        string url, string targetPath, IProgress<double>? progress = null, CancellationToken ct = default);

    /// <summary>Предопределённый набор типовых конфигураций 1С.</summary>
    System.Collections.Generic.IReadOnlyList<OneCConfigType> BuiltInConfigTypes { get; }
}