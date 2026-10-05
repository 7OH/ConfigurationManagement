# T1: снимок открытых issues + ВСЕ комментарии по #346/#340/#334/#330/#323
# Заголовки авторизации читаются из .gh_headers (файл в ignore, не коммитится).
# Результат: publish/_open_issues_raw.json,
#            publish/issue_<N>.json, publish/issue_<N>_comments.json
$ErrorActionPreference = 'Stop'
$repo = 'https://api.github.com/repos/sivatorov/ConfigurationManagement'

# --- Заголовки авторизации из .gh_headers (не логируем содержимое) ---
$headers = @{}
if (Test-Path '.gh_headers') {
    Get-Content '.gh_headers' | ForEach-Object {
        $parts = $_ -split ':\s*', 2
        if ($parts.Count -eq 2) { $headers[$parts[0]] = $parts[1] }
    }
}
if ($headers.Count -eq 0) {
    Write-Warning 'No auth headers loaded from .gh_headers - fallback to anonymous requests'
}

# --- 1. Полный список открытых issues ---
$issues = Invoke-RestMethod -Uri "$repo/issues?state=open&per_page=100" -Headers $headers
$issues | ConvertTo-Json -Depth 30 | Set-Content -Path 'publish\_open_issues_raw.json' -Encoding UTF8
Write-Output ("OPEN ISSUES COUNT: " + @($issues).Count)
foreach ($i in $issues) {
    Write-Output ("ISSUE #$($i.number) [$($i.state)] comments=$($i.comments) updated=$($i.updated_at) :: $($i.title)")
}

# --- 2. Для каждого целевого issue: карточка + ВСЕ комментарии (пагинация по 100) ---
$targets = @(346, 340, 334, 330, 323)
foreach ($n in $targets) {
    $issue = Invoke-RestMethod -Uri "$repo/issues/$n" -Headers $headers
    $issue | ConvertTo-Json -Depth 30 | Set-Content -Path "publish\issue_$n.json" -Encoding UTF8

    $all = New-Object System.Collections.ArrayList
    $page = 1
    $maxPages = 30
    while ($true) {
        $c = Invoke-RestMethod -Uri "$repo/issues/$n/comments?per_page=100&page=$page" -Headers $headers
        $arr = @($c)
        foreach ($item in $arr) { [void]$all.Add($item) }
        if ($arr.Count -lt 100) { break }
        $page++
        if ($page -gt $maxPages) { Write-Warning "Pagination guard hit for #$n (page $page)"; break }
    }

    $all | ConvertTo-Json -Depth 30 | Set-Content -Path "publish\issue_${n}_comments.json" -Encoding UTF8

    $expected = $issue.comments
    $got = $all.Count
    $ok = if ($got -eq $expected) { 'OK' } else { 'MISMATCH!' }
    Write-Output ("ISSUE #$n comments expected=$expected got=$got [$ok]")
    if ($got -gt 0) {
        $last = $all[$got - 1]
        Write-Output ("  last: @" + $last.user.login + " " + $last.created_at)
    }
}
Write-Output 'DONE'