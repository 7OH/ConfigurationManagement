## 0.3.9.311 — диагностика #340 «Снятие выделения после контекстного меню»

**Исправлено:**

- **Расширенная БЕЗУСЛОВНАЯ трассировка кликов и активаций (issue #340, кластер B).** Присланный
  на 0.3.9.308 `trace.json` содержал только startup и записи `MenuOpened`/`MenuClosed` без НИ
  ОДНОГО события клика: трассировка была условной (`MouseDown` писался только при записанном
  снимке; `TryApply`/`Fallback`/`EnsureStable` — только при срабатывании соответствующих веток),
  поэтому сессия не содержала воспроизведения проблемного сценария. Теперь пишутся клики и
  активации безусловно:
- Новый чистый метод [`BuildClickTraceLine`](Configuration%20Management/Services/BatchSelectionHelper.cs)
  — единый формат события клика (поля `x`, `y`, `modifiers`, `target`, `snapshot`, `redelivery`,
  `pinned`) + [`FormatModifiers`](Configuration%20Management/Services/BatchSelectionHelper.cs):
  используется И WPF, И Avalonia, записи платформ симметричны.
- `MouseDown`/`MouseUp` по дереву (WPF, [`MainWindow.Events.cs`](Configuration%20Management/Views/MainWindow.Events.cs))
  и `PointerPressed`/`PointerReleased` (Avalonia, [`MainWindow.Avalonia.Events.cs`](Configuration%20Management/Views/MainWindow.Avalonia.Events.cs))
  пишутся **безусловно** — при любом клике, включая «пути без снимка» (обычный клик вне окна
  стабилизации, Ctrl/Shift-клик, промах мимо строки).
- `Deactivated`/`Activated` окна записываются **всегда** (прежде — только при pending-состоянии):
  число открытых меню и время с последнего закрытия меню дерева
  ([`MainWindow.xaml.cs`](Configuration%20Management/Views/MainWindow.xaml.cs) /
  [`MainWindow.Avalonia.cs`](Configuration%20Management/Views/MainWindow.Avalonia.cs)).
- На `MenuClosed` меню дерева дополнительно пишется `MenuClosedCursor`: координаты курсора,
  признак «курсор над строкой дерева» (hit-test), «над пунктом меню» и фокус окна — видно,
  ЧЕМ именно закрыто меню (кликом по строке / кликом мимо / выбором пункта / ESC).
- Стабилизация пишет стартовую запись `EnsureStableStart` (причина `snapshot`|`recentMenuClose`,
  целевая база, `SelectedInfobase`, `containerIsSelected`, `containerRealized`), а в каждый проход
  добавлены `SelectedInfobase`, `containerIsSelected`, `selectedByData`, `containerFound`
  ([`MainWindow.Tree.cs`](Configuration%20Management/Views/MainWindow.Tree.cs) /
  [`MainWindow.Avalonia.Events.cs`](Configuration%20Management/Views/MainWindow.Avalonia.Events.cs)).
- Лимит кругового усечения `trace.json` увеличен с 512 КБ до **1 МБ**
  ([`MenuCloseTraceFormat.MaxFileBytes`](Configuration%20Management/Services/MenuCloseTraceFormat.cs)) —
  трассировка стала плотнее.
- 5 новых тестов: единый формат строки клика WPF/Avalonia, форматирование модификаторов,
  `target=null` при промахе, новый лимит усечения, валидная JSON-сериализация полей события
  клика без секретов ([`BatchSelectionHelperTests.cs`](ConfigurationManagement.Tests/BatchSelectionHelperTests.cs),
  [`MenuCloseTraceFormatTests.cs`](ConfigurationManagement.Tests/MenuCloseTraceFormatTests.cs)).
- Поведение выбора/стабилизации НЕ менялось — правки только диагностические (B-1…B-7 плана).

**Подробности** — в [CHANGELOG.md](../../CHANGELOG.md).

### Файлы для установки

| Платформа | Файл | Контрольная сумма (SHA-256) |
|---|---|---|
| Windows (WPF, single-file) | `ConfigurationManagement.exe` | `0151a61de9cbf35bb98df2486b211bfc1f0f86d0af5fe1bbc95d006f5179c3da` |
| Linux (Avalonia, single-file) | `ConfigurationManagement` | `d58e54fbeac56bbf330cb5905b06a62aa5ffcfde449f1289b28cf0a09c559b28` |
| Linux (.deb) | `configuration-management_0.3.9.311_amd64.deb` | `013ac8ac4f302d8c8cde0726280bd44a54d5597dc0c0ebe51543910f73190c2d` |

Полный набор и `SHA256SUMS.txt` — в архиве ниже (attachments).