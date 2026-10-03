# Управление конфигурациями 1С — v0.3.9.299

Дата сборки: 2026-10-03. Версия в csproj: **0.3.9.299** (`Version`/`AssemblyVersion`/`FileVersion`/`InformationalVersion`).

## Исправлено

- **Свертка групп (issue #341)**: Ctrl+клик по «плюсику» группы теперь, как и по названию, сворачивает/разворачивает всю ветку на обеих платформах (WPF/Avalonia) — команда ветки применяется ровно один раз, текущая строка и выделение не меняются ([`MainWindow.Events.cs`](Configuration%20Management/Views/MainWindow.Events.cs)).

- **Снятие выделения после мультивыделения (issue #340)**: после мультивыделения, правого клика (контекстное меню) и левого клика по другой строке выбор строки больше не пропадает через мгновение — решение принимается по данным, а не по переиспользуемому контейнеру при `VirtualizationMode=Recycling`, повторная доставка того же `MouseDown` подавлена ([`BatchSelectionHelper.cs`](Configuration%20Management/Services/BatchSelectionHelper.cs), [`MainWindow.Hotkeys.cs`](Configuration%20Management/Views/MainWindow.Hotkeys.cs)).

- **Горизонтальный скрол (issue #309)**: горизонтальная прокрутка списка баз вынесена во внешний общий ScrollViewer (`DbListScroll`) — заголовок и дерево прокручиваются синхронно; вертикальная полоса — отдельным столбцом вне горизонтали, полоса целиком видна и докручивается до последних колонок; анти-регрессы #255/#343 сохранены ([`MainWindow.Scroll.cs`](Configuration%20Management/Views/MainWindow.Scroll.cs), [`MainWindow.Columns.cs`](Configuration%20Management/Views/MainWindow.Columns.cs)).

## Установка

**Windows:** распакуйте архив и запустите `ConfigurationManagement.exe` — self-contained single-file, .NET не требуется.

**Linux:** сделайте бинарник исполняемым и запустите:
`chmod +x ConfigurationManagement-linux-x64 && ./ConfigurationManagement-linux-x64`

или установите deb-пакет: `sudo dpkg -i configuration-management_0.3.9.299_amd64.deb`.