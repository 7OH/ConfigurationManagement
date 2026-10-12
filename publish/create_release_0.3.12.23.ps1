# Create GitHub release v0.3.12.23 (sivatorov/ConfigurationManagement)
# Pattern follows release 0.3.12.3 (see: gh release view v0.3.12.3 and publish_win_0.3.11.0.md).
#
# Assets (order matters: ConfigurationManagement.exe must be first - GitHubReleaseService.FindAsset
# matches it exactly, see publish_win_0.3.11.0.md):
#   1. ConfigurationManagement.exe                          (mandatory asset, issue #358)
#   2. ConfigurationManagement-0.3.12.23-win-x64.zip       (ZIP - extra asset)
#   3. ConfigurationManagement-linux-x64                    (Linux single-file, Avalonia)
#   4. configuration-management_0.3.12.23_amd64.deb
#   5. SHA256SUMS.txt
#
# Usage:
#   powershell -NoProfile -ExecutionPolicy Bypass -File publish/create_release_0.3.12.23.ps1

$ErrorActionPreference = 'Stop'

$Repo    = 'sivatorov/ConfigurationManagement'
$Tag     = 'v0.3.12.23'
$Head    = '27885db69cdd36f377b28150f176118df4248454'   # current HEAD of main (updated after commit)
$OutDir  = 'publish/out-0.3.12.23'
$Notes   = 'publish/release_body_0.3.12.23.md'
# Title is taken from the first line of the notes file (H1), UTF-8 is read explicitly
# so that non-ASCII characters survive Windows PowerShell 5.1 (script itself is ASCII-only).
$Title   = ((Get-Content $Notes -Encoding UTF8 -TotalCount 1) -replace '^#\s*', '').Trim()
if (-not $Title) { throw "Cannot read release title from $Notes" }

# 0. GH_TOKEN may hold an invalid token - drop it so gh uses stored credentials (gh auth).
Remove-Item Env:GH_TOKEN -ErrorAction SilentlyContinue

if (-not (Test-Path $OutDir)) { throw "Missing artifacts dir: $OutDir" }
if (-not (Test-Path $Notes))  { throw "Missing notes file: $Notes" }

Push-Location
Set-Location $PSScriptRoot/..   # repo root

try {
    # 1. Tag v0.3.12.23 on current HEAD and push the tag
    $actualHead = git rev-parse HEAD
    if ($actualHead -ne $Head) { throw "HEAD ($actualHead) does not match expected $Head" }

    git tag $Tag $Head
    git push origin $Tag

    # 2. Create the release with notes
    gh release create $Tag `
        --repo $Repo `
        --title $Title `
        --notes-file $Notes

    # 3. Upload assets (exe first, see publish_win_0.3.11.0.md)
    #    Linux binary is uploaded as ConfigurationManagement-linux-x64 (as in 0.3.12.2).
    Copy-Item "$OutDir/ConfigurationManagement" "$OutDir/ConfigurationManagement-linux-x64" -Force

    gh release upload $Tag "$OutDir/ConfigurationManagement.exe"                  --repo $Repo --clobber
    gh release upload $Tag "$OutDir/ConfigurationManagement-0.3.12.23-win-x64.zip" --repo $Repo --clobber
    gh release upload $Tag "$OutDir/ConfigurationManagement-linux-x64"            --repo $Repo --clobber
    gh release upload $Tag "$OutDir/configuration-management_0.3.12.23_amd64.deb"  --repo $Repo --clobber
    gh release upload $Tag "$OutDir/SHA256SUMS.txt"                               --repo $Repo --clobber

    # 4. Verify
    gh release view $Tag --repo $Repo
}
finally {
    Pop-Location
}