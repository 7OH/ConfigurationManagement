# План выпуска 0.3.12.4 — issue #360

Дата плана: 2026-10-11
Текущая версия: 0.3.12.3 → целевая: **0.3.12.4**
Issue: #360 «Время до окончания заакачек» (нет комментариев — работа по описанию).
Пропущены: #359 (нет комментариев — следующий выпуск), #352 (последний комментарий
не от автора — следующий выпуск), #324 (последний комментарий не от автора —
следующий выпуск), #330 (последний комментарий от автора — пропускаем).

## Суть

Показывать оставшееся время у полос прогресса: «00:25»/«16:13» (mm:ss),
«2:15:18» (h:mm:ss) свыше часа. Минимум — главное окно; по возможности — везде,
где идёт скачивание файлов.

## Дизайн

- **`Services/DownloadEta.cs`** (новый):
  - `DownloadEta.Format(TimeSpan)` — mm:ss / h:mm:ss;
  - `DownloadEta.AppendSuffix(text, eta)` — «45 % · 16:13»;
  - `DownloadEta.FormatMax(estimates)` — максимум по набору (индикатор главного окна);
  - `DownloadEtaTracker` — сглаженная скорость (alpha 0.3), порог показа 2 с,
    часы инжектируются (`Func<DateTime>`) — тесты детерминированы.
- **`BackgroundDownloadManager`**: `ActiveDownload.EstimatedRemaining` (обновляется
  в `ReportProgress`, сброс в Complete/Fail/Cancel); внутренний ctor с часами.
- **Отображение**: главное окно (`DownloadsEtaText`), окно проверки обновлений
  (одиночное + цепочка), окно скачивания платформы (`EtaText`), окно обновления
  платформы (`EtaText` под панельной полосой). WPF и Avalonia — параллельно.

## Файлы

| Файл | Изменение |
|---|---|
| `Services/DownloadEta.cs` | новый — форматирование + трекер |
| `Services/BackgroundDownloadManager.cs` | ETA записей, ctor с часами |
| `ViewModels/MainViewModel.Downloads.cs` | `DownloadsEtaText` |
| `Views/MainWindow.xaml` / `.Avalonia.cs` | TextBlock у полосы индикатора |
| `Views/UpdateCheckWindow.xaml.cs` / `.Avalonia.cs` | ETA одиночного скачивания и цепочки |
| `ViewModels/PlatformDownloadViewModel.cs` + окна | `EtaText` |
| `ViewModels/PlatformUpdateViewModel.cs` + окна | `EtaText` |
| `ConfigurationManagement.Tests/DownloadEtaTests.cs` | +13 тестов |
| `ConfigurationManagement.Tests/BackgroundDownloadManagerTests.cs` | +3 теста ETA |
| `Configuration Management.csproj` | 0.3.12.4 |
| `CHANGELOG.md`, `README.md` | секция/строка выпуска |

## Выпуск

1. `dotnet test` (2298 зелёных), `dotnet build` + `-p:ForceLinux=true`.
2. `build-windows-single-file.ps1`, `build-linux-single-file.ps1`,
   `publish/build_deb_win_0.3.12.4.py`, `check_deb_win_0.3.12.4.py`.
3. Коммит, пуш, тег `v0.3.12.4`, релиз (exe → zip → linux → deb → SHA256SUMS.txt),
   комментарий в issue #360. Issues НЕ закрываем.
