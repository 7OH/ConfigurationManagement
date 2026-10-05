# T1: пересборка publish/issue_<N>_full.md и обновление файлов состояния issues
# Источник: свежие publish/issue_<N>.json + publish/issue_<N>_comments.json (получены ранее).
# ВАЖНО: в PS 5.1 нельзя оборачивать в @() конвейер с ConvertFrom-Json напрямую
#        (массив не разворачивается). Сначала присваивание, потом @($var).
$ErrorActionPreference = 'Stop'
$targets = @(346, 340, 334, 330, 323)

function Get-Preview([string]$body, [int]$maxLen = 300) {
    if ([string]::IsNullOrEmpty($body)) { return '' }
    $flat = $body -replace "`r?`n", ' '
    $flat = $flat -replace '\s+', ' '
    if ($flat.Length -gt $maxLen) { return $flat.Substring(0, $maxLen) + '...' }
    return $flat
}

# ---------- 1. Читаем данные по всем целевым issues ----------
$data = @{}
foreach ($n in $targets) {
    $issue = Get-Content "publish\issue_$n.json" -Raw | ConvertFrom-Json
    $parsedC = Get-Content "publish\issue_${n}_comments.json" -Raw | ConvertFrom-Json
    $comments = @($parsedC)
    $data[$n] = @{ Issue = $issue; Comments = $comments }
    Write-Output ("Loaded #$n : comments=" + $comments.Count)
}

# ---------- 2. Пересборка publish/issue_<N>_full.md ----------
foreach ($n in $targets) {
    $issue = $data[$n].Issue
    $comments = $data[$n].Comments
    $out = "publish\issue_${n}_full.md"
    $sb = New-Object System.Text.StringBuilder
    [void]$sb.AppendLine("# Issue #$($issue.number): $($issue.title)")
    [void]$sb.AppendLine("")
    [void]$sb.AppendLine("- Author: $($issue.user.login)")
    [void]$sb.AppendLine("- Created: $($issue.created_at)")
    [void]$sb.AppendLine("- Updated: $($issue.updated_at)")
    [void]$sb.AppendLine("- State: $($issue.state)")
    $labels = ''
    if ($issue.labels) { $labels = ($issue.labels | ForEach-Object { $_.name }) -join ', ' }
    [void]$sb.AppendLine("- Labels: $labels")
    [void]$sb.AppendLine("")
    [void]$sb.AppendLine("## Описание")
    [void]$sb.AppendLine("")
    [void]$sb.AppendLine([string]$issue.body)
    [void]$sb.AppendLine("")
    [void]$sb.AppendLine("---")
    [void]$sb.AppendLine("")
    $i = 0
    foreach ($c in $comments) {
        $i++
        [void]$sb.AppendLine("## Комментарий $i от $($c.user.login) ($($c.created_at))")
        [void]$sb.AppendLine("")
        [void]$sb.AppendLine([string]$c.body)
        [void]$sb.AppendLine("")
        [void]$sb.AppendLine("---")
        [void]$sb.AppendLine("")
    }
    Set-Content -Path $out -Value $sb.ToString() -Encoding UTF8
    Write-Output "Built $out"
}

# ---------- 3. issues_live_state.json ----------
$live = @()
foreach ($n in ($targets | Sort-Object -Descending)) {
    $issue = $data[$n].Issue
    $comments = $data[$n].Comments
    $last = $null
    if ($comments.Count -gt 0) { $last = $comments[$comments.Count - 1] }
    $live += [PSCustomObject]@{
        Number              = $n
        Title               = $issue.title
        CommentsCount       = $comments.Count
        UpdatedAt           = $issue.updated_at
        LastCommentAuthor   = $(if ($last) { $last.user.login } else { $null })
        LastCommentDate     = $(if ($last) { $last.created_at } else { $null })
        LastCommentPreview  = $(if ($last) { Get-Preview $last.body } else { '' })
    }
}
$live | ConvertTo-Json -Depth 10 | Set-Content -Path 'issues_live_state.json' -Encoding UTF8
Write-Output 'Updated issues_live_state.json'

# ---------- 4. issues_state.json ----------
$state = @()
foreach ($n in ($targets | Sort-Object -Descending)) {
    $issue = $data[$n].Issue
    $comments = $data[$n].Comments
    $last = $null
    if ($comments.Count -gt 0) { $last = $comments[$comments.Count - 1] }
    $state += [PSCustomObject]@{
        Number              = $n
        Title               = $issue.title
        Comments            = $comments.Count
        LastCommentAuthor   = $(if ($last) { $last.user.login } else { $null })
        LastCommentDate     = $(if ($last) { $last.created_at } else { $null })
        LastCommentPreview  = $(if ($last) { Get-Preview $last.body } else { '' })
    }
}
$state | ConvertTo-Json -Depth 10 | Set-Content -Path 'issues_state.json' -Encoding UTF8
Write-Output 'Updated issues_state.json'

# ---------- 5. issues_details.json (полные описания + все комментарии) ----------
$details = @()
foreach ($n in ($targets | Sort-Object -Descending)) {
    $issue = $data[$n].Issue
    $comments = $data[$n].Comments
    foreach ($c in $comments) {
        $details += [PSCustomObject]@{
            Number        = $n
            IssueTitle    = $issue.title
            Body          = [string]$issue.body
            CommentAuthor = $c.user.login
            CommentDate   = $c.created_at
            CommentBody   = [string]$c.body
        }
    }
}
$details | ConvertTo-Json -Depth 10 | Set-Content -Path 'issues_details.json' -Encoding UTF8
Write-Output ("Updated issues_details.json (entries=" + $details.Count + ")")

# ---------- 6. new_comments_after_1935.json (дописать новое, без дублей) ----------
$existingPath = 'new_comments_after_1935.json'
$parsedAll = Get-Content $existingPath -Raw | ConvertFrom-Json
$existingAll = @($parsedAll)
# Отбрасываем повреждённые записи предыдущего запуска (поля-массивы) - оставляем валидные.
$existing = @($existingAll | Where-Object { $_.Author -is [string] })
Write-Output ("new_comments base: total=" + $existingAll.Count + " valid=" + $existing.Count)

# Ключ дедупликации: Number|Date|Author (тела в историческом файле могут отличаться
# от текущих тел API, поэтому Body в ключ не включаем).
$keySet = New-Object 'System.Collections.Generic.HashSet[string]'
foreach ($e in $existing) {
    [void]$keySet.Add(("$($e.Number)|$($e.Date)|$($e.Author)"))
}

$added = 0
$newItems = New-Object System.Collections.ArrayList
foreach ($n in $targets) {
    $issue = $data[$n].Issue
    $comments = $data[$n].Comments
    foreach ($c in $comments) {
        $key = "$n|$($c.created_at)|$($c.user.login)"
        if (-not $keySet.Contains($key)) {
            [void]$keySet.Add($key)
            [void]$newItems.Add([PSCustomObject]@{
                Number = $n
                Title  = $issue.title
                Author = $c.user.login
                Date   = $c.created_at
                Body   = [string]$c.body
            })
            Write-Output ("NEW comment: #" + $n + " " + $c.user.login + " " + $c.created_at)
            $added++
        }
    }
}

$combined = @($existing) + @($newItems)
$combined | ConvertTo-Json -Depth 10 | Set-Content -Path $existingPath -Encoding UTF8
Write-Output ("Updated new_comments_after_1935.json (existing=" + $existing.Count + " added=" + $added + " total=" + $combined.Count + ")")

Write-Output 'DONE'