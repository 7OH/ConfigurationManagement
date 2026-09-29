using Configuration_Management.Models;

namespace Configuration_Management.Services;

/// <summary>
/// Сервис выгрузки конфигурации для «Обозревателя метаданных» (цикл 0.3.9.132–0.3.9.136):
/// генерирует XML-выгрузку <c>/DumpConfigToFiles</c> из информационной базы или файла .cf
/// и возвращает <see cref="MetadataDump"/> с путём к каталогу выгрузки и заголовком
/// (имя/версия конфигурации). Каталог выгрузки НЕ удаляется автоматически — владелец
/// (окно обозревателя) вызывает <see cref="MetadataDump.Delete"/> при закрытии.
/// </summary>
public interface IMetadataExplorerService
{
    /// <summary>
    /// Выгружает конфигурацию реальной базы в каталог <c>%TEMP%\cm_metaeplorer_<guid></c>.
    /// </summary>
    /// <exception cref="MetadataExplorerException">База недоступна, платформа не найдена,
    /// таймаут/ошибка DESIGNER (текст из лога 1С).</exception>
    Task<MetadataDump> DumpFromBaseAsync(
        Infobase infobase,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Распаковывает файл .cf во временную файловую ИБ (<c>CREATEINFOBASE /UseTemplate</c>,
    /// таймаут 30 минут) и выгружает конфигурацию <c>/DumpConfigToFiles</c>.
    /// </summary>
    /// <exception cref="MetadataExplorerException">Файл .cf отсутствует/битый, платформа не
    /// найдена, таймаут/ошибка операции.</exception>
    Task<MetadataDump> DumpFromCfAsync(
        string cfPath,
        string platformVersion,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default);
}