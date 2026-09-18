# План: применение открытых PR и выпуск нового релиза (Windows + Linux)

> Репозиторий: `https://github.com/sivatorov/ConfigurationManagement`
> Локальный путь: `f:/ya/Yandex.Disk/h/Configuration_Management`
> Терминал Windows — **cmd.exe**. Все команды ниже даны в синтаксисе cmd.
> Выполнять рекомендуется **в режиме Code** через отдельные `new_task` (по одной задаче на шаг).

---

## 1. Цель

1. Подключиться к GitHub, получить список **всех открытых** pull request (закрытые/merged не трогать).
2. Для каждого открытого PR определить возможность чисто применить без конфликтов:
   - нет конфликтов → применить (merge);
   - есть конфликты → зафиксировать в плане, **НЕ применять**.
3. После применения всех PR обновить версию приложения и описать изменения в `CHANGELOG.md`, `README.md` и заметке `_release/<версия>.md`.
4. Собрать исполняемые файлы для **Windows (win-x64)** и **Linux (linux-x64)**.
5. Выложить изменения на GitHub и создать **новый релиз**.

Решение по версионированию (согласовано): **все применённые PR агрегируются в ОДИН релиз** — единый финальный коммит с поднятой версией, один раздел в CHANGELOG, один тег.

---

## 2. Контекст и факты, подтверждённые проверкой

### 2.1 Версии и теги
- Версия в [`Configuration Management.csproj`](../Configuration%20Management/Configuration%20Management.csproj) на main: **`0.3.8.21`** (поля `Version`, `AssemblyVersion`, `FileVersion`, `InformationalVersion`).
- `main` чист, синхронизирован с `origin/main` (`git status -sb` → `## main...origin/main`).
- Существующие теги `v0.3.8.*`: только `v0.3.8.9`, `v0.3.8.10`, `v0.3.8.20`. **Тега `v0.3.8.21` нет** — текущая версия ещё не релизилась.
- Тег **`v0.3.9.0` существует**, но является **устаревшим**: указывает на предка main (`73374d1`), где версия была поднята до `0.3.9.0`, а затем откачена/перенумерована (`84c33d3 chore: перенумеровать версию с 0.3.9.0 на 0.3.8.10`). **Не переиспользовать** `v0.3.9.0`.

> Итог: следующая версия — **`0.3.8.22`**, тег — **`v0.3.8.22`**.

### 2.2 Открытые pull request (на момент планирования)
Список получен командой:
```cmd
gh pr list --repo sivatorov/ConfigurationManagement --state open --limit 100
```
Открытых PR — **два**, оба от `ksv47`, оба в ветку `main`, оба по Linux/Avalonia:

| PR | Заголовок | mergeable | Изменённые файлы |
|----|-----------|-----------|------------------|
| **#258** | Linux: ручная проверка обновлений ставила версию без вопроса о согласии | MERGEABLE / CLEAN | `Configuration Management/App.axaml.cs`, `Services/UpdateService.Avalonia.cs`, `ViewModels/MainViewModel.Avalonia.cs` |
| **#259** | Linux: кнопки раздела «Базы» в настройках по разметке WPF, две возможности были недоступны | MERGEABLE / CLEAN | `Configuration Management/Views/SettingsWindow.Avalonia.cs` |

- Файлы #258 и #259 **не пересекаются** → конфликта между PR нет.
- Все затронутые файлы — **только Linux/Avalonia**, Windows/WPF-файлы (`App.xaml.cs`, `UpdateService.cs` и др.) не изменяются, хотя дефект #258 на Windows симметричен (в описании явно указано: WPF-сторону не трогаем).
- На момент первого запроса #258 был `mergeable=UNKNOWN` (CI ещё не отработал), затем стал `CLEAN`. → перед merge обязательно **перепроверить** статус.

### 2.3 Сборка
- Сборка single-file задаётся в csproj (RID зависит от ОС; Linux из Windows включается свойством `-p:ForceLinux=true`).
- Скрипты:
  - Windows: [`Configuration Management/build-windows-single-file.ps1`](../Configuration%20Management/build-windows-single-file.ps1) → `dist/win-x64/ConfigurationManagement.exe`
  - Linux из Windows (кросс-компиляция): [`Configuration Management/build-linux-single-file.ps1`](../Configuration%20Management/build-linux-single-file.ps1) → `dist/linux-x64/ConfigurationManagement`
- Папка `dist/` **в `.gitignore`** (`dist/`) → бинарники в git не коммитятся, это корректно.
- CI: [`.github/workflows/build.yml`](../.github/workflows/build.yml) собирает обе цели на push в `main` и на каждый PR; [`.github/workflows/release.yml`](../.github/workflows/release.yml) срабатывает на пуш тега, сам собирает **Linux**-ассет и прикрепляет к релизу (overwrite). **Windows-ассет публикуется вручную** через `gh release upload`.

---

## 3. Правила обновления версии

1. **Формула бампа:** поднять **4-й числовой компонент** (микро-версию) текущей версии: `0.3.8.21` → `0.3.8.22`.
2. Изменить **все четыре поля** в [`Configuration Management.csproj`](../Configuration%20Management/Configuration%20Management.csproj):
   `<Version>`, `<AssemblyVersion>`, `<FileVersion>`, `<InformationalVersion>`.
3. Обновить бейдж версии в [`README.md`](../README.md) (строка 3, `Версия-0.3.8.22`).
4. Добавить **новый раздел** в начало [`CHANGELOG.md`](../CHANGELOG.md) по формату Keep a Changelog:
   ```markdown
   ## [0.3.8.22] — <ГГГГ-ММ-ДД>

   ### Исправления

   - <описание #258 ...>
   - <описание #259 ...>

   ### Версия

   - **Версия поднята до `0.3.8.22`** во всех четырёх полях `<Version>`, `<AssemblyVersion>`, `<FileVersion>`, `<InformationalVersion>` в [`Configuration Management.csproj`](Configuration%20Management/Configuration%20Management.csproj).
   ```
5. Создать заметку [`_release/0.3.8.22.md`](../_release/0.3.8.22.md) в стиле существующих (см. [`_release/0.3.8.21.md`](../_release/0.3.8.21.md)): заголовок версии, разделы «Что исправлено», «Исполняемые файлы» (Windows x64 / Linux x64), «Сборка обеих платформ».
6. Всё это — в **одном** release-коммите (после merge обоих PR).

---

## 4. Общий порядок выполнения

```
[Задача A] Preflight (git+gh, доступность)
   ↓
[Задача B] Получить открытые PR, проверить конфликты
   ↓
[Задача C] Применить PR без конфликтов (merge)
   ↓
[Задача D] Версия 0.3.8.22 + CHANGELOG + README + _release
   ↓
[Задача E] Сборка Windows (win-x64)
   ↓
[Задача F] Сборка Linux (linux-x64, кросс из Windows)
   ↓
[Задача G] Пуш, тег v0.3.8.22, создание релиза, загрузка ассетов
```

---

## 5. Декомпозиция на задачи (каждая — через `new_task` в режиме Code)

### Задача A — Preflight: доступность GitHub и инструментов
**Команды:**
```cmd
gh auth status
gh repo view sivatorov/ConfigurationManagement --json defaultBranchRef,name --template "{{.name}} {{.defaultBranchRef.name}}{{println}}"
dotnet --version
git fetch origin --tags --prune
git status -sb
```
**Критерий готовности:** gh авторизован (аккаунт `sivatorov`, GH_TOKEN), `dotnet` доступен, `main` чист и на `origin/main`.

---

### Задача B — Получить все открытые PR и оценить конфликты
**Шаг B.1. Список открытых PR:**
```cmd
gh pr list --repo sivatorov/ConfigurationManagement --state open --limit 100 --json number,title,headRefName,baseRefName,isDraft,mergeable,mergeStateStatus,createdAt,author --template "{{range .}}PR#{{.number}} draft={{.isDraft}} mergeable={{.mergeable}} status={{.mergeStateStatus}} author={{.author.login}}{{println}}  {{.title}}{{println}}  head={{.headRefName}} -> base={{.baseRefName}}{{println}}{{end}}"
```
> Закрытые/merged игнорировать (их здесь нет; при появлении — пропускать).

**Шаг B.2. Проверка возможности чисто применить (без конфликтов):**
Для каждого открытого PR:
```cmd
gh pr view <N> --repo sivatorov/ConfigurationManagement --json mergeable,mergeStateStatus,files --template "mergeable={{.mergeable}} status={{.mergeStateStatus}}{{println}}"
gh pr diff --name-only --repo sivatorov/ConfigurationManagement <N>
```
Локальная «тестовая» проверка слияния (надёжнее, чем поле `mergeable`):
```cmd
git checkout main
git pull --ff-only origin main
git fetch origin pull/<N>/head:pr-<N>
git checkout -b test-merge-pr-<N>
git merge --no-commit --no-ff pr-<N>
git merge --abort   % откатить тест
git checkout main
git branch -D test-merge-pr-<N>
```
- Если merge прошёл без конфликтов → PR помечается как «можно применять».
- Если конфликт → **зафиксировать** (номер PR, затронутые файлы, причина) в этом плане и **НЕ применять**; перейти к следующему.

**Результат:** итоговый список применимых PR и список отложенных из-за конфликтов. На момент планирования: **оба (#258, #259) применимы**.

---

### Задача C — Применить PR без конфликтов (merge)
Перед каждым merge перепроверить `mergeable=CLEAN` (для #258 из-за раннего `UNKNOWN`).
```cmd
git checkout main
git pull --ff-only origin main
gh pr merge 258 --repo sivatorov/ConfigurationManagement --squash --delete-branch
gh pr merge 259 --repo sivatorov/ConfigurationManagement --squash --delete-branch
git pull --ff-only origin main
git log --oneline -5
```
- Стратегия — **squash** (по одному коммиту на PR).
- Порядок значения не имеет (файлы не пересекаются); при появлении общих файлов — мёржить по возрастанию номера.
- После merge убедиться, что CI `build.yml` на `main` зелёный (опционально):
```cmd
gh run list --repo sivatorov/ConfigurationManagement --branch main --limit 3
```
- Если какой-то PR дал конфликт при merge (неожиданно) — **не форсить**, зафиксировать и продолжить с остальными.

---

### Задача D — Версия + CHANGELOG + README + заметка релиза
Объединить всё в один коммит версии `0.3.8.22`.

**D.1. Поднять версию в csproj** (`0.3.8.21` → `0.3.8.22`) во всех четырёх полях:
```cmd
notepad "Configuration Management\Configuration Management.csproj"
```
**D.2. README** — бейдж версии в строке 3: `Версия-0.3.8.22`.
**D.3. CHANGELOG** — новый раздел `[0.3.8.22]` сверху (см. §3, пункты 4).
**D.4. Заметка `_release/0.3.8.22.md`** — по образцу `_release/0.3.8.21.md`.
**D.5. Коммит:**
```cmd
git add -A
git commit -m "Версия 0.3.8.22: объединение открытых PR #258, #259 (Linux)"
```
> Контроль: не закоммитить `dist/` (он в .gitignore).

---

### Задача E — Сборка Windows (win-x64)
```cmd
cd "Configuration Management"
.\build-windows-single-file.ps1
```
Ожидаемый результат: `Configuration Management/dist/win-x64/ConfigurationManagement.exe` (один файл, self-contained).

---

### Задача F — Сборка Linux (linux-x64) кросс-компиляцией из Windows
```cmd
cd "Configuration Management"
.\build-linux-single-file.ps1
```
Ожидаемый результат: `Configuration Management/dist/linux-x64/ConfigurationManagement` (один файл).
> **ВНИМАНИЕ:** бинарник собран на Windows для запуска на Linux. Проверить его работу локально нельзя (нет Linux-рантайма). Для гарантии качества Linux-сборку также пересобирает CI (`release.yml` на реальном `ubuntu-latest`).

**Опционально — регрессионные тесты:**
```cmd
dotnet test "ConfigurationManagement.Tests\ConfigurationManagement.Tests.csproj" -c Release -p:RuntimeIdentifier= -p:SelfContained=false -p:PublishSingleFile=false
```

---

### Задача G — Публикация и создание релиза v0.3.8.22

**Шаг G.1. Пуш изменений main:**
```cmd
git push origin main
```
**Шаг G.2. Создание тега и релиза + загрузка ассетов (рекомендуемый детерминированный способ):**
```cmd
git tag v0.3.8.22
git push origin v0.3.8.22
```
После пуша тега `release.yml` сам соберёт Linux-ассет и создаст/обновит релиз. Затем задать описание и добавить Windows-ассет:
```cmd
gh release edit v0.3.8.22 --repo sivatorov/ConfigurationManagement --title "Управление конфигурациями 1С 0.3.8.22" --notes-file "_release\0.3.8.22.md"
gh release upload v0.3.8.22 --repo sivatorov/ConfigurationManagement "Configuration Management\dist\win-x64\ConfigurationManagement.exe" --clobber
```
*(Windows-ассет публикуется вручную; Linux-ассет `ConfigurationManagement-linux-x64` добавит workflow.)*

**Альтернатива — создать релиз сразу с обоими локальными ассетами** (если не хотим ждать CI):
```cmd
git push origin main
gh release create v0.3.8.22 --repo sivatorov/ConfigurationManagement --title "Управление конфигурациями 1С 0.3.8.22" --notes-file "_release\0.3.8.22.md" --target main "Configuration Management\dist\win-x64\ConfigurationManagement.exe" "Configuration Management\dist\linux-x64\ConfigurationManagement"
```
> При этом тег создаётся автоматически; `release.yml` после этого дополнительно пересоберёт Linux-ассет (overwrite одноимённого — безвредно). Не использовать тег `v0.3.9.0`.

**Шаг G.3. Контроль:**
```cmd
gh release view v0.3.8.22 --repo sivatorov/ConfigurationManagement --json name,assets --template "{{.name}}:{{println}}{{range .assets}}{{.name}} ({{.size}}B){{println}}{{end}}"
```

---

## 6. Сводка ключевых команд

| Шаг | Команда |
|-----|---------|
| Список открытых PR | `gh pr list --repo sivatorov/ConfigurationManagement --state open` |
| Проверка mergeable | `gh pr view <N> --repo sivatorov/ConfigurationManagement --json mergeable,mergeStateStatus` |
| Применить PR | `gh pr merge <N> --repo sivatorov/ConfigurationManagement --squash --delete-branch` |
| Сборка Windows | `.\build-windows-single-file.ps1` (в `Configuration Management`) |
| Сборка Linux | `.\build-linux-single-file.ps1` (в `Configuration Management`) |
| Тест-мёрж (конфликты) | `git fetch origin pull/<N>/head:pr-<N> && git merge --no-commit --no-ff pr-<N>` |
| Тег | `git tag v0.3.8.22 && git push origin v0.3.8.22` |
| Релиз | `gh release create/upload/edit v0.3.8.22 --repo sivatorov/ConfigurationManagement ...` |

---

## 7. Риски и митигация

| Риск | Митигация |
|------|-----------|
| **Недоступность GitHub / сбой авторизации** | `gh auth status` заранее; GH_TOKEN задан; при ошибке — повторить; обходные пути к GH не требуются. |
| **`mergeable=UNKNOWN` (не успел пройти CI)** | Перед merge перепроверять статус; ждать завершения проверок (#258 именно так менялся UNKNOWN→CLEAN). |
| **Конфликты при merge** | Тест-мёрж локально; при конфликте — зафиксировать, НЕ применять, не форсить. |
| **Конфликт версий/тегов** | Устаревший тег `v0.3.9.0` не переиспользовать; всегда `v0.3.8.22`. |
| **Сборка Linux на Windows** | Библиотеки Linux не установлены → можно только скомпилировать, не запустить. Гарантия качества — `release.yml` на `ubuntu-latest` (пересобирает Linux-ассет). При необходимости — WSL. |
| **Случайный коммит бинарников** | `dist/` в `.gitignore`; контроль `git status` перед коммитом. |
| **Раса с `release.yml` (двойное создание релиза)** | Либо ждать workflow и затем `gh release edit` + `upload`, либо создавать релиз `gh release create` до того, как workflow «захватит» тег. Не дублировать имена ассетов (для Linux — единое имя). |
| **Ассеты Windows не загрузились** | После создания релиза проверить `gh release view ... --json assets`; загрузить вручную `gh release upload --clobber`. |

---

## 8. Критерии готовности (Definition of Done)

- [ ] Все открытые PR просмотрены; закрытые/merged не затронуты.
- [ ] PR без конфликтов применены; конфликтные — зафиксированы в плане и не применены.
- [ ] Версия поднята до `0.3.8.22` во всех четырёх полях csproj.
- [ ] `CHANGELOG.md`, `README.md` и `_release/0.3.8.22.md` обновлены.
- [ ] Собраны `ConfigurationManagement.exe` (win-x64) и `ConfigurationManagement` (linux-x64).
- [ ] Изменения запушены в `main`, создан тег `v0.3.8.22` и релиз с обоими ассетами.
- [ ] `gh release view v0.3.8.22` показывает оба ассета.