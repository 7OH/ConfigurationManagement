$ErrorActionPreference = 'Stop'
$repo = 'sivatorov/ConfigurationManagement'

$issues = gh api "repos/$repo/issues?state=open&per_page=100" | ConvertFrom-Json
$result = foreach ($i in $issues) {
    $c = gh api "repos/$repo/issues/$($i.number)/comments?per_page=100" | ConvertFrom-Json
    $last = if ($c.Count -gt 0) {
        $l = $c[$c.Count - 1]
        "$($l.user.login) | $($l.created_at)"
    } else {
        'NO_COMMENTS'
    }
    [PSCustomObject]@{
        Number   = $i.number
        Title    = $i.title
        Created  = $i.created_at
        Updated  = $i.updated_at
        Author   = $i.user.login
        Comments = $c.Count
        Last     = $last
    }
}
$result | Format-Table -AutoSize | Out-String -Width 200 | Write-Output
Write-Output "----BODIES----"
foreach ($i in $issues) {
    Write-Output "=== ISSUE #$($i.number): $($i.title) ==="
    Write-Output $i.body
    Write-Output ""
}