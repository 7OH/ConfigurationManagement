# Получение полных данных (описание + все комментарии) по выбранным issues
$ErrorActionPreference = "Stop"
Remove-Item Env:GH_TOKEN -ErrorAction SilentlyContinue

$targets = @(339, 338, 333, 332, 309, 308)

foreach ($n in $targets) {
    # Описание issue
    gh api "repos/sivatorov/ConfigurationManagement/issues/$n" > "publish\issue_$n.json"
    # Все комментарии
    gh api "repos/sivatorov/ConfigurationManagement/issues/$n/comments?per_page=100" > "publish\issue_${n}_comments.json"
    Write-Output "Saved issue $n"
}