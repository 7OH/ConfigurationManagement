# Получение последнего комментария по каждому открытому issue
$ErrorActionPreference = "Stop"
Remove-Item Env:GH_TOKEN -ErrorAction SilentlyContinue

$issues = @(339,338,336,335,334,333,332,330,329,327,326,325,324,323,322,321,313,309,308,305)

foreach ($n in $issues) {
    try {
        $json = gh api "repos/sivatorov/ConfigurationManagement/issues/$n/comments?per_page=100"
        $comments = $json | ConvertFrom-Json
        if ($null -eq $comments -or $comments.Count -eq 0) {
            Write-Output ("{0}`tNO_COMMENTS" -f $n)
        } else {
            $last = $comments[$comments.Count - 1]
            $preview = $last.body.Substring(0, [Math]::Min(200, $last.body.Length))
            $preview = $preview.Replace("`r", " ").Replace("`n", " ")
            Write-Output ("{0}`t{1}`t{2}`t{3}" -f $n, $last.user.login, $last.created_at, $preview)
        }
    } catch {
        Write-Output ("{0}`tERROR: {1}" -f $n, $_.Exception.Message)
    }
}