# Проверка последних комментариев к issues
$repo = 'https://api.github.com/repos/sivatorov/ConfigurationManagement'

$headers = @{}
Get-Content '.gh_headers' | ForEach-Object {
    $parts = $_ -split ':\s*', 2
    if ($parts.Count -eq 2) { $headers[$parts[0]] = $parts[1] }
}

foreach ($n in 323, 330, 334, 340) {
    $url = "$repo/issues/$n/comments?per_page=100"
    $c = Invoke-RestMethod -Uri $url -Headers $headers
    Write-Host "### ISSUE $n (count=$(@($c).Count))"
    if ($null -eq $c -or @($c).Count -eq 0) {
        Write-Host "NO COMMENTS"
        Write-Host ""
        continue
    }
    $arr = @($c)
    $last = $arr[$arr.Count - 1]
    Write-Host ("LAST: " + $last.user.login + " @ " + $last.created_at)
    $body = $last.body -replace "`r?`n", " "
    if ($body.Length -gt 150) { $body = $body.Substring(0, 150) + "..." }
    Write-Host ("BODY: " + $body)
    Write-Host ""
}