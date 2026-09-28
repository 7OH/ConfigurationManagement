using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Configuration_Management.Models;

namespace Configuration_Management.Services;

/// <summary>
/// Сервис работы с хранилищем конфигурации 1С через пакетный режим DESIGNER
/// («Обозреватель хранилища конфигурации», цикл 0.3.9.127–0.3.9.130): выгрузка версий в .cf,
/// состав версии (список объектов), история версий, захват/отмена захвата объектов.
/// Реализация — <see cref="RepositoryStorageService"/>; интерфейс выделен для тестов ViewModel
/// (этапы 2–4) с фейковой реализацией.
/// </summary>
public interface IRepositoryStorageService
{
    /// <summary>
    /// Выгружает версию хранилища конфигурации в файл .cf (/ConfigurationRepositoryDumpCfg
    /// "файл" [-v N]); без номера версии (или -v -1) выгружается актуальная версия.
    /// Возвращает путь к созданному файлу. Ошибки — <see cref="RepositoryStorageException"/>.
    /// </summary>
    Task<string> DumpVersionToCfAsync(Infobase infobase, int? version, string cfPath,
        IProgress<string>? progress = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Загружает состав версии хранилища: DumpCfg во временный .cf → распаковка во временную
    /// файловую ИБ (CREATEINFOBASE с /UseTemplate) → /DumpConfigToFiles → чистый обход выгрузки
    /// <see cref="ConfigurationDiffEngine.BuildObjectList"/>. Временный каталог %TEMP%\cm_repo_<guid>
    /// гарантированно удаляется в finally.
    /// </summary>
    Task<IReadOnlyList<RepositoryObjectInfo>> LoadVersionObjectsAsync(Infobase infobase, int? version,
        string platformVersion, IProgress<string>? progress = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Возвращает историю версий хранилища (/ConfigurationRepositoryReport "файл" [-NBegin N] [-NEnd N]
    /// + <see cref="RepositoryHistoryParser"/>). При недоступности читаемого текстового формата
    /// отчёта (см. комментарий разведки в <see cref="RepositoryHistoryParser"/>) возвращает одну
    /// запись «актуальная версия»: Number = -1, IsCurrent = true.
    /// </summary>
    Task<IReadOnlyList<RepositoryVersion>> GetHistoryAsync(Infobase infobase, int? nBegin = null, int? nEnd = null,
        IProgress<string>? progress = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Захватывает объекты хранилища (/ConfigurationRepositoryLock): все объекты, если
    /// <paramref name="objectsXmlPath"/> не задан, либо только перечисленные в XML-файле
    /// (формат не документирован — экспериментальный).
    /// </summary>
    Task LockAsync(Infobase infobase, string? objectsXmlPath = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Отменяет захват объектов хранилища (/ConfigurationRepositoryUnlock): все объекты, если
    /// <paramref name="objectsXmlPath"/> не задан, либо только перечисленные в XML-файле.
    /// </summary>
    Task UnlockAsync(Infobase infobase, string? objectsXmlPath = null, CancellationToken cancellationToken = default);
}

/// <summary>
/// Ошибка операции с хранилищем конфигурации с человекочитаемым сообщением
/// (образец — <see cref="ConfigurationDiffException"/>): текст ошибки 1С из лога /Out
/// или описание сбоя самого сервиса.
/// </summary>
public sealed class RepositoryStorageException : Exception
{
    public RepositoryStorageException(string message) : base(message) { }
}