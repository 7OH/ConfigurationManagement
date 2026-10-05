# Сбор свежих данных: все комментарии по всем открытым issues
$ErrorActionPreference = "Stop"
Remove-Item Env:GH_TOKEN -ErrorAction SilentlyContinue

$issues = @(344,343,342,341,340,335,334,333,330,324,323,321,309,305)

foreach ($n in $issues) {
    try {
        $json = gh api "repos/sivatorov/ConfigurationManagement/issues/$n/comments?per_page=100"
        $json | Out-File -FilePath ("publish/issue_{0}_comments_fresh.json" -f $n) -Encoding utf8
        $comments = $json | ConvertFrom-Json
        if ($null -eq $comments -or $comments.Count -eq 0) {
            Write-Output ("{0}`tNO_COMMENTS" -f $n)
        } else {
            $last = $comments[$comments.Count - 1]
            $preview = $last.body.Substring(0, [Math]::Min(150, $last.body.Length))
            $preview = $preview.Replace("`r", " ").Replace("`n", " ")
            Write-Output ("{0}`tLAST: {1}`t{2}`t{3}" -f $n, $last.user.login, $last.created_at, $preview)
        }
    } catch {
        Write-Output ("{0}`tERROR: {1}" -f $n, $_.Exception.Message)
    }
}