# -*- coding: utf-8 -*-
"""
Задача 1 (PLAN-0.3.9.307): свежая проверка открытых issues и обновление снимков.
Читает сырые данные GitHub REST (publish/issue_<N>.json, publish/_comments_<N>.json),
сохраняет форматы существующих снимков:
  - issues_live_state.json
  - issues_details.json
  - new_comments_after_1935.json
  - issues_analysis.json
  - publish/issues_snapshot_<дата>.md
"""
import json
import datetime

REPO = "sivatorov/ConfigurationManagement"
SNAPSHOT_DATE = "2026-10-05"          # дата снимка (UTC)
ISSUE_NUMBERS = [323, 330, 334, 340]  # все открытые issues на момент проверки

# --- Чтение сырых данных ----------------------------------------------------
issues = {}
for n in ISSUE_NUMBERS:
    with open(f"publish/issue_{n}.json", encoding="utf-8") as f:
        issues[n] = json.load(f)

comments = {}
for n in ISSUE_NUMBERS:
    with open(f"publish/_comments_{n}.json", encoding="utf-8") as f:
        comments[n] = json.load(f)
    # гарантируем порядок по времени (API уже отдаёт по возрастанию)
    comments[n].sort(key=lambda c: c["created_at"])

def preview(text, limit=400):
    """Короткое превью начала текста (как в существующих снимках)."""
    if not text:
        return None
    return text[:limit]

# --- issues_live_state.json -------------------------------------------------
live = []
for n in sorted(ISSUE_NUMBERS, reverse=True):
    it = issues[n]
    cc = comments[n]
    entry = {
        "Number": n,
        "Title": it["title"],
        "CommentsCount": len(cc),
        "UpdatedAt": it["updated_at"],
        "LastCommentAuthor": None,
        "LastCommentDate": None,
        "LastCommentPreview": None,
    }
    if cc:
        last = cc[-1]
        entry["LastCommentAuthor"] = last["user"]["login"]
        entry["LastCommentDate"] = last["created_at"]
        entry["LastCommentPreview"] = preview(last["body"])
    live.append(entry)

# --- issues_details.json ----------------------------------------------------
details = []
for n in sorted(ISSUE_NUMBERS, reverse=True):
    it = issues[n]
    for cm in comments[n]:
        details.append({
            "Number": n,
            "IssueTitle": it["title"],
            "Body": it["body"],
            "CommentAuthor": cm["user"]["login"],
            "CommentDate": cm["created_at"],
            "CommentBody": cm["body"],
        })

# --- new_comments_after_1935.json -------------------------------------------
new_comments = []
for n in sorted(ISSUE_NUMBERS, reverse=True):
    it = issues[n]
    for cm in comments[n]:
        new_comments.append({
            "Number": n,
            "Title": it["title"],
            "Author": cm["user"]["login"],
            "Date": cm["created_at"],
            "Body": cm["body"],
        })

# --- issues_analysis.json ---------------------------------------------------
requirements = {
    323: (
        "Проверка обновлений (F9) по-прежнему не может получить каталог релизов releases.1c.ru: "
        "после релиза 0.3.9.306 (фикс «фантомного успеха» входа) свежий лог пользователя "
        "2026-10-05 09:06:21 показывает «успешный» вход → повтор исходного запроса → снова HTTP 302 "
        "на login.1c.ru (retryAfterLoginStill302=true) → AuthRequired. Требуется добиться реального "
        "программного входа на portal.1c.ru (валидная сессионная cookie / тикет CAS) либо честная "
        "диагностика; правки выполнять по последним комментариям."
    ),
    330: (
        "Скачивание и установка нужной версии платформы 1С из стартера: окно работает, но получение "
        "каталога версий упирается во вход на portal.1c.ru — свежий лог 2026-10-05 09:07:26: «успешный» "
        "вход → повтор исходного запроса → снова 302 (retryAfterLoginStill302=true) → ошибка/требуется "
        "вход. Требуется реальный программный вход на portal.1c.ru с валидной сессией; правки выполнять "
        "по последним комментариям."
    ),
    334: (
        "Автообновление платформы: получение каталога версий с портала 1С не работает — свежий лог "
        "2026-10-05 09:07:52: «успешный» вход → повтор исходного запроса → снова 302 "
        "(retryAfterLoginStill302=true). Требуется доработка программного входа (повторная попытка входа "
        "в рамках операции/свежая форма, заголовки Referer/Origin, вариант HTTP) чтобы каталог получался; "
        "правки выполнять по последним комментариям."
    ),
    340: (
        "Снятие выделения после мультивыделения/контекстного меню — восьмая попытка не помогла: "
        "пользователь сообщает «файл не появился (см. выше), текущая строка всё ещё исчезает»; просьба — "
        "видеть в файле trace.json доступные переменные для отладки. Требуется: рабочая диагностика "
        "(файл-флаг/журнал menuclose_trace.json рядом с настройками) и по журналу — детерминированный "
        "фикс снятия выделения; правки выполнять по последним комментариям."
    ),
}

analysis = []
for n in sorted(ISSUE_NUMBERS, reverse=True):
    it = issues[n]
    cc = comments[n]
    last = cc[-1] if cc else None
    analysis.append({
        "Number": n,
        "Title": it["title"],
        "Comments": len(cc),
        "Created": it["created_at"],
        "Updated": it["updated_at"],
        "Author": it["user"]["login"],
        "LastCommentAuthor": last["user"]["login"] if last else None,
        "LastCommentDate": last["created_at"] if last else None,
        "Action": "fix",
        "Requirement": requirements[n],
        "LastCommentBody": last["body"] if last else None,
    })

# --- запись JSON (UTF-8, без BOM, отступ 4, кириллица без \u) -----------------
def write_json(path, data):
    with open(path, "w", encoding="utf-8", newline="\n") as f:
        json.dump(data, f, ensure_ascii=False, indent=4)
        f.write("\n")

write_json("issues_live_state.json", live)
write_json("issues_details.json", details)
write_json("new_comments_after_1935.json", new_comments)
write_json("issues_analysis.json", analysis)

# --- publish/issues_snapshot_<дата>.md ---------------------------------------
def one_line(text, limit=130):
    t = (text or "").replace("\r", " ").replace("\n", " ")
    while "  " in t:
        t = t.replace("  ", " ")
    return t[:limit]

rows = []
for n in sorted(ISSUE_NUMBERS, reverse=True):
    it = issues[n]
    cc = comments[n]
    last = cc[-1] if cc else None
    last_author = last["user"]["login"] if last else "—"
    last_date = last["created_at"] if last else "—"
    if last:
        prev = one_line(last["body"], 110)
        last_str = f"{last_author}, {last_date} — «{prev}…»"
    else:
        last_str = "нет комментариев"
    decision = "fix"  # у всех 4 последний комментарий не от sivatorov
    basis = (
        f"Последний комментарий от {last_author}, не от автора репозитория (sivatorov) → "
        f"правило «б»: fix по последним комментариям."
        if last and last_author != "sivatorov"
        else "Нет комментариев → fix по описанию (правило «а»)."
    )
    rows.append((n, it["title"], it["user"]["login"], len(cc), last_str, decision, basis))

md_lines = []
md_lines.append("# Снимок открытых issues — 2026-10-05")
md_lines.append("")
md_lines.append(f"- Репозиторий: [{REPO}](https://github.com/{REPO})")
md_lines.append("- Дата и время проверки: 2026-10-05, ~07:19 UTC (REST GitHub через `gh api`, авторизация `sivatorov`)")
md_lines.append(f"- Открыто issues: **{len(ISSUE_NUMBERS)}** (#{', #'.join(str(x) for x in sorted(ISSUE_NUMBERS, reverse=True))})")
md_lines.append("- Правила отбора: (а) нет комментариев → fix по описанию; (б) последний комментарий не от `sivatorov` → fix по последним комментариям; последний комментарий от `sivatorov` → skip (ждём реакции пользователя).")
md_lines.append("")
md_lines.append("## Таблица отбора")
md_lines.append("")
md_lines.append("| № | Title | Автор issue | Комментариев | Последний комментарий (автор, дата UTC) | fix/skip | Обоснование |")
md_lines.append("|---|-------|-------------|--------------|------------------------------------------|----------|-------------|")
for n, title, author, cnt, last_str, decision, basis in rows:
    md_lines.append(f"| #{n} | {title} | {author} | {cnt} | {last_str} | **{decision}** | {basis} |")
md_lines.append("")
md_lines.append("## Детали по каждому issue")
md_lines.append("")
for n, title, author, cnt, last_str, decision, basis in rows:
    md_lines.append(f"### #{n} — {title}")
    md_lines.append("")
    md_lines.append(f"- Автор issue: `{author}`")
    md_lines.append(f"- Комментариев: {cnt}")
    md_lines.append(f"- Последний комментарий: {last_str}")
    md_lines.append(f"- Решение: **{decision}**")
    md_lines.append(f"- Обоснование: {basis}")
    md_lines.append(f"- Требование (для Задачи 2): {one_line(requirements[n], 400)}")
    md_lines.append("")
md_lines.append("## Изменения состава открытых issues")
md_lines.append("")
md_lines.append("Проверено `gh issue list --state all --limit 100`: максимальный номер issue — **345**, новых issues не создавалось.")
md_lines.append("")
md_lines.append("Открыто ровно **4 issues** — состав совпадает с PLAN-0.3.9.307 (раздел 2).")
md_lines.append("")
md_lines.append("По сравнению с предыдущим локальным снимком `issues_live_state.json` (9 записей) закрыты 5 issues:")
md_lines.append("")
md_lines.append("- #345 «Функционал связывания базы» (закрыт 2026-10-04)")
md_lines.append("- #342 «Инспектор процессов» (закрыт 2026-10-04)")
md_lines.append("- #321 «Окно Типовые конфигурации» (закрыт 2026-10-04)")
md_lines.append("- #309 «Пропал горизонтальный скрол» (закрыт 2026-10-04)")
md_lines.append("- #305 «Создание серверной базы» (закрыт 2026-10-04)")
md_lines.append("")
md_lines.append("## Сводка решений")
md_lines.append("")
md_lines.append("Все 4 открытых issues → **fix** (последний комментарий в каждом — от пользователя `7OH`, не от автора репозитория).")
md_lines.append("")
md_lines.append("- **Кластер A — вход на portal.1c.ru** (#323, #330, #334): общий корень — после релиза 0.3.9.306 лог показывает «успешный» вход, но повтор исходного запроса снова даёт HTTP 302 на login.1c.ru (`retryAfterLoginStill302=true`) → `AuthRequired`.")
md_lines.append("- **#340 «Снятие выделения»**: восьмая попытка не помогла; требуется рабочая диагностика (`trace.json`/`menuclose_trace.json`) и детерминированный фикс по журналу.")

with open(f"publish/issues_snapshot_{SNAPSHOT_DATE}.md", "w", encoding="utf-8", newline="\n") as f:
    f.write("\n".join(md_lines))
    f.write("\n")

print("OK: 4 JSON + issues_snapshot_{}.md сформированы".format(SNAPSHOT_DATE))
for e in live:
    print(f"  #{e['Number']}: {e['CommentsCount']} comments, last={e['LastCommentAuthor']} {e['LastCommentDate']}")