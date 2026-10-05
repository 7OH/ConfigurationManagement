# Gather open issues + comments for ConfigurationManagement, save per-issue artifacts,
# and print the list of candidates: issues with no comments OR last comment author != sivatorov.
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$ErrorActionPreference = "Stop"
$owner = "sivatorov"
$repo = "ConfigurationManagement"
$me = "sivatorov"
$headers = @{ "User-Agent" = "CM-analysis" }

try {
    $resp = Invoke-RestMethod "https://api.github.com/repos/$owner/$repo/issues?state=open&per_page=100" -Headers $headers
} catch {
    "FETCH ERROR: $($_.Exception.Message)" | Out-File -Encoding utf8 "publish\_fetch_debug.txt"
    throw
}
$issues = @($resp)
"after fetch: $($issues.Count)" | Out-File -Encoding utf8 "publish\_fetch_debug.txt"
$issues = @($issues | Where-Object { -not $_.pull_request })
"after pr filter: $($issues.Count)" | Out-File -Encoding utf8 "publish\_fetch_debug.txt"

$issues | ConvertTo-Json -Depth 6 | Out-File -Encoding utf8 "publish\open_issues.json"

$summary = @()
foreach ($issue in $issues) {
    $n = $issue.number
    $issue | ConvertTo-Json -Depth 6 | Out-File -Encoding utf8 "publish\issue_${n}.json"
    $comments = @()
    if ($issue.comments -gt 0) {
        $resp = Invoke-RestMethod $issue.comments_url -Headers $headers
        $comments = @($resp)
    }
    $comments | ConvertTo-Json -Depth 6 | Out-File -Encoding utf8 "publish\issue_${n}_comments.json"

    $last = $null
    if ($comments.Count -gt 0) { $last = $comments[$comments.Count - 1] }

    $login = ""
    $date = ""
    $preview = ""
    if ($null -ne $last) {
        $login = [string]$last.user.login
        $date = [string]$last.created_at
        if ($last.body -is [string]) {
            $b = $last.body -replace "`r", " " -replace "`n", " "
            $preview = if ($b.Length -gt 300) { $b.Substring(0, 300) } else { $b }
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

$summary | Sort-Object Number -Descending | ConvertTo-Json -Depth 4 | Out-File -Encoding utf8 "publish\issues_fresh_full.json"

Write-Output "=== OPEN ISSUES SUMMARY ==="
$summary | Sort-Object Number -Descending | Format-Table Number, CommentsCount, LastCommentAuthor, NeedsWork, Title -AutoSize | Out-String -Width 220

Write-Output ""
Write-Output ("=== CANDIDATES (no comments or last comment not from {0}): {1} ===" -f $me, (@($summary | Where-Object { $_.NeedsWork }).Count))
$candidates = @($summary | Where-Object { $_.NeedsWork }) | Sort-Object Number -Descending
foreach ($c in $candidates) {
    Write-Output ("#{0} [{1}] last: {2} ({3}) | {4}" -f $c.Number, $c.CommentsCount, $c.LastCommentAuthor, $c.LastCommentDate, $c.Title)
}