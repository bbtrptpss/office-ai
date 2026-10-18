# Backs up Office AI on Windows: the PostgreSQL database and the Git repositories volume, like scripts/backup.sh.
# Run by the daily scheduled task (see install-backup-task.ps1) or by hand. Keeps the newest -Keep backups.
#   powershell -ExecutionPolicy Bypass -File scripts\windows\backup-office-ai.ps1
param(
    [string]$OutRoot,
    [int]$Keep = 14
)

$ErrorActionPreference = 'Stop'
$Root = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
if (-not $OutRoot) { $OutRoot = Join-Path $Root 'backups' }
$LogFile = Join-Path $env:LOCALAPPDATA 'OfficeAI\backup.log'
$Compose = @('compose', '--project-directory', $Root, '-f', (Join-Path $Root 'docker-compose.yml'))
New-Item -ItemType Directory -Force (Split-Path $LogFile) | Out-Null

function Log([string]$Message) {
    $line = '{0:yyyy-MM-dd HH:mm:ss}  {1}' -f (Get-Date), $Message
    Write-Host $line
    Add-Content -Path $LogFile -Value $line -Encoding utf8
}

# PowerShell 5.1 turns a native command's stderr into errors, so docker runs with 'Continue' and is judged by its exit code.
# Output goes through files inside the containers, never through a PowerShell pipe (that would corrupt the binary dump).
function Invoke-Docker([string[]]$Arguments) {
    $ErrorActionPreference = 'Continue'
    $output = & docker @Arguments 2>&1
    if ($LASTEXITCODE -ne 0) { throw "docker $($Arguments -join ' ') failed: $($output -join ' ')" }
}

try {
    $stamp = (Get-Date).ToUniversalTime().ToString('yyyyMMddTHHmmssZ')
    $out = Join-Path $OutRoot $stamp
    New-Item -ItemType Directory -Force $out | Out-Null
    Log "Backup to $out"

    Invoke-Docker ($Compose + @('exec', '-T', 'db', 'pg_dump', '-U', 'officeai', '-d', 'officeai', '--format=custom', '--no-owner', '-f', '/tmp/officeai-backup.dump'))
    Invoke-Docker ($Compose + @('cp', 'db:/tmp/officeai-backup.dump', (Join-Path $out 'db.dump')))
    Invoke-Docker ($Compose + @('exec', '-T', 'db', 'rm', '-f', '/tmp/officeai-backup.dump'))
    Log '  database done'

    Invoke-Docker @('run', '--rm', '-v', 'officeai-repos:/data:ro', '-v', "${out}:/backup", 'alpine:3.20', 'tar', '-czf', '/backup/repos.tar.gz', '-C', '/data', '.')
    Log '  repositories done'

    # Same SHA256SUMS format as backup.sh, so scripts/restore.sh can verify it.
    $sums = foreach ($f in 'db.dump', 'repos.tar.gz') { '{0}  {1}' -f (Get-FileHash (Join-Path $out $f) -Algorithm SHA256).Hash.ToLowerInvariant(), $f }
    [IO.File]::WriteAllText((Join-Path $out 'SHA256SUMS'), ($sums -join "`n") + "`n")
    $size = (Get-ChildItem $out | Measure-Object Length -Sum).Sum / 1MB
    Log ('  written: {0:N1} MB' -f $size)

    $old = Get-ChildItem $OutRoot -Directory | Where-Object { $_.Name -match '^\d{8}T\d{6}Z$' } | Sort-Object Name -Descending | Select-Object -Skip $Keep
    foreach ($dir in $old) { Remove-Item $dir.FullName -Recurse -Force; Log "  removed old backup $($dir.Name)" }
    exit 0
} catch {
    Log "ERROR: $($_.Exception.Message)"
    exit 1
}
