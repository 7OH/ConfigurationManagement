# T1: просмотр последних комментариев по 5 открытым issues (для сводки)
$targets = @(346, 340, 334, 330, 323)
foreach ($n in $targets) {
    $issue = Get-Content "publish\issue_$n.json" -Raw | ConvertFrom-Json
    $comments = @(Get-Content "publish\issue_${n}_comments.json" -Raw | ConvertFrom-Json)
    Write-Output ("==================== ISSUE #${n}: $($issue.title) ====================")
    Write-Output ("state=$($issue.state) created=$($issue.created_at) updated=$($issue.updated_at) comments_total=$($comments.Count)")
    $start = [Math]::Max(0, $comments.Count - 2)
    for ($i = $start; $i -lt $comments.Count; $i++) {
        $c = $comments[$i]
        Write-Output ("----- [$($i+1)/$($comments.Count)] @$($c.user.login) $($c.created_at) id=$($c.id) -----")
        Write-Output $c.body
        Write-Output ""
    }
}
Write-Output 'DONE'