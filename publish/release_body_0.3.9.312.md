## 0.3.9.312 — группа «Привязка» в свойствах базы (#346)

**Добавлено:**

- **Группа «Привязка» в свойствах базы (issue #346, кластер C)** — на вкладке «Платформа» под
  группой «Версия платформы и параметры» теперь видна текущая привязка базы к типовой
  конфигурации: наименование типовой, редакция (определяется по номеру релиза из свойств базы)
  и адрес каталога релизов (раньше эта информация нигде не отображалась):
- Кнопка **«Связать…»** открывает существующее окно «Связать с конфигурацией» для повторной
  привязки; поля связи пишутся в базу и репозиторий сразу, как из контекстного меню (issue #322).
- Кнопка **«Очистить»** (с подтверждением) сбрасывает код типовой конфигурации, ручную ссылку
  и персональный сегмент; для новой базы сброс сохраняется штатно через свойства базы
  ([`ConnectionSettingsViewModel.cs`](Configuration%20Management/ViewModels/ConnectionSettingsViewModel.cs)).
- Реализовано симметрично в WPF и Avalonia, все тексты локализованы ru/en
  ([`ConnectionSettingsWindow.xaml`](Configuration%20Management/Views/ConnectionSettingsWindow.xaml),
  [`ConnectionSettingsWindow.Avalonia.cs`](Configuration%20Management/Views/ConnectionSettingsWindow.Avalonia.cs)).
- Общий helper сохранения связи
  [`InfobaseLinkStorage`](Configuration%20Management/Services/InfobaseLinkStorage.cs) (логика
  PersistLink окна связи без изменения его контракта).
- Новый чистый метод [`FindByCode`](Configuration%20Management/Services/ConfigTypeMatcher.cs) —
  поиск типовой конфигурации по коду связи (регистронезависим).
- 11 новых тестов: перенос полей привязки в LoadFrom/ApplyTo (включая сброс в пустые строки),
  BuildLinkSummary (формат/без URL/ручная ссылка), HasLink и RefreshLinkState, очистка связи,
  FindByCode (регистронезависимость и null-кейсы)
  ([`ConnectionSettingsViewModelTests.cs`](ConfigurationManagement.Tests/ConnectionSettingsViewModelTests.cs),
  [`ConfigTypeMatcherTests.cs`](ConfigurationManagement.Tests/ConfigTypeMatcherTests.cs)).

**Подробности** — в [CHANGELOG.md](../../CHANGELOG.md).

### Файлы для установки

| Платформа | Файл | Контрольная сумма (SHA-256) |
|---|---|---|
| Windows (WPF, single-file) | `ConfigurationManagement.exe` | `5963c250cb8e94555594d02b1b11e93aa305d0ddc67acdfc769a00e12fb14f7d` |
| Linux (Avalonia, single-file) | `ConfigurationManagement` | `e3f50aa6fe1f3312cf352062d8905b17ca782394947eb332536b1cae86261f83` |
| Linux (.deb) | `configuration-management_0.3.9.312_amd64.deb` | `628cdecf7dd292628be96f28d644599169f58de6d01b492f29afab1266a18a8a` |

Полный набор и `SHA256SUMS.txt` — в архиве ниже (attachments).