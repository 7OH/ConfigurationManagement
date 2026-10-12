namespace Configuration_Management.Services;

/// <summary>
/// Показ сообщения в строке состояния главного окна из фоновых сервисов (issue #366):
/// канал уведомления задания по расписанию «Проверить наличие обновления платформы»
/// «В главном окне». Реализации платформенных окон (WPF/Avalonia) обязаны тихо
/// деградировать: если главного окна ещё нет — no-op, исключения наружу не всплывают.
/// </summary>
public interface IMainWindowNotifier
{
    /// <summary>Показывает сообщение в строке состояния главного окна.</summary>
    /// <param name="message">Текст сообщения (пустой — no-op).</param>
    void ShowMessage(string message);
}
