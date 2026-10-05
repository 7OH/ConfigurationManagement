# Формирование читаемых файлов описаний + комментариев для анализа issues
$ErrorActionPreference = "Stop"
Remove-Item Env:GH_TOKEN -ErrorAction SilentlyContinue

$targets = @(339, 338, 333, 332, 309, 308)

foreach ($n in $targets) {
    $issue = Get-Content "publish\issue_$n.json" -Raw | ConvertFrom-Json
    $comments = Get-Content "publish\issue_${n}_comments.json" -Raw | ConvertFrom-Json

    $out = "publish\issue_${n}_full.md"
    $sb = New-Object System.Text.StringBuilder
    [void]$sb.AppendLine("# Issue #$($issue.number): $($issue.title)")
    [void]$sb.AppendLine("")
    [void]$sb.AppendLine("- Author: $($issue.user.login)")
    [void]$sb.AppendLine("- Created: $($issue.created_at)")
    [void]$sb.AppendLine("- State: $($issue.state)")
    [void]$sb.AppendLine("- Labels: $(($issue.labels | ForEach-Object { $_.name }) -join ', ')")
    [void]$sb.AppendLine("")
    [void]$sb.AppendLine("## Описание")
    [void]$sb.AppendLine("")
    [void]$sb.AppendLine($issue.body)
    [void]$sb.AppendLine("")
    [void]$sb.AppendLine("---")
    [void]$sb.AppendLine("")

    $i = 0
    foreach ($c in $comments) {
        $i++
        [void]$sb.AppendLine("## Комментарий $i от $($c.user.login) ($($c.created_at))")
        [void]$sb.AppendLine("")
        [void]$sb.AppendLine($c.body)
        [void]$sb.AppendLine("")
        [void]$sb.AppendLine("---")
        [void]$sb.AppendLine("")
    }

    Set-Content -Path $out -Value $sb.ToString() -Encoding UTF8
    Write-Output "Built $out"
}