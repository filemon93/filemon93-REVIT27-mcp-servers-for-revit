param([Parameter(Mandatory)][string]$Bundle)
$ErrorActionPreference = 'Stop'
$Bundle = (Resolve-Path -LiteralPath $Bundle).Path
$testRoot = Join-Path (Split-Path -Parent $Bundle) ('installer-test-' + [Guid]::NewGuid().ToString('N'))
$env:LOCALAPPDATA = Join-Path $testRoot 'local'
$env:APPDATA = Join-Path $testRoot 'roaming'
$env:ProgramData = Join-Path $testRoot 'programdata'
$global:McpTestRunning = $false
function Get-Process { param($Name, $ErrorAction) if ($global:McpTestRunning) { [pscustomobject]@{Path='C:\Program Files\Autodesk\Revit 2027\Revit.exe'} } }
function Check($Condition, $Label) { if (-not $Condition) { throw "FAILED: $Label" }; Write-Host "PASS: $Label" }
$addins = Join-Path $env:APPDATA 'Autodesk\Revit\Addins\2027'
New-Item -ItemType Directory -Path $addins -Force | Out-Null
$shared = Join-Path $addins 'shared.addin'
@'
<RevitAddIns>
  <AddIn Type="Application"><Name>Old MCP</Name><ClientId>090A4C8C-61DC-426D-87DF-E4BAE0F80EC1</ClientId><Assembly>old.dll</Assembly></AddIn>
  <AddIn Type="Application"><Name>Unrelated</Name><ClientId>00000000-0000-0000-0000-000000000001</ClientId><Assembly>other.dll</Assembly></AddIn>
</RevitAddIns>
'@ | Set-Content -LiteralPath $shared
& (Join-Path $Bundle 'Install.ps1') -CheckOnly
Check (-not (Test-Path -LiteralPath (Join-Path $env:LOCALAPPDATA 'RevitMCP27'))) 'CheckOnly makes no installation changes'
$global:McpTestRunning = $true
$blocked = $false
try { & (Join-Path $Bundle 'Install.ps1') } catch { Write-Host $_.Exception.Message; $blocked = $_.Exception.Message -like '*Close Revit 2027*' }
Check $blocked 'running Revit blocks installation'
$global:McpTestRunning = $false
& (Join-Path $Bundle 'Install.ps1')
[xml]$remaining = Get-Content -LiteralPath $shared
Check ($remaining.RevitAddIns.AddIn.Name -eq 'Unrelated') 'shared manifest preserves unrelated add-in'
$config = Get-Content -LiteralPath (Join-Path $env:LOCALAPPDATA 'RevitMCP27\agent-config.json') -Raw | ConvertFrom-Json
$server = $config.mcpServers.'revit-mcp-27'
Check ((Test-Path -LiteralPath $server.command) -and (Test-Path -LiteralPath $server.args[0])) 'agent configuration references installed runtime and entry point'
[xml]$manifest = Get-Content -LiteralPath (Join-Path $addins 'mcp-servers-for-revit.addin')
Check (Test-Path -LiteralPath $manifest.RevitAddIns.AddIn.Assembly) 'manifest references installed plugin'
$registry = Get-Content -LiteralPath (Join-Path (Split-Path $manifest.RevitAddIns.AddIn.Assembly) 'Commands\commandRegistry.json') -Raw | ConvertFrom-Json
Check (@($registry.commands | Where-Object { $_.commandName -in @('get_dimension_types','dimension_walls_by_guides','create_dimensions') -and $_.enabled }).Count -eq 3) 'new dimension commands enabled with absolute paths'
Check (@(Get-ChildItem -LiteralPath (Join-Path $env:LOCALAPPDATA 'RevitMCP27\backups') -Recurse -Filter shared.addin).Count -eq 1) 'previous manifest backed up'
& (Join-Path $Bundle 'Install.ps1')
[xml]$manifest2 = Get-Content -LiteralPath (Join-Path $addins 'mcp-servers-for-revit.addin')
Check ($manifest2.RevitAddIns.AddIn.Assembly -ne $manifest.RevitAddIns.AddIn.Assembly) 'upgrade switches manifest to a new installation'
Check (Test-Path -LiteralPath $manifest.RevitAddIns.AddIn.Assembly) 'upgrade preserves old installation for rollback'
$before = [IO.File]::ReadAllText((Join-Path $addins 'mcp-servers-for-revit.addin'))
function Set-Content {
    param($LiteralPath, $Value, $Encoding)
    if ($LiteralPath -like '*agent-config.json') { throw 'Simulated configuration write failure' }
    $input | Microsoft.PowerShell.Management\Set-Content -LiteralPath $LiteralPath -Encoding $Encoding
}
$rolledBack = $false
try { & (Join-Path $Bundle 'Install.ps1') } catch { $rolledBack = $_.Exception.Message -like '*Simulated*' }
Check ($rolledBack -and [IO.File]::ReadAllText((Join-Path $addins 'mcp-servers-for-revit.addin')) -eq $before) 'failed installation restores previous manifest'
Write-Host "Installer checks passed; isolated fixture: $testRoot"
