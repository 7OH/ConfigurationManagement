using Configuration_Management.Services;
using Xunit;

namespace ConfigurationManagement.Tests;

/// <summary>
/// Тесты отладочного флага логики обновлений платформы (issue #361): флаг включается
/// переменной окружения <c>CM_DEBUG_PLATFORM_UPDATE=1</c> и пишет диагностику в
/// <c>platform-update-debug.log</c>. Тесты не зависят от окружения: проверяется, что
/// вызовы <see cref="PlatformUpdateDebug.Log"/> безопасны (не бросают исключений)
/// при любом состоянии флага.
/// </summary>
public sealed class PlatformUpdateDebugTests
{
    [Fact]
    public void Log_NullOrEmpty_DoesNotThrow()
    {
        // Пустые/нулевые сообщения отсекаются до записи — вызов безопасен.
        PlatformUpdateDebug.Log(null!);
        PlatformUpdateDebug.Log(string.Empty);
        PlatformUpdateDebug.Log("   ");
    }

    [Fact]
    public void Log_RegularMessage_DoesNotThrow()
    {
        // При выключенном флаге запись не выполняется; при включённом — пишется в файл.
        // В обоих случаях исключений быть не должно (диагностика не роняет приложение).
        PlatformUpdateDebug.Log("[Test] диагностическая строка");
    }
}