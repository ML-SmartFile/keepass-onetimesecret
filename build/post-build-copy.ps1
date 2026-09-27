<#
Copies the built plugin DLL into the local KeePass Plugins folder.
Edit $KeePassPluginsDir once for your machine.

KeePass caches loaded plugins - restart KeePass after every run of this script.
#>

$ErrorActionPreference = 'Stop'

$KeePassPluginsDir = 'C:\KeePass\Plugins'
$RepoRoot = Split-Path -Parent $PSScriptRoot
$BuiltDll = Join-Path $RepoRoot 'src\OneTimeSecret\bin\Debug\net472\OneTimeSecret.dll'

if (-not (Test-Path -LiteralPath $BuiltDll)) {
    throw "Built DLL not found at $BuiltDll - build the project first."
}

if (-not (Test-Path -LiteralPath $KeePassPluginsDir)) {
    throw "KeePass Plugins folder not found at $KeePassPluginsDir - check `$KeePassPluginsDir above."
}

Copy-Item -LiteralPath $BuiltDll -Destination $KeePassPluginsDir -Force
Write-Output "Copied OneTimeSecret.dll to $KeePassPluginsDir. Restart KeePass to pick it up."
