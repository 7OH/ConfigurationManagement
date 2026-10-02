using System;

namespace Configuration_Management.Services;

/// <summary>
/// Чистый хелпер нормализации точки подключения rac: разделение введённого адреса
/// «host[:port]» на хост и порт (issue #324). Пользователь может указать нестандартный
/// порт агента/RAS двумя способами — отдельным полем «Порт» или прямо в поле адреса
/// как «host:port» (например, «localhost:27545», как в командной строке
/// <c>rac.exe localhost:27545 cluster list</c>). Без нормализации оба источника
/// склеивались бы в невалидный токен «host:port:port2» (см. <see cref="RacClient.BuildArguments"/>).
/// Правило приоритета: порт, указанный В адресе, имеет приоритет над значением поля
/// «Порт» — человек явно вписал его в адрес и ожидает именно его.
/// </summary>
public static class RacConnectionAddress
{
    /// <summary>
    /// Разделяет адрес «host[:port]» на хост и порт. Если порт в адресе есть и валиден
    /// (1–65535) — он возвращается и имеет приоритет над <paramref name="fallbackPort"/>;
    /// иначе хост берётся целиком, а порт — <paramref name="fallbackPort"/>.
    /// Пустой адрес даёт «localhost» с портом по умолчанию.
    /// Скобочная форма IPv6 («[::1]:1540») не поддерживается — для rac используется
    /// обычный host/IP; значение с несколькими «:» без числового хвоста считается хостом
    /// (голый IPv6) и возвращается целиком с fallback-портом.
    /// </summary>
    public static (string Host, int Port) Split(string address, int fallbackPort)
    {
        var value = (address ?? string.Empty).Trim();
        if (value.Length == 0)
            return ("localhost", NormalizePort(fallbackPort));

        // host:port — берём последнее «:», чтобы не ломать имена с двоеточием без порта
        // (тот же паттерн, что в ConnectionSettings.ParseSrvr и NetworkDiagnosticsService).
        var colon = value.LastIndexOf(':');
        if (colon > 0 && colon < value.Length - 1)
        {
            var head = value.Substring(0, colon);
            var portPart = value.Substring(colon + 1).Trim();

            // Голый IPv6 без скобок («2001:db8::1») — в head остались «:» → это адрес, не порт.
            if (head.IndexOf(':') < 0 && TryParsePort(portPart, out var port))
                return (head.Trim(), port);
        }

        return (value, NormalizePort(fallbackPort));
    }

    private static bool TryParsePort(string portPart, out int port) =>
        int.TryParse(portPart, out port) && port >= 1 && port <= 65535;

    private static int NormalizePort(int port) => port > 0 ? port : IRacClient.DefaultPort;
}