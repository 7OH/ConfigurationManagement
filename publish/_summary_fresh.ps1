$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$OutputEncoding = [System.Text.Encoding]::UTF8
$nums = @(323, 330, 334, 340)
foreach ($n in $nums) {
    $issue = Get-Content "publish\issue_$n.json" -Raw | ConvertFrom-Json
    Write-Output "===== ISSUE #$n : $($issue.title)"
    Write-Output "state=$($issue.state) created=$($issue.created_at) updated=$($issue.updated_at) author=$($issue.user.login) comments=$($issue.comments)"
    $b = $issue.body -replace "`r", ' ' -replace "`n", ' '
    if ($b.Length -gt 300) { $b = $b.Substring(0, 300) }
    Write-Output "body: $b"
    $c = Get-Content "publish\_comments_$n.json" -Raw | ConvertFrom-Json
    Write-Output "--- $($c.Count) comments ---"
    foreach ($cm in $c) {
        $prev = $cm.body -replace "`r", ' ' -replace "`n", ' '
        if ($prev.Length -gt 200) { $prev = $prev.Substring(0, 200) }
        Write-Output "$($cm.created_at) @$($cm.user.login) | $prev"
    }
}