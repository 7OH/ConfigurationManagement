# v0.3.12.6 — Имена обновлений, папки и переоткрытие окна (issue #352)

Windows/WPF и Linux/Avalonia.

## Исправлено

- **Имена скачиваемых файлов обновления сохраняют оригинальное имя**
  (issue #352) — файл сохраняется как «<Имя>_<версия>_<оригинал>»
  (например `УТ_11.5.26.118_Trade_11_5_26_118_updsetup.cf`), чтобы «полный
  дистрибутив» и «дистрибутив обновления» различались и не перезаписывали
  друг друга. Общий `UpdateDownloadNaming` для WPF и Avalonia.

- **Папка одиночного скачивания запоминается отдельно от папки цепочки**
  («что для полной версии, что для цепочки») — новая настройка
  `AppSettings.UpdateDistributionFolder` (пусто — прежняя папка цепочки);
  диалог сохранения предлагает её же.

- **Окно проверки обновлений подхватывает активную загрузку при переоткрытии**
  (Windows/WPF и Linux/Avalonia) — клик по индикатору на главном окне возвращает
  окно с реальным прогрессом уже идущей закачки (`update:`/`chain:`) для этой ИБ.
  Идентификаторы закачек сохранены, чтобы выбор окна из индикатора из #359
  не сломался.

## Тесты

Полный набор `dotnet test` зелёный (**2302**); сборка решения (`dotnet build`) и
кросс-сборка Linux (`dotnet build -p:ForceLinux=true`) без ошибок. Новые тесты
`UpdateDownloadNamingTests` (+4) покрывают имя сохранения одиночного дистрибутива.

## Сборка

- Windows: single-file self-contained WPF (`net10.0-windows`, win-x64) —
  `ConfigurationManagement.exe` (обязательный ассет релиза, MZ-сигнатура и
  FileVersion 0.3.12.6 проверены, `--list` exit=0);
  ZIP-архив с exe внутри — дополнительный ассет для ручной загрузки.
- Linux: single-file self-contained Avalonia (`net10.0`, linux-x64) —
  `ConfigurationManagement-linux-x64` (ELF `7F 45 4C 46` проверен),
  пакет `configuration-management_0.3.12.6_amd64.deb`.
- Контрольные суммы — в `SHA256SUMS.txt`.

## Артефакты

| Файл | Размер | SHA-256 |
|---|---|---|
| `ConfigurationManagement.exe` | 84 165 649 байт (≈ 80.3 MiB) | `f4f4026c262918f1e72dfb2f666299401b93c408bbe5fe63d16b80d5168cdbf2` |
| `ConfigurationManagement-0.3.12.6-win-x64.zip` | 78 352 723 байт (≈ 74.7 MiB) | `67cfad3a85965966ff02e5e22f17f7266be02dfe36480b4abfe9e29da5f3285d` |
| `ConfigurationManagement-linux-x64` | 52 634 227 байт (≈ 50.2 MiB) | `768ba744d56c554b99ba26ae780fd87f10f41ca70b7381c326da50e628e997c7` |
| `configuration-management_0.3.12.6_amd64.deb` | 45 407 282 байт (≈ 43.3 MiB) | `a8d2f9da34b4cc37093bd7894ae917872e894a6431d83ff1bfa143f814f6d20e` |
| `SHA256SUMS.txt` | 4 записи | — |

Issues остаются открытыми — ждём подтверждения от репортеров.
