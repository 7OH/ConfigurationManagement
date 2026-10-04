Исправлено в версии **0.3.9.300**.

**Что было.**

1. В предупреждении о различии версий платформы не указывался порт: сравнение серверов ([`SameServer`](Configuration%20Management/Services/CreateInfobaseService.cs)) выполнялось без учёта порта, и поиск несовместимой версии находил базы на «localhost» независимо от порта — было непонятно, про какой именно сервер идёт речь.
2. «Сервер СУБД» не подставлялся при повторном открытии окна создания базы: цепочка «сохранение при создании → восстановление при открытии» на практике терялась.

**Что сделано.**

1. [`Services/CreateInfobaseService.cs`](Configuration%20Management/Services/CreateInfobaseService.cs): `SameServer(a, b)` теперь учитывает порт — если у обеих сторон порт задан явно и отличается, серверы считаются разными (`localhost:1541` ≠ `localhost:1545`); если порт не задан хотя бы у одной стороны — fallback «равны» (`localhost` ≡ `localhost:1541`). Адрес разбирается через `ParseServerPort`.
2. Поиск несовместимой версии использует новое сравнение и возвращает полный адрес найденной базы — текст предупреждения теперь содержит адрес с портом: «базы на сервере localhost:1541 работают с версией …».
3. Восстановлена цепочка сохранения/восстановления «Сервера СУБД» (`SaveLastDbServer` → `AppSettings.LastDbServer/LastDbPort` → `RestoreLastDbServer` при открытии окна, WPF и Avalonia).

**Тесты** ([`CreateInfobaseDbServerStringTests.cs`](ConfigurationManagement.Tests/CreateInfobaseDbServerStringTests.cs)): `SameServer_WithPorts_EqualWhenNoPortSpecified`, `SameServer_WithDifferentPorts_NotEqual`, `SameServer_WithSamePort_Equal`.

**Как проверить:** обновитесь до **0.3.9.300**; создайте клиент-серверную базу и откройте окно создания повторно — «Сервер СУБД» подставлен; при серверах с разными портами предупреждение показывает адрес с портом.