Исправлено в версии **0.3.9.279** (Windows/WPF; Linux/Avalonia не затронута — там привязка уже была `Mode=OneWay`).

**Что было.**

При открытии окна «Скачивание версии платформы 1С» появлялась ошибка:

```
System.InvalidOperationException: Привязка типа TwoWay или OneWayToSource не может работать
с доступным только для чтения свойством "LogText"
```

**Причина.**

В [`Views/PlatformDownloadWindow.xaml`](Configuration%20Management/Views/PlatformDownloadWindow.xaml) поле журнала `LogBox` (read-only `TextBox`) привязано к свойству `PlatformDownloadViewModel.LogText` БЕЗ указания режима: `Text="{Binding LogText}"`. WPF по умолчанию строит двустороннюю привязку, а свойство `LogText` доступно только для чтения — при открытии окна привязка валидировалась и роняла его. В `PlatformUpdateWindow.xaml` режим `Mode=OneWay` уже был задан (это чинилось в 0.3.9.260), а здесь — нет.

**Как исправлено.**

Явный режим: `Text="{Binding LogText, Mode=OneWay}"`. Проверены остальные окна с read-only текстовыми полями журналов — все они либо уже используют `Mode=OneWay`, либо выводят текст через `TextBlock` (односторонние по умолчанию), поэтому других правок не потребовалось.

**Тесты.**

Полный набор `dotnet test` зелёный, кросс-сборка Linux (`dotnet build -p:BuildLinux=true`) без ошибок.

**Как проверить:**

1. Установите **0.3.9.279** ([релиз v0.3.9.279](https://github.com/sivatorov/ConfigurationManagement/releases/tag/v0.3.9.279)).
2. Откройте «Утилиты → Скачивание версии платформы 1С» несколько раз подряд: окно открывается, журнал прокручивается, падений нет.
3. То же для «Обновление платформы 1С» (F9/Ctrl+F9).