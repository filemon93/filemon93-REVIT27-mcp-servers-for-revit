[CmdletBinding()]
param([switch]$CheckOnly)
$ErrorActionPreference = 'Stop'
$bundle = $PSScriptRoot
$version = (Get-Content -LiteralPath (Join-Path $bundle 'version.txt') -Raw).Trim()
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw 'Invalid package version.' }
$hashes = Get-Content -LiteralPath (Join-Path $bundle 'checksums.json') -Raw | ConvertFrom-Json
foreach ($entry in $hashes) {
    $file = [IO.Path]::GetFullPath((Join-Path $bundle $entry.path))
    if (-not $file.StartsWith($bundle + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid checksum path.' }
    if (-not (Test-Path -LiteralPath $file -PathType Leaf) -or (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash -ne $entry.sha256) {
        throw "Package integrity check failed: $($entry.path)"
    }
}
if ($CheckOnly) { Write-Host "Package $version verified. No installation changes made."; return }
if (Get-Process Revit -ErrorAction SilentlyContinue | Where-Object { $_.Path -like '*Revit 2027\Revit.exe' }) {
    throw 'Close Revit 2027 before installation. The installer does not close or save projects.'
}
if (-not (Test-Path -LiteralPath 'C:\Program Files\Autodesk\Revit 2027\Revit.exe')) { throw 'Revit 2027 is required.' }
$clientId = '090A4C8C-61DC-426D-87DF-E4BAE0F80EC1'
$systemAddins = Join-Path $env:ProgramData 'Autodesk\Revit\Addins\2027'
foreach ($file in Get-ChildItem -LiteralPath $systemAddins -Filter '*.addin' -ErrorAction SilentlyContinue) {
    [xml]$xml = Get-Content -LiteralPath $file.FullName
    if ($xml.SelectNodes('//AddIn') | Where-Object { $_.ClientId -eq $clientId -or $_.AddInId -eq $clientId }) {
        throw "A system installation exists: $($file.FullName). Ask the administrator to remove that entry first."
    }
}
$appRoot = Join-Path $env:LOCALAPPDATA 'RevitMCP27'
$install = Join-Path $appRoot ("$version-" + [Guid]::NewGuid().ToString('N'))
$addins = Join-Path $env:APPDATA 'Autodesk\Revit\Addins\2027'
$backup = Join-Path $appRoot ('backups\' + [DateTime]::Now.ToString('yyyyMMdd-HHmmss-ffff'))
New-Item -ItemType Directory -Path $install,$addins,$backup -Force | Out-Null
$originals = @{}
$manifest = Join-Path $addins 'mcp-servers-for-revit.addin'
$manifestExisted = Test-Path -LiteralPath $manifest
try {
    Copy-Item -LiteralPath (Join-Path $bundle 'addin') -Destination $install -Recurse
    Copy-Item -LiteralPath (Join-Path $bundle 'server') -Destination $install -Recurse
    # Generate absolute command paths so first launch works without a Settings visit.
    $commandDll = Join-Path $install 'addin\revit_mcp_plugin\Commands\RevitMCPCommandSet\2027\RevitMCPCommandSet.dll'
    $definition = Get-Content -LiteralPath (Join-Path $bundle 'addin\revit_mcp_plugin\Commands\RevitMCPCommandSet\command.json') -Raw | ConvertFrom-Json
    $commands = @($definition.commands | ForEach-Object {
        @{commandName=$_.commandName; description=$_.description; assemblyPath=$commandDll; enabled=$true; supportedRevitVersions=@('2027')}
    })
    @{commands=$commands} | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $install 'addin\revit_mcp_plugin\Commands\commandRegistry.json') -Encoding UTF8
    # Keep other add-ins in shared manifests and retain a complete rollback copy.
    foreach ($file in Get-ChildItem -LiteralPath $addins -Filter '*.addin') {
        [xml]$xml = Get-Content -LiteralPath $file.FullName
        $matches = @($xml.SelectNodes('//AddIn') | Where-Object { $_.ClientId -eq $clientId -or $_.AddInId -eq $clientId })
        if ($matches.Count -gt 0 -or $file.FullName -eq $manifest) {
            if ($file.FullName -eq $manifest -and $xml.SelectNodes('//AddIn').Count -gt $matches.Count) {
                throw "Target manifest contains unrelated add-ins: $manifest. Rename it before installing."
            }
            $originals[$file.FullName] = [IO.File]::ReadAllBytes($file.FullName)
            Copy-Item -LiteralPath $file.FullName -Destination $backup
            foreach ($entry in $matches) { $entry.ParentNode.RemoveChild($entry) | Out-Null }
            $xml.Save($file.FullName)
        }
    }
    [xml]$manifestXml = Get-Content -LiteralPath (Join-Path $bundle 'addin\mcp-servers-for-revit.addin')
    $manifestXml.RevitAddIns.AddIn.Assembly = [string](Join-Path $install 'addin\revit_mcp_plugin\RevitMCPPlugin.dll')
    $manifestXml.Save($manifest)
    $node = Join-Path $install 'server\node.exe'
    $entryPoint = Join-Path $install 'server\build\index.js'
    @{mcpServers=@{'revit-mcp-27'=@{command=$node; args=@($entryPoint)}}} | ConvertTo-Json -Depth 5 |
        Set-Content -LiteralPath (Join-Path $appRoot 'agent-config.json') -Encoding UTF8
    $tomlNode = $node.Replace('\','/')
    $tomlEntry = $entryPoint.Replace('\','/')
    "[mcp_servers.revit-mcp-27]`ncommand = `"$tomlNode`"`nargs = [`"$tomlEntry`"]" |
        Set-Content -LiteralPath (Join-Path $appRoot 'agent-config.toml') -Encoding UTF8
    Write-Host "Installed Revit MCP $version for Revit 2027."
    Write-Host "Agent configuration: $appRoot\agent-config.json (or agent-config.toml)"
    Write-Host "Previous manifests: $backup"
    Write-Host 'Start Revit, open a model, then Add-Ins > Revit MCP Switch.'
} catch {
    if (-not $manifestExisted -and (Test-Path -LiteralPath $manifest)) { Remove-Item -LiteralPath $manifest }
    foreach ($path in $originals.Keys) { [IO.File]::WriteAllBytes($path, $originals[$path]) }
    throw
}
