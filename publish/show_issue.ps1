# Вывод описания и всех комментариев issue в консоль
param([int]$Number)

$ErrorActionPreference = "Stop"
Remove-Item Env:GH_TOKEN -ErrorAction SilentlyContinue

$issue = gh api "repos/sivatorov/ConfigurationManagement/issues/$Number"
$comments = gh api "repos/sivatorov/ConfigurationManagement/issues/$Number/comments?per_page=100"

$issueObj = $issue | ConvertFrom-Json
$commentsObj = $comments | ConvertFrom-Json

Write-Output ("===== ISSUE #{0}: {1} =====" -f $issueObj.number, $issueObj.title)
Write-Output ("Author: {0} | Created: {1} | State: {2}" -f $issueObj.user.login, $issueObj.created_at, $issueObj.state)
Write-Output "----- BODY -----"
Write-Output $issueObj.body
Write-Output "----- END BODY -----"
$i = 0
foreach ($c in $commentsObj) {
    $i++
    Write-Output ""
    Write-Output ("--- COMMENT {0} by {1} at {2} ---" -f $i, $c.user.login, $c.created_at)
    Write-Output $c.body
    Write-Output ("--- END COMMENT {0} ---" -f $i)
}