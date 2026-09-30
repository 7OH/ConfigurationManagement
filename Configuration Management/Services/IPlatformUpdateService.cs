using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Configuration_Management.Models;

namespace Configuration_Management.Services;

/// <summary>
/// Сервис автообновления технологической платформы 1С: получение списка доступных
/// версий с портала <c>releases.1c.ru</c>, ленивая подгрузка файлов дистрибутива
/// выбранного релиза и выбор файла под текущую ОС/разрядность. Ошибки сети,
/// авторизации и отмены не бросают исключений наружу — результат несёт статус
/// <see cref="PortalFetchStatus"/> и ключ локализации.
/// </summary>
public interface IPlatformUpdateService
{
    /// <summary>
    /// Получает список доступных версий платформы со страницы
    /// <c>releases.1c.ru/project/Platform83</c> (через
    /// <see cref="IOneCUpdatesService.GetPageTextAsync"/> и
    /// <see cref="OneCPlatformCatalogParser.ParseVersions"/>).
    /// </summary>
    /// <param name="ct">Токен отмены.</param>
    /// <returns>Результат со статусом и отсортированным по убыванию списком версий.</returns>
    Task<PlatformCatalogResult> GetAvailableReleasesAsync(CancellationToken ct = default);

    /// <summary>
    /// Лениво подгружает файлы дистрибутива выбранной версии из ответа
    /// <c>version_files?nick=Platform83&ver=…</c> (через
    /// <see cref="OneCPlatformCatalogParser.ParseDistributionFiles"/>). Заполняет
    /// <see cref="PlatformRelease.Files"/> переданного релиза, приводит
    /// <see cref="PlatformRelease.VersionFilesUrl"/> к абсолютному адресу и возвращает
    /// тот же экземпляр в <see cref="PlatformCatalogResult.Release"/>.
    /// </summary>
    /// <param name="release">Релиз, для которого подгружаются файлы (мутируется).</param>
    /// <param name="ct">Токен отмены.</param>
    /// <returns>Результат с заполненным <see cref="PlatformCatalogResult.Release"/>.</returns>
    Task<PlatformCatalogResult> LoadReleaseFilesAsync(PlatformRelease release, CancellationToken ct = default);

    /// <summary>
    /// Выбирает файл дистрибутива под текущую ОС и разрядность (чистый метод):
    /// на Windows приоритет zip-архива с setup.exe (x64 предпочтительнее x86),
    /// на Linux — пакет .deb/.rpm (x64), при отсутствии — универсальный .tar.gz.
    /// Пустой список возвращает null.
    /// </summary>
    /// <param name="files">Файлы дистрибутива релиза.</param>
    /// <returns>Выбранный файл или null, если подходящего нет.</returns>
    PlatformReleaseFile? PickDistribution(IReadOnlyList<PlatformReleaseFile> files);
}