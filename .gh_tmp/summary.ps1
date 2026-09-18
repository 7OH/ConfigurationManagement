$ErrorActionPreference = 'Stop'
$j = Get-Content .gh_tmp\issues.json -Raw | ConvertFrom-Json
$lines = @()
foreach ($n in $j.data.repository.issues.nodes) {
    $c = @($n.comments.nodes)
    $cc = $c.Count
    $last = '--'
    $lastDate = ''
    if ($cc -gt 0) {
        $last = $c[$cc - 1].author.login
        $lastDate = $c[$cc - 1].createdAt
    }
    $lines += "===== ISSUE #$($n.number) ====="
    $lines += "Title: $($n.title)"
    $lines += "Comments: $cc | Last author: $last | Last comment date: $lastDate"
    $lines += "Created: $($n.createdAt) | Updated: $($n.updatedAt)"
    $lines += "---- BODY ----"
    $lines += $n.body
    $lines += "---- COMMENTS ----"
    foreach ($cm in $c) {
        $lines += "[$($cm.createdAt)] $($cm.author.login):"
        $lines += $cm.body
        $lines += "----"
    }
    $lines += ""
}
$lines | Out-File -FilePath .gh_tmp\issues_summary.txt -Encoding UTF8
Write-Output ("Total issues: " + ($j.data.repository.issues.nodes | Measure-Object).Count)