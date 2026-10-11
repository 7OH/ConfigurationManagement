# v0.3.12.8 — Запуск второй копии на сервере терминалов (issue #362)

Исправление проблемы, когда приложение не запускалось у второго пользователя
сервера терминалов (RDS) — вплоть до фатального диалога «Не удалось запустить
приложение».

## Что исправлено (Windows/WPF)

Мьютекс и событие активации single-instance переведены из глобального
пространства `Global\` в область одной сессии `Local\` (`App.xaml.cs`):

- **Раньше:** именованные объекты `Global\ConfigurationManagement_1C_*` были
  видны во всех сессиях сервера терминалов. Второй пользователь получал
  `UnauthorizedAccessException` при создании мьютекса (фатальный диалог) либо
  его экземпляр тихо завершался, «активируя» окно первого пользователя.
- **Теперь:** `Local\` ограничивает область одной сессией — каждый
  RDS-пользователь получает свой single-instance scope. На обычном десктопе
  поведение не меняется.

Дополнительно:

- `new Mutex` обёрнут в `try/catch (UnauthorizedAccessException)` — при отказе
  доступа к именованному объекту (возможен при специфичных политиках
  безопасности RDS) приложение запускается без гейта второго экземпляра вместо
  фатального диалога.
- В fallback `SignalExistingInstance` процессы фильтруются по `SessionId` —
  `SetForegroundWindow` больше не пытается поднять окно пользователя из другой
  сессии RDS.

Версия Linux/Avalonia не затрагивала: там single-instance построен на файловом
lock в per-user каталоге данных и на RDS безопасен.

## Проверки

- `dotnet test` — **2302** passed, 0 failed.
- Сборки WPF (win-x64) и Linux (linux-x64, кросс-компиляция) без ошибок.
- `dotnet publish -c Release -r win-x64` — exe запускается, `--list` exit=0.

## Ассеты

| Файл | Назначение |
|---|---|
| `ConfigurationManagement.exe` | Windows single-file self-contained (основной ассет автообновления) |
| `ConfigurationManagement-0.3.12.8-win-x64.zip` | ZIP-архив exe |
| `ConfigurationManagement-linux-x64` | Linux single-file (Avalonia) |
| `configuration-management_0.3.12.8_amd64.deb` | Debian/Ubuntu пакет |
| `SHA256SUMS.txt` | Контрольные суммы |
