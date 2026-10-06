# Управление конфигурациями 1С — v0.3.9.301

Дата сборки: 2026-10-04. Версия в csproj: **0.3.9.301** (`Version`/`AssemblyVersion`/`FileVersion`/`InformationalVersion`).

## Исправлено

- **CAS-вход на portal.1c.ru ([#334](https://github.com/sivatorov/ConfigurationManagement/issues/334))**: POST формы входа больше не собирается из жёстко захардкоженного списка полей — `ExtractFormFields` берёт все скрытые поля фактической формы (`execution`, `lt` и пр.) плюс отмеченные чекбоксы, поэтому изменение формы портала не ломает вход. На запросы входа принудительно включён **HTTP/1.1** и добавлены заголовки **Referer/Origin**. При 401 читается тело ответа и в журнал пишутся только **анонимизированные признаки причины** (размер HTML, маркеры, имена полей) — логин, пароль и токены не логируются. Вместо «отравляющего» флага — **retry-политика**: счётчик `_portalLoginAttempts` с лимитом 3 попыток за сессию службы и сбросом при смене учётной записи. Статусы авторизации разделены: **AuthFailed** (креды не приняты) / **AuthRequired** (креды не настроены) / **NetworkError** (сеть); новые ключи локализации `Updates.AuthFailed` и `PlatformUpdate.Error.AuthFailed` ([`OneCUpdatesService.cs`](Configuration%20Management/Services/OneCUpdatesService.cs), [`ru.json`](Configuration%20Management/Localization/Languages/ru.json), [`en.json`](Configuration%20Management/Localization/Languages/en.json)).

- **Окно «Скачивание версии платформы 1С» ([#330](https://github.com/sivatorov/ConfigurationManagement/issues/330))**: каталог версий снова загружается — общий корень с #334/#323: исправлен программный вход (динамические поля формы, HTTP/1.1, Referer/Origin), retry вместо блокировки сессии, понятная диагностика 401 вместо голого `PlatformUpdate.Error.NetworkError`.

- **«Проверка обновлений» F9 ([#323](https://github.com/sivatorov/ConfigurationManagement/issues/323))**: редирект **302 без заголовка Location** (поведение CAS Spring Security при отсутствии сессии) теперь трактуется как требование авторизации и запускает программный вход на portal.1c.ru; при неудаче входа показывается понятное сообщение (`Updates.AuthFailed`), а не техническое «HTTP 302 … requestUri=<тот же>». После неудачной проверки следующее нажатие F9 может попробовать войти снова (лимит попыток, не блокировка на всю сессию).

- **Каталог платформы Platform85**: ник каталога параметризован — `Platform83Nick`/`Platform85Nick`, `SupportedPlatformNicks = [Platform83, Platform85]`; `GetAvailableReleasesAsync(nick, …)` позволяет проверять `https://releases.1c.ru/project/Platform85` (по умолчанию остаётся Platform83) ([`OneCPlatformCatalogParser.cs`](Configuration%20Management/Services/OneCPlatformCatalogParser.cs), [`PlatformUpdateService.cs`](Configuration%20Management/Services/PlatformUpdateService.cs), [`IPlatformUpdateService.cs`](Configuration%20Management/Services/IPlatformUpdateService.cs)).

- **Анализ связывания базы с типовой конфигурацией ([#345](https://github.com/sivatorov/ConfigurationManagement/issues/345))**: проведён анализ функционала (карта использований, вывод, вопросы), код не менялся — подробности в issue.

**Тесты.** Новый [`OneCUpdatesLoginFlowTests.cs`](ConfigurationManagement.Tests/OneCUpdatesLoginFlowTests.cs) — 12 сценариев CAS-входа; дополнены [`PlatformUpdateServiceTests.cs`](ConfigurationManagement.Tests/PlatformUpdateServiceTests.cs), [`PlatformUpdateViewModelTests.cs`](ConfigurationManagement.Tests/PlatformUpdateViewModelTests.cs), [`PlatformDownloadViewModelTests.cs`](ConfigurationManagement.Tests/PlatformDownloadViewModelTests.cs), [`OneCPlatformCatalogParserTests.cs`](ConfigurationManagement.Tests/OneCPlatformCatalogParserTests.cs). Полный набор `dotnet test` зелёный: **Windows 1701**, **Linux 1672**.

## Установка

**Windows:** запустите `ConfigurationManagement.exe` — self-contained single-file, .NET не требуется.

**Linux:** сделайте бинарник исполняемым и запустите:
`chmod +x ConfigurationManagement-linux-x64 && ./ConfigurationManagement-linux-x64`

или установите deb-пакет: `sudo dpkg -i configuration-management_0.3.9.301_amd64.deb`.

## Скачать

- Windows (self-contained single-file): [ConfigurationManagement.exe](https://github.com/sivatorov/ConfigurationManagement/releases/download/v0.3.9.301/ConfigurationManagement.exe)
- Linux x64 (single-file): [ConfigurationManagement-linux-x64](https://github.com/sivatorov/ConfigurationManagement/releases/download/v0.3.9.301/ConfigurationManagement-linux-x64)
- Debian/Ubuntu: [configuration-management_0.3.9.301_amd64.deb](https://github.com/sivatorov/ConfigurationManagement/releases/download/v0.3.9.301/configuration-management_0.3.9.301_amd64.deb)
- Контрольные суммы: [SHA256SUMS.txt](https://github.com/sivatorov/ConfigurationManagement/releases/download/v0.3.9.301/SHA256SUMS.txt)

### SHA-256

```
d4d62a6ff1e0611da1e6fd0b65ac28b0922e2312fa14fe0c840b993cd7bb654d  ConfigurationManagement.exe
07d924e694bbdd98eed4300363590bf2c2a6f52342f5b0ec62ac473839f406af  ConfigurationManagement-linux-x64
c4e130d636a8e5c70d8724ef2f8444b245d6c41bd2429708552ecf016e8d2de1  configuration-management_0.3.9.301_amd64.deb