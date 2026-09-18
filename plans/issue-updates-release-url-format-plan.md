# План: корректный протокол обновлений конфигураций 1С (releases.1c.ru)

## Диагноз (подтверждён живым тестом)

Версия `0.3.9.2` добавила настройки логина/пароля (HTTP Basic Auth). Живой тест показал:

- Ресурс `releases.1c.ru` при отсутствии сессии **редиректит на `login.1c.ru`**, а не выдаёт 401.
- Реализован и **работает** CAS-вход: GET формы → извлечь токен `execution` → POST `/login` →
  302 на `/user/profile`, сессионные cookie сохраняются (см. лог `[Updates] Вход на portal.1c.ru выполнен (status=302, location='/user/profile')`).
- Старый формат URL `downloads.1c.ru/ipp/.../Configs/...` даёт 404 — недействителен.
- Текущий парсер «жадно» извлекает числа из JSON `version_files` без `ver` → версии неверные.

## Реальный протокол (подтверждён HTML страницы project/<nick>)

### 1. Проверка последней версии
`GET https://releases.1c.ru/project/<nick>` (после авторизации) возвращает HTML со списком релизов:
- Таблица `#versionsTable` с колонками `Номер версии / Дата выхода / Обновление версии / Мин. версия платформы`.
- **Первая строка таблицы = последняя (самая новая) версия.**
- Ссылка в первой строке: `<a href="/version_files?nick=<nick>&ver=<версия>">3.0.206.19</a>`.
- Дополнительно есть ссылка `?allUpdates=true#updates` («Показать все обновления»).

### 2. Скачивание дистрибутива
`GET https://releases.1c.ru/version_files?nick=<nick>&ver=<версия>` → JSON со списком файлов
конкретного релиза (полный дистрибутив `*.zip`, `1cv8.cf`, `1Cv8.dt`, обновления `setup*.zip` и т.п.).

## Проблема текущей реализации

- [`BuildUpdateUrl`](Configuration Management/Services/OneCUpdatesService.cs:101) строит
  `version_files?nick=...` **без** `ver` для проверки — неверно. Для получения последней версии
  нужен `project/<nick>`.
- `ParseLatestVersionFromJson` слишком «жаден» — берёт любые 3–4-частные числа из тела.
- Окна (F9/ALT+F9) используют единый URL из `BuildUpdateUrl` для проверки.

## Цель

Реализовать корректный протокол. Версия релиза: **0.3.9.3**.

## Решение

### 1. Формирование URL проверки
В [`BuildUpdateUrl`](Configuration Management/Services/OneCUpdatesService.cs:101):
- если задан `Nick` → возвращать `https://releases.1c.ru/project/<Nick>` (HTML-список версий);
- если ник не задан → честный `Failed` с понятной ошибкой (старый downloads-путь убрать как нерабочий).

### 2. Парсинг последней версии из HTML project/<nick>
Добавить метод `ParseLatestVersionFromProjectHtml(html)`:
- найти блок `<table id="versionsTable" ...>` и внутри него **первую** строку `<tr>...<td class="versionColumn">...`;
- извлечь версию из первого `<a href="/version_files?nick=...&ver=...">`;
- вернуть эту версию.
- (Запасной вариант: если таблицы нет — регресс к поиску `version_files?...&ver=` в тексте.)

### 3. CheckForUpdatesAsync
- По URL определить тип: `project/` → HTML; `version_files` → JSON/HTML.
- Для `project/`: распарсить последнюю версию из HTML таблицы, сравнить с `currentVersion`.
- Для `version_files` (совместимость) — прежний гибкий парсер.

### 4. Скачивание дистрибутива
`DownloadUpdateAsync` должен принимать URL `version_files?nick=<nick>&ver=<версия>`, получать JSON
со списком файлов, выбирать дистрибутив (приоритет: `setup*.zip` / полный `*.zip` / `1cv8.cf`)
и скачивать его прямой ссылкой. Точную структуру JSON уточнить по реальному ответу.

### 5. Авторизация (уже работает)
Сохранить работающий CAS-вход (триггер по редиректу на `login.1c.ru`, GET формы + `execution` +
POST `/login`, CookieContainer). Basic Auth остаётся запасным (401/403).

### 6. Модель / ники
- `Nick` уже добавлен в `OneCConfigType` и заполнен в `BuiltInConfigTypes`.
- Уточнить/дополнить ники для ERP и БГУ (сейчас пустые → корректно показать, что URL не задан).

### 7. Версия, документация, сборка, релиз
- Поднять версию на `0.3.9.3`, обновить CHANGELOG/README, заметку `_release/0.3.9.3.md`.
- Собрать single-file Windows и Linux.
- Коммит, тег `v0.3.9.3`, релиз на GitHub.

## Декомпозиция

1. Внедрить протокол: BuildUpdateUrl→project, парсер последней версии из HTML, DownloadUpdateAsync
   через version_files (получение JSON→выбор файла→скачивание).
2. Собрать exe, протестировать на реальных каталогах (низкоточные ники скорректировать).
3. Версия 0.3.9.3 + CHANGELOG/README/_release.
4. Сборка обеих платформ.
5. Push, тег, релиз.