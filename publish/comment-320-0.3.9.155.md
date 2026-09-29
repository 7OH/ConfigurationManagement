Сделано в версии **0.3.9.155** (Windows/WPF и Linux/Avalonia) — новые файлы конфигурации сценариев теперь сохраняются с читаемым UTF-8: русские буквы записываются как есть (например, `"Name": "Тест 2"`), а не как `\u0422\u0435\u0441\u0442 2`.

**Причина.** `ScriptScenarioStore`, `BackupScenarioStore` и `ScheduledTaskStore` создавали `JsonSerializerOptions` без `Encoder`, а System.Text.Json по умолчанию экранирует все не-ASCII символы в `\uXXXX`-последовательности. В остальных JSON-хранилищах (`InfobaseRepository`, `ProfileService`, `InfobaseJsonTransfer`) нужный `Encoder` уже был задан — их не трогали.

**Что изменено.** Во всех трёх хранилищах задан `Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping`:
- [`Services/ScriptScenarioStore.cs`](Configuration%20Management/Services/ScriptScenarioStore.cs) — сценарии запуска скриптов (`scripts\scenarios\*.script.json`);
- [`Services/BackupScenarioStore.cs`](Configuration%20Management/Services/BackupScenarioStore.cs) — сценарии резервирования (`backups\scenarios\*.scenario.json`);
- [`Services/ScheduledTaskStore.cs`](Configuration%20Management/Services/ScheduledTaskStore.cs) — задания по расписанию (`schedules\*.task.json`).

Чтение не изменилось: старые файлы с `\uXXXX` по-прежнему читаются корректно (System.Text.Json разбирает escapes). Для изоляции тестов в `BackupScenarioStore` и `ScheduledTaskStore` добавлен необязательный параметр `directoryOverride` (как в `ScriptScenarioStore`). Добавлены тесты на читаемый UTF-8 и обратную совместимость (`ScriptScenarioStoreTests`, `BackupScenarioStoreTests`, `ScheduledTaskStoreTests`); весь набор — 703/703 зелёный, Linux-сборка (`dotnet build -p:BuildLinux=true`) без ошибок.

**Как проверить.** Установите версию **0.3.9.155**, создайте сценарий с именем «Тест 2» и русскими параметрами; откройте `<DataDir>\scripts\scenarios\*.script.json` — русский текст без `\u`. То же для сценариев резервирования и заданий по расписанию. Перезапустите приложение — сценарии загружаются, имена корректны.