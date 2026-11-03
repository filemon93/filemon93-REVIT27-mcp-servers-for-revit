[CmdletBinding()]
param([string]$Version = '1.1.0', [string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw 'Invalid version.' }
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $root 'artifacts' }
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
$stage = Join-Path $OutputDirectory ("RevitMCP27-$Version-" + [Guid]::NewGuid().ToString('N'))
function Run([string]$Executable, [string[]]$Arguments) {
    & $Executable @Arguments
    if ($LASTEXITCODE -ne 0) { throw "$Executable failed ($LASTEXITCODE)" }
}
Push-Location $root
try {
    Run dotnet @('build','plugin/RevitMCPPlugin.csproj','-c','Release R27','--nologo')
    Run dotnet @('build','commandset/RevitMCPCommandSet.csproj','-c','Release R27','--nologo')
    Push-Location (Join-Path $root 'server')
    try { Run npm.cmd @('ci'); Run npm.cmd @('run','build'); Run npm.cmd @('test'); Run npm.cmd @('audit','--omit=dev') }
    finally { Pop-Location }
    New-Item -ItemType Directory -Path $stage,(Join-Path $stage 'server') -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $root 'plugin\bin\AddIn 2027 Release R27') -Destination (Join-Path $stage 'addin') -Recurse
    # Autodesk API binaries are provided by Revit, never redistribute them.
    Get-ChildItem -LiteralPath (Join-Path $stage 'addin') -Filter 'RevitAPI*.dll' -Recurse | Remove-Item
    Copy-Item -LiteralPath (Join-Path $root 'server\build') -Destination (Join-Path $stage 'server') -Recurse
    Copy-Item -LiteralPath (Join-Path $root 'server\package.json'),(Join-Path $root 'server\package-lock.json') -Destination (Join-Path $stage 'server')
    Push-Location (Join-Path $stage 'server')
    try { Run npm.cmd @('ci','--omit=dev') } finally { Pop-Location }
    Copy-Item -LiteralPath (Get-Command node.exe).Source -Destination (Join-Path $stage 'server\node.exe')
    $nodeVersion = (& node.exe --version).Trim()
    if ($nodeVersion -notmatch '^v22\.') { throw 'Build the Windows x64 distribution with Node 22.' }
    Invoke-WebRequest "https://raw.githubusercontent.com/nodejs/node/$nodeVersion/LICENSE" -OutFile (Join-Path $stage 'server\NODE-LICENSE.txt')
    Copy-Item -Path (Join-Path $root 'distribution\*') -Destination $stage
    Copy-Item -LiteralPath (Join-Path $root 'docs\START-PL.md') -Destination (Join-Path $stage 'START-PL.md')
    Copy-Item -LiteralPath (Join-Path $root 'docs\DIMENSIONING.md'),(Join-Path $root 'docs\VALIDATION.md'),(Join-Path $root 'LICENSE') -Destination $stage
    Set-Content -LiteralPath (Join-Path $stage 'version.txt') -Value $Version -Encoding ASCII
    Set-Content -LiteralPath (Join-Path $stage 'runtime.txt') -Value "Windows x64; Node $nodeVersion; Revit 2027; .NET 10" -Encoding ASCII
    $checksums = @(Get-ChildItem -LiteralPath $stage -File -Recurse | ForEach-Object {
        @{path=$_.FullName.Substring($stage.Length+1);sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash}
    })
    $checksums | ConvertTo-Json -Depth 3 | Set-Content -LiteralPath (Join-Path $stage 'checksums.json') -Encoding UTF8
    & (Join-Path $stage 'Install.ps1') -CheckOnly
    $zip = Join-Path $OutputDirectory "RevitMCP27-$Version-Windows-x64.zip"
    if (Test-Path -LiteralPath $zip) { throw "Package already exists: $zip" }
    Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip -CompressionLevel Optimal
    Get-FileHash -LiteralPath $zip -Algorithm SHA256 | ForEach-Object { "$($_.Hash)  $([IO.Path]::GetFileName($zip))" } |
        Set-Content -LiteralPath ($zip + '.sha256') -Encoding ASCII
    Write-Host "Release asset: $zip"
} finally { Pop-Location }
