# Fresh snapshot of open issues + last comment authors via `gh` CLI.
# Requires: gh authenticated as sivatorov (keyring), GH_TOKEN unset.
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$ErrorActionPreference = "Stop"
$me = "sivatorov"

gh api "repos/sivatorov/ConfigurationManagement/issues?state=open&per_page=100" --paginate 2>$null > publish\open_issues_raw.json
$issues = @((Get-Content -Raw -Encoding UTF8 "publish\open_issues_raw.json") | ConvertFrom-Json | Where-Object { -not $_.pull_request })
"OPEN ISSUES: $($issues.Count)"

$summary = @()
foreach ($issue in $issues) {
    $n = $issue.number
    $comments = @()
    if ($issue.comments -gt 0) {
        gh api "repos/sivatorov/ConfigurationManagement/issues/$n/comments?per_page=100" --paginate 2>$null > "publish\issue_${n}_comments_live.json"
        $comments = @((Get-Content -Raw -Encoding UTF8 "publish\issue_${n}_comments_live.json") | ConvertFrom-Json)
    }
    $comments | ConvertTo-Json -Depth 6 | Out-File -Encoding utf8 "publish\issue_${n}_comments_live_norm.json"

    $login = ""
    $date = ""
    $preview = ""
    if ($comments.Count -gt 0) {
        $last = $comments[$comments.Count - 1]
        $login = [string]$last.user.login
        $date = [string]$last.created_at
        if ($last.body -is [string]) {
            $b = $last.body -replace "`r", " " -replace "`n", " "
            $preview = if ($b.Length -gt 240) { $b.Substring(0, 240) } else { $b }
        }
    }
    $needsWork = ($comments.Count -eq 0) -or ($login -ne $me)

    $summary += [pscustomobject]@{
        Number             = $n
        Title              = [string]$issue.title
        Author             = [string]$issue.user.login
        Created            = [string]$issue.created_at
        Updated            = [string]$issue.updated_at
        CommentsCount      = $comments.Count
        LastCommentAuthor  = $login
        LastCommentDate    = $date
        LastCommentPreview = $preview
        NeedsWork          = $needsWork
    }
}

$summary | Sort-Object Number -Descending | ConvertTo-Json -Depth 4 | Out-File -Encoding utf8 "publish\issues_live.json"

Write-Output ""
Write-Output "=== OPEN ISSUES (live) ==="
$summary | Sort-Object Number -Descending | ForEach-Object {
    Write-Output ("#{0,-4} cmt={1,-3} last={2,-10} needsWork={3} | {4}" -f $_.Number, $_.CommentsCount, $_.LastCommentAuthor, $_.NeedsWork, $_.Title)
}
Write-Output ""
Write-Output "=== CANDIDATES (no comments or last comment not from $me): ==="
$summary | Where-Object { $_.NeedsWork } | Sort-Object Number -Descending | ForEach-Object {
    Write-Output ("#{0} [{1}] last: {2} ({3}) | {4}" -f $_.Number, $_.CommentsCount, $_.LastCommentAuthor, $_.LastCommentDate, $_.Title)
}