Анализ функционала связывания базы с типовой конфигурацией (**код не менялся** — по вашему описанию «для начала просто нужно проанализировать, решение о действии примем сообща»).

## Карта использований

1. **Окно «Связать с конфигурацией»** ([`ConfigUpdateLinkWindow.xaml.cs`](Configuration%20Management/Views/ConfigUpdateLinkWindow.xaml.cs), Avalonia — [`ConfigUpdateLinkWindow.Avalonia.cs`](Configuration%20Management/Views/ConfigUpdateLinkWindow.Avalonia.cs)) — **единственная точка записи** полей явной связи `UpdateConfigCode` / `UpdateUrlOverride` / `UpdateUrlSegment` ([`Infobase.cs`](Configuration%20Management/Models/Infobase.cs)); связь применяется ко всем базам с тем же «Именем конфигурации».
2. **F9 / «Проверка обновлений» (#323)**: явная связь имеет **приоритет** над автоопределением; автоопределение — `ConfigTypeMatcher.FindByInfobaseName` (fallback по «Имени конфигурации», [`ConfigTypeMatcher.cs`](Configuration%20Management/Services/ConfigTypeMatcher.cs)).
3. **Кэш результатов** `UpdateCheckCache` индексируется по `UpdateConfigCode`; читается в `MaintenanceCenterViewModel.ResolveUpdateResult` ([`MaintenanceCenterViewModel.cs`](Configuration%20Management/ViewModels/MaintenanceCenterViewModel.cs)).
4. **«Актуальные релизы» (ALT+F9)** явную связь **не используют** ([`ActualReleasesViewModel.cs`](Configuration%20Management/ViewModels/ActualReleasesViewModel.cs)) — работают только по типовой конфигурации.
5. **Где явная связь — единственный способ** получить каталог: имя базы ≠ имени/подстроки типовой конфигурации; персональный ник (`UpdateUrlSegment`) или ручная ссылка (`UpdateUrlOverride`).

## Вывод

**Рекомендация: оставить как есть.** Автоопределение по «Имени конфигурации» уже покрывает основной сценарий (fallback в F9), а явная связь закрывает «крайние» случаи и является единственным механизмом персонального ника/ручной ссылки. Варианты «упростить» (убрать пункт меню) и «удалить» (отказ от `UpdateConfigCode` с миграцией настроек и кэша) несут риск регресса F9 и потери персональных ссылок — они оправданы только при явном подтверждении, что эти случаи вам не нужны.

## Вопросы (ответьте коротко — по ответам решим, нужна ли отдельная доработка)

1. Наблюдается ли на практике случай, когда F9 **не находит каталог** при заполненном «Имени конфигурации» (без явной связи)? Какой именно (имя базы, типовая конфигурация)?
2. Используются ли персональный ник базы / ручная ссылка (`UpdateUrlSegment` / `UpdateUrlOverride`) — заполняли ли вы их в окне «Связать с конфигурацией»?
3. Есть ли потребность в **пакетной привязке** нескольких баз к одной конфигурации помимо текущего механизма (привязка по одинаковому «Имени конфигурации»)?

Issue оставляю открытым — жду вашего решения.