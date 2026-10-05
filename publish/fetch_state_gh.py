# Fresh snapshot of open issues + last comment authors via `gh` CLI.
# Run with GH_TOKEN unset so the keyring account (sivatorov) is used.
import json
import subprocess
import sys
import os

ME = "sivatorov"
OWNER = "sivatorov"
REPO = "ConfigurationManagement"
OUT = "publish"


def gh(args):
    env = dict(os.environ)
    env.pop("GH_TOKEN", None)
    r = subprocess.run(["gh", "api"] + args, capture_output=True, text=True, encoding="utf-8", env=env)
    if r.returncode != 0:
        sys.exit(f"gh api failed: {args}\n{r.stderr}")
    return r.stdout


def main():
    raw = gh([f"repos/{OWNER}/{REPO}/issues?state=open&per_page=100", "--paginate"])
    issues = [i for i in json.loads(raw) if "pull_request" not in i]
    print(f"OPEN ISSUES: {len(issues)}")

    summary = []
    for issue in issues:
        n = issue["number"]
        comments = []
        if issue.get("comments", 0) > 0:
            c_raw = gh([f"repos/{OWNER}/{REPO}/issues/{n}/comments?per_page=100", "--paginate"])
            comments = json.loads(c_raw)
        with open(os.path.join(OUT, f"issue_{n}_comments_live.json"), "w", encoding="utf-8") as f:
            json.dump(comments, f, ensure_ascii=False, indent=2)

        login, date, preview = "", "", ""
        if comments:
            last = comments[-1]
            login = str(last.get("user", {}).get("login", ""))
            date = str(last.get("created_at", ""))
            b = str(last.get("body", ""))
            b = b.replace("\r", " ").replace("\n", " ")
            preview = b[:240]

        needs_work = (len(comments) == 0) or (login != ME)
        summary.append({
            "Number": n,
            "Title": issue.get("title", ""),
            "Author": issue.get("user", {}).get("login", ""),
            "Created": issue.get("created_at", ""),
            "Updated": issue.get("updated_at", ""),
            "CommentsCount": len(comments),
            "LastCommentAuthor": login,
            "LastCommentDate": date,
            "LastCommentPreview": preview,
            "NeedsWork": needs_work,
        })

    summary.sort(key=lambda x: -x["Number"])
    with open(os.path.join(OUT, "issues_live.json"), "w", encoding="utf-8") as f:
        json.dump(summary, f, ensure_ascii=False, indent=4)

    print("\n=== OPEN ISSUES (live) ===")
    for s in summary:
        print(f"#{s['Number']:<4} cmt={s['CommentsCount']:<3} last={s['LastCommentAuthor']:<10} needsWork={s['NeedsWork']} | {s['Title']}")

    cands = [s for s in summary if s["NeedsWork"]]
    print(f"\n=== CANDIDATES (no comments or last comment not from {ME}): {len(cands)} ===")
    for c in cands:
        print(f"#{c['Number']} [{c['CommentsCount']}] last: {c['LastCommentAuthor']} ({c['LastCommentDate']}) | {c['Title']}")


if __name__ == "__main__":
    main()