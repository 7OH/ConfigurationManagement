#if LINUX
using Configuration_Management.Models;

namespace Configuration_Management.Services;

/// <summary>
/// Linux-зеркало активации окна запущенной базы (issue #339). Активация чужих окон
/// на X11/Wayland требует wmctrl/xdotool (внешние утилиты), поэтому на Linux двойной
/// клик и Enter в отборе «Только запущенные» выполняют обычное действие (запуск/
/// настройку), а активация окна помечена как не поддержанная.
/// </summary>
public static class OneCWindowActivator
{
    /// <summary>На Linux активация окна 1С не поддержана — всегда false.</summary>
    public static bool Activate(Infobase infobase) => false;
}
#endif