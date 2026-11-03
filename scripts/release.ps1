param([Parameter(Mandatory)][ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Push-Location $root
try {
    if (git status --porcelain) { throw 'Commit all changes before releasing.' }
    if ((git branch --show-current).Trim() -ne 'main') { throw 'Release from merged main.' }
    $current = (Get-Content server/package.json -Raw | ConvertFrom-Json).version
    if ($current -ne $Version) { throw 'Update and merge the package version first.' }
    git tag "v$Version"
    if ($LASTEXITCODE -ne 0) { throw 'Tag creation failed.' }
    Write-Host "Tag created. Push with: git push origin v$Version"
} finally { Pop-Location }
