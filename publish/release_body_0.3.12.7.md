# v0.3.12.7 — Кнопка разворачивания и «Закрыть» в окне «Серверы 1С» (issue #324)

Windows/WPF и Linux/Avalonia.

## Исправлено

- **Окно «Серверы 1С» на Windows получило кнопку разворачивания** (issue #324,
  комментарий от 7OH от 2026-10-11): в заголовке окна появилась типовая кнопка
  «Развернуть/Восстановить». В 0.3.12.3 кнопка добавлена только в Linux/Avalonia
  (собственный chrome), на Windows системные кнопки заголовка снимались, и
  разворота не было вовсе. Глиф переключается при смене состояния окна.

- **Убрана кнопка «Закрыть» рядом с «Диагностика сети»** в окне «Серверы 1С»
  (Windows и Linux) — дублировала кнопку ✕ в заголовке окна. Закрытие по Esc
  сохранено (раньше его давала IsCancel у панельной кнопки, теперь обрабатывается
  вручную).

## Тесты

Полный набор `dotnet test` зелёный (**2302**); сборка решения (`dotnet build`) и
кросс-сборка Linux (`dotnet build -p:ForceLinux=true`) без ошибок.

## Сборка

- Windows: single-file self-contained WPF (`net10.0-windows`, win-x64) —
  `ConfigurationManagement.exe` (FileVersion 0.3.12.7, `--list` exit=0);
  ZIP-архив с exe внутри — дополнительный ассет.
- Linux: single-file self-contained Avalonia (`net10.0`, linux-x64) —
  `ConfigurationManagement-linux-x64` (ELF `7F 45 4C 46`),
  пакет `configuration-management_0.3.12.7_amd64.deb`.
- Контрольные суммы — в `SHA256SUMS.txt`.

## Артефакты

| Файл | Размер | SHA-256 |
|---|---|---|
| `ConfigurationManagement.exe` | 84 166 213 байт (≈ 80.3 MiB) | `b2f6476e206dcf4180e3fb9700d847a8e9ed2595206acae14cb9b59f0fae1e7c` |
| `ConfigurationManagement-0.3.12.7-win-x64.zip` | 78 353 174 байт (≈ 74.7 MiB) | `5c6b049e6ff931d8b9bca3e051c07904eaabd564673893fd8610b18f16bc6efb` |
| `ConfigurationManagement-linux-x64` | 52 634 291 байт (≈ 50.2 MiB) | `4aa0e71b034bdc4215e5d9403673d4f0c3ec6fbeb57e4ded6c3e1b1c3cb4cd6e` |
| `configuration-management_0.3.12.7_amd64.deb` | 45 407 606 байт (≈ 43.3 MiB) | `57e4045668f0a5cf895f6ee8f1072be0a53ceafb6b6a893016923e8ba72d75ee` |
| `SHA256SUMS.txt` | 4 записи | — |

Issues остаются открытыми — ждём подтверждения от репортеров.
