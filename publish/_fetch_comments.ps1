$ErrorActionPreference = 'Stop'
$repo = 'sivatorov/ConfigurationManagement'
$nums = @(320, 319, 316, 314, 313, 309, 308, 305, 304)

foreach ($n in $nums) {
    Write-Output "==================== ISSUE #$n ===================="
    $c = gh api "repos/$repo/issues/$n/comments?per_page=100" | ConvertFrom-Json
    foreach ($cm in $c) {
        Write-Output "--- $($cm.created_at) by @$($cm.user.login) ---"
        Write-Output $cm.body
        Write-Output ""
    }
}