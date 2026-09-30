using System.Collections.Generic;

namespace Configuration_Management.Models;

/// <summary>
/// Статус обращения к каталогу технологической платформы 1С на <c>releases.1c.ru</c>.
/// </summary>
public enum PortalFetchStatus
{
    /// <summary>Каталог получен успешно (или файлы релиза подгружены).</summary>
    Ok,

    /// <summary>Требуется авторизация на портале 1С (редирект на <c>login.1c.ru</c>, 401/403).</summary>
    AuthRequired,

    /// <summary>Запрошенный ресурс не найден (HTTP 404 — страница-маркер «404 Not Found»).</summary>
    NotFound,

    /// <summary>Сетевая ошибка, пустое тело ответа либо неизвестный сбой.</summary>
    NetworkError,

    /// <summary>Операция отменена через <see cref="System.Threading.CancellationToken"/>.</summary>
    Cancelled,
}

/// <summary>
/// Результат обращения к каталогу технологической платформы 1С: статус и список
/// доступных версий (<see cref="Releases"/>) либо файлы выбранного релиза
/// (<see cref="Release"/>). Ошибки сети/авторизации не бросают исключений — итог
/// всегда описывается статусом и ключом локализации.
/// </summary>
public sealed class PlatformCatalogResult
{
    /// <summary>Итоговый статус обращения к порталу.</summary>
    public PortalFetchStatus Status { get; init; } = PortalFetchStatus.Ok;

    /// <summary>Ключ локализации сообщения об ошибке (префикс «PlatformUpdate.Error.*»);
    /// пуст для успешного результата.</summary>
    public string ErrorKey { get; init; } = string.Empty;

    /// <summary>Список доступных версий платформы (заполняется
    /// <c>GetAvailableReleasesAsync</c>), отсортированный по убыванию.</summary>
    public IReadOnlyList<PlatformRelease> Releases { get; init; } = new List<PlatformRelease>();

    /// <summary>Релиз с подгруженными файлами дистрибутива (для <c>LoadReleaseFilesAsync</c>).</summary>
    public PlatformRelease? Release { get; init; }
}