# Creates the "Office AI" desktop icon and a Startup shortcut that starts the system when you sign in.
#   powershell -ExecutionPolicy Bypass -File scripts\windows\install-shortcuts.ps1             # both
#   powershell -ExecutionPolicy Bypass -File scripts\windows\install-shortcuts.ps1 -NoStartup  # desktop icon only
#   powershell -ExecutionPolicy Bypass -File scripts\windows\install-shortcuts.ps1 -Remove     # remove both
param(
    [switch]$NoStartup,
    [switch]$Remove
)

$ErrorActionPreference = 'Stop'
$Root = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$StartScript = Join-Path $PSScriptRoot 'start-office-ai.ps1'
$Icon = Join-Path $Root 'frontend\src\app\favicon.ico'
$PowerShell = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'

$DesktopLink = Join-Path ([Environment]::GetFolderPath('Desktop')) 'Office AI.lnk'
$StartupLink = Join-Path ([Environment]::GetFolderPath('Startup')) 'Office AI.lnk'

if ($Remove) {
    foreach ($link in $DesktopLink, $StartupLink) {
        if (Test-Path $link) { Remove-Item $link; Write-Host "Removed $link" }
    }
    exit 0
}

function New-Shortcut([string]$Path, [string]$Arguments, [int]$WindowStyle, [string]$Description) {
    $shell = New-Object -ComObject WScript.Shell
    $s = $shell.CreateShortcut($Path)
    $s.TargetPath = $PowerShell
    $s.Arguments = "-NoProfile -ExecutionPolicy Bypass -File `"$StartScript`" $Arguments".Trim()
    $s.WorkingDirectory = $Root
    $s.WindowStyle = $WindowStyle  # 1 = normal, 7 = minimized
    $s.Description = $Description
    if (Test-Path $Icon) { $s.IconLocation = "$Icon,0" }
    $s.Save()
    Write-Host "Created $Path"
}

New-Shortcut $DesktopLink '' 1 'Start Office AI and open it in the browser'
if (-not $NoStartup) {
    New-Shortcut $StartupLink '-NoBrowser' 7 'Start Office AI when you sign in to Windows'
}
