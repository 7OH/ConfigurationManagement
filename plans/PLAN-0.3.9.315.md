# PLAN 0.3.9.315 — Кластер C: #347 «Сказ о trace.json» (механизм отладочных флагов)

- Режим: **architect** — план; код НЕ изменяется до одобрения.
- Текущая версия: **0.3.9.312**. Целевая версия кластера: **0.3.9.315**.
- Основа: предложение 7OH (0 комментариев): файл `trace.json` рядом с настройками несёт флажки диагностики; пользователь включает флаг по наставлению из тикета; по умолчанию все флаги выключены; цель — перестать «капать» логи закрытых тикетов (ширина колонок) и управлять отладкой меню (#340) и редиректа (#323/#330/#334).

---

## 1. Дизайн

### 1.1 Файл-конфиг `trace.json` (новый формат)

Рядом с настройками (`PlatformPaths.AppDataDirectory`; Windows: `%APPDATA%\ConfigurationManagement\`, Linux: `~/.config/ConfigurationManagement/`) создаётся **JSON-объект** (не JSONL):

```json
{
  "version": 1,
  "CM_COLUMNS": false,
  "CM_MENUCLICK": false,
  "CM_MENUCLOSE": false,
  "CM_REDIRECT": false
}
```

- Все флаги по умолчанию `false`. Файл создаётся при каждом старте, если отсутствует.
- Неизвестные имена флагов игнорируются (устойчивость к будущим версиям); регистр имён безразличен.
- Документация флагов — в README и в комментарии к issue #347 (JSON комментарии не поддерживает).

### 1.2 Миграция существующего `trace.json`

Сегодня `trace.json` — JSONL-журнал (первая строка `{"ts":…`). При первом старте новой версии:
- если первая строка файла начинается с `{"ts":` — файл переименовывается в `trace_menuclose_legacy.json` (история диагностики сохраняется), затем создаётся новый `trace.json`-конфиг;
- legacy `menuclose_trace.json` (0.3.9.306) остаётся как есть (не трогаем).

### 1.3 Сервис флагов

Новый [`Configuration Management/Services/TraceFlags.cs`](../Configuration%20Management/Services/TraceFlags.cs) (статический, потокобезопасный, чистый парсер вынесен для тестов):
- `EnsureExists()` — создание файла с дефолтами (вызывается из `Program`/`App` при старте, обе платформы).
- `IsEnabled(string flag)` — чтение с кэшем: файл перечитывается, если изменился `LastWriteTime` (пользователь может включить флаг без перезапуска приложения).
- Повреждённый JSON → трактуется как «все флаги выключены» + запись предупреждения в общий лог приложения, файл не перезаписывается.
- Чистый парсер/форматтер — в `TraceFlagsFormat` (internal, без I/O), по образцу `MenuCloseTraceFormat`.

### 1.4 Интеграция

| Флаг | Что включает | Где |
|---|---|---|
| `CM_MENUCLOSE` | Журнал событий меню/кликов (текущий `MenuCloseTrace.Log`): MenuOpened/MenuClosed, MouseDown/MouseUp, PointerPressed/Released, TryApply/Fallback/EnsureStable/Dump500ms | `MenuCloseTrace.Log` — no-op при выключенном флаге; журнал переезжает в **`trace_menuclose.jsonl`** (чтобы не конфликтовать с конфигом). Startup-запись — только при включённом флаге. `EnsureStarted` всегда гарантирует наличие конфига `trace.json` |
| `CM_MENUCLICK` | Записи именно ПРАВОГО клика / открытия меню (по тексту #347) | Выделяется из `MenuCloseTrace` при необходимости (уточнение: на практике события MenuOpened уже покрывают правый клик — флаг оставить для будущей детализации, в этой версии допускается псевдоним на `CM_MENUCLOSE`) |
| `CM_COLUMNS` | Диагностика колонок (`LogColumnsDiagnostics`, стартовый дамп) вместо env `CM_COLUMNS_TRACE=1` | [`Views/MainWindow.Columns.cs`](../Configuration%20Management/Views/MainWindow.Columns.cs): чтение через `TraceFlags.IsEnabled("CM_COLUMNS")`; env-переменная остаётся только как override включения |
| `CM_REDIRECT` | INFO-диагностика CAS-входа/редиректов (`[Updates] Редирект …`, `Вход запущен/…`, инвентаризация cookie, POST-диагностика) | [`Services/OneCUpdatesService.cs`](../Configuration%20Management/Services/OneCUpdatesService.cs): INFO-записи диагностики — под флагом; WARN/ERROR (итоговые ошибки) пишутся ВСЕГДА. Аккуратно через локальный хелпер `LogRedirectInfo(msg)` |

### 1.5 README

Раздел про `trace.json` (соглашение 0.3.9.308) переписывается: файл теперь конфиг флагов; журнал меню — `trace_menuclose.jsonl` при `CM_MENUCLOSE=true`; таблица флагов с назначением.

---

## 2. Тесты

Новый файл [`ConfigurationManagement.Tests/TraceFlagsTests.cs`](../ConfigurationManagement.Tests/TraceFlagsTests.cs):
1. `Parse_MissingFile_ReturnsDefaultsAllFalse`
2. `Parse_JsonWithFlags_ReadsValues_IgnoreCase`
3. `Parse_BrokenJson_ReturnsDefaults_NoThrow`
4. `Parse_UnknownFlags_Ignored`
5. `Serialize_CreatesVersionedDefaults`
6. `Migrate_JsonlTrace_RenamesToLegacy` (первая строка `{"ts":` → решение о миграции)
7. `Migrate_ConfigJson_NotRenamed`
8. `Reload_OnMtimeChange_SeesNewValue`

Обновить [`ConfigurationManagement.Tests/MenuCloseTraceFormatTests.cs`](../ConfigurationManagement.Tests/MenuCloseTraceFormatTests.cs):
9. журнал меню пишется в `trace_menuclose.jsonl` (имя файла);
10. legacy `menuclose_trace.json` продолжает дописываться (миграция 0.3.9.306).

Регрессия: полный набор `dotnet test`; кросс-сборка Linux.

---

## 3. Файлы

| Файл | Изменение |
|---|---|
| [`Configuration Management/Services/TraceFlags.cs`](../Configuration%20Management/Services/TraceFlags.cs) | Новый сервис флагов (EnsureExists/IsEnabled) |
| `TraceFlagsFormat.cs` (в Services) | Чистый парсер/форматтер/миграция |
| [`Configuration Management/Services/MenuCloseTrace.cs`](../Configuration%20Management/Services/MenuCloseTrace.cs) | Гейт по `CM_MENUCLOSE`; журнал → `trace_menuclose.jsonl`; startup-запись под флагом |
| [`Configuration Management/Services/MenuCloseTraceFormat.cs`](../Configuration%20Management/Services/MenuCloseTraceFormat.cs) | Новое имя журнала; резолюция имени с учётом legacy |
| [`Configuration Management/Views/MainWindow.Columns.cs`](../Configuration%20Management/Views/MainWindow.Columns.cs) | `CM_COLUMNS` через TraceFlags (env — override) |
| [`Configuration Management/Services/OneCUpdatesService.cs`](../Configuration%20Management/Services/OneCUpdatesService.cs) | `CM_REDIRECT` для INFO-диагностики входа |
| `Configuration Management/Program.cs` (точка входа) | `TraceFlags.EnsureExists()` при старте |
| `README.md`, `CHANGELOG.md` | Документация флагов, запись о версии |
| `ConfigurationManagement.Tests/TraceFlagsTests.cs`, `MenuCloseTraceFormatTests.cs` | Новые/обновлённые тесты |
| [`Configuration Management/Configuration Management.csproj`](../Configuration%20Management/Configuration%20Management.csproj) | Версия → 0.3.9.315 (4 поля) |

---

## 4. Риски

| Риск | Влияние | Митигация |
|---|---|---|
| Смена формата `trace.json` (JSONL → JSON-конфиг) сломает привычку пользователя | Недоумение при отсутствии журнала | Миграция с сохранением истории; подробный комментарий в #347; README |
| После 0.3.9.315 диагностика меню выключена по умолчанию | Для новой итерации #340 нужно включать флаг | Пошаговая инструкция включения `CM_MENUCLOSE=true` в комментарии; перечитывание по mtime без перезапуска |
| Регресс #340/#323 из-за гейтов | Пропажа диагностики | INFO/WARN разделение: итоговые ошибки пишутся всегда; `CM_COLUMNS` не влияет на поведение колонок |
| Запись в файл при каждом клике (если флаг включён) | Нагрузка на диск | Прежний лимит 1 МБ с круговым усечением сохраняется |

---

## 5. Критерии приёмки

1. `dotnet test` зелёный; кросс-сборка Linux без ошибок.
2. При старте создаётся `trace.json`-конфиг с флагами `false`; старый JSONL-журнал переименован в `trace_menuclose_legacy.json`.
3. При `CM_MENUCLOSE=true` записи событий меню появляются в `trace_menuclose.jsonl`; при `false` — файл не растёт.
4. Диагностика колонок не пишется при `CM_COLUMNS=false` (по умолчанию) — жалоба «логи колонок капают» закрыта.
5. INFO-диагностика редиректов не засоряет общий журнал при `CM_REDIRECT=false`.
6. Релиз v0.3.9.315, комментарий в #347 после релиза (issue не закрывать).