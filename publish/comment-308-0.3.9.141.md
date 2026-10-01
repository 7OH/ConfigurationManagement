Исправлено в версии **0.3.9.141** (Windows/WPF и Linux/Avalonia) — три замечания по «Сценариям» (часть 1).

**1. Окно редактора сценария — по содержимому (без скролла).**
Фиксированная высота заменена на авторазмер: `SizeToContent = Height` (как в окне
создания ИБ, 0.3.9.113) — окно подстраивается под контент, внутренний скролл не
появляется; `MaxHeight = 800` ограничивает рост на низких экранах.
Windows/WPF — [`Views/ScriptScenarioEditWindow.xaml`](Configuration%20Management/Views/ScriptScenarioEditWindow.xaml),
Linux/Avalonia — [`Views/ScriptScenarioEditWindow.Avalonia.cs`](Configuration%20Management/Views/ScriptScenarioEditWindow.Avalonia.cs).

**2. Комбобокс «База для примера подстановок» снова показывает имя базы (регресс).**
На Linux/Avalonia вместо имени отображался `ToString()` элемента. ItemTemplate
упрощён до `TextBlock` с `Infobase.Name` (цвет наследуется из темы окна — по образцу
`MetadataExplorerWindow`), а первая база выбирается по индексу (`SelectedIndex = 0`),
как в WPF-версии и `ConfigDiffSetupWindow`. Теперь и в списке, и в закрытом комбобоксе
видно имя базы, превью командной строки соответствует ей же.

**3. «Скрывать окно скрипта»: при снятой галке окно PowerShell/cmd появляется.**
Причина: `ExternalCommandRunner` всегда использовал `UseShellExecute = false`, а
GUI-процесс без консоли с таким флагом окно не создаёт даже при `CreateNoWindow = false`.
Для видимого окна на Windows теперь включается `UseShellExecute = true` (запуск через
shell с новым окном) — см.
[`Services/ExternalCommandRunner.cs`](Configuration%20Management/Services/ExternalCommandRunner.cs).
Поведение по умолчанию не изменилось: pre/post-команды баз и CLI остаются с
`createNoWindow = true` и `UseShellExecute = false`; на Linux флаг не используется
(окно там зависит от окружения).

**Как проверить.**

1. Установите версию **0.3.9.141** (Windows или Linux).
2. «Утилиты» → «Настройка сценариев» → «Добавить»: окно редактора открывается по
   содержимому, вертикального скролла в нём нет (на низких экранах скролл появится
   только при упоре в `MaxHeight`).
3. В комбобоксе «База для примера подстановок» сразу выбранная база отображается по
   имени (не по ключу/типу), превью командной строки соответствует ей.
4. Снимите галку «Скрывать окно скрипта», сохраните сценарий и запустите его (F5) на
   Windows — появится видимое окно `cmd`/PowerShell; при установленной галке окно скрыто,
   как и раньше (post/pre-команды баз не изменились).