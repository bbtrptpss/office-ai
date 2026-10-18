# Registers a daily Windows scheduled task "Office AI Backup" that runs backup-office-ai.ps1 for the signed-in user.
# A run missed while the computer was off starts as soon as it is back on.
#   powershell -ExecutionPolicy Bypass -File scripts\windows\install-backup-task.ps1            # daily at 12:00
#   powershell -ExecutionPolicy Bypass -File scripts\windows\install-backup-task.ps1 -At 18:30
#   powershell -ExecutionPolicy Bypass -File scripts\windows\install-backup-task.ps1 -Remove
param(
    [string]$At = '12:00',
    [switch]$Remove
)

$ErrorActionPreference = 'Stop'
$TaskName = 'Office AI Backup'

if ($Remove) {
    Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false -ErrorAction SilentlyContinue
    Write-Host "Removed scheduled task '$TaskName'"
    exit 0
}

$script = Join-Path $PSScriptRoot 'backup-office-ai.ps1'
$powershell = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
$action = New-ScheduledTaskAction -Execute $powershell -Argument "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File `"$script`""
$trigger = New-ScheduledTaskTrigger -Daily -At $At
$settings = New-ScheduledTaskSettingsSet -StartWhenAvailable -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -ExecutionTimeLimit (New-TimeSpan -Hours 1)
Register-ScheduledTask -TaskName $TaskName -Action $action -Trigger $trigger -Settings $settings `
    -Description 'Daily backup of the Office AI database and Git repositories (keeps the newest 14).' -Force | Out-Null
Write-Host "Scheduled '$TaskName' daily at $At"
