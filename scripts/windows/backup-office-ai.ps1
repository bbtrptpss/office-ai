# Backs up Office AI on Windows: the PostgreSQL database and the Git repositories volume, like scripts/backup.sh.
# Run by the daily scheduled task (see install-backup-task.ps1) or by hand. Keeps the newest -Keep verified backups.
#   powershell -ExecutionPolicy Bypass -File scripts\windows\backup-office-ai.ps1
# A backup is written to a .partial-* folder and gets its final name only after it has been verified, so a failed run leaves nothing behind.
# When Docker or the database is down they are started first (only the database, and it is stopped again afterwards); -NoStart turns that off.
# Each new backup is also encrypted with gpg (AES-256) and copied to -OffsiteRoot, keeping the newest -OffsiteKeep; -NoOffsite turns that off.
# The passphrase comes from set-backup-passphrase.ps1. Exit codes: 0 all good, 1 backup failed, 2 backup good but the off-site copy failed.
# To read an off-site copy: gpg --decrypt <stamp>.tar.gpg > backup.tar, then tar -xf backup.tar and run scripts/restore.sh on the folder.
param(
    [string]$OutRoot,
    [int]$Keep = 14,
    [switch]$NoStart,
    [string]$OffsiteRoot = 'G:\My Drive\OfficeAI-Backups',
    [int]$OffsiteKeep = 14,
    [switch]$NoOffsite
)

$ErrorActionPreference = 'Stop'
$Root = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
if (-not $OutRoot) { $OutRoot = Join-Path $Root 'backups' }
$LogFile = Join-Path $env:LOCALAPPDATA 'OfficeAI\backup.log'
$Compose = @('compose', '--project-directory', $Root, '-f', (Join-Path $Root 'docker-compose.yml'))
$BackupFiles = @('db.dump', 'repos.tar.gz')
$PassphraseFile = Join-Path $env:LOCALAPPDATA 'OfficeAI\backup-passphrase.dat'
$Gpg = Join-Path $env:ProgramFiles 'Git\usr\bin\gpg.exe'
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

function Test-DockerEngine {
    $ErrorActionPreference = 'Continue'
    & docker info --format '{{.ServerVersion}}' *> $null
    return $LASTEXITCODE -eq 0
}

function Test-DatabaseRunning {
    $ErrorActionPreference = 'Continue'
    $services = & docker @Compose ps --status running --services 2>$null
    return ($LASTEXITCODE -eq 0) -and (@($services) -contains 'db')
}

# Returns $true when this run started the database, so it can be stopped again afterwards.
function Start-Database {
    if (-not (Test-DockerEngine)) {
        if ($NoStart) { throw 'The Docker engine is not running.' }
        $desktop = Join-Path $env:ProgramFiles 'Docker\Docker\Docker Desktop.exe'
        if (-not (Test-Path $desktop)) { throw "Docker Desktop not found at $desktop" }
        Log '  starting Docker Desktop'
        Start-Process -FilePath $desktop
        $deadline = (Get-Date).AddSeconds(240)
        while (-not (Test-DockerEngine)) {
            if ((Get-Date) -ge $deadline) { throw 'Docker Desktop did not become ready within 240 seconds.' }
            Start-Sleep -Seconds 5
        }
    }
    if (Test-DatabaseRunning) { return $false }
    if ($NoStart) { throw 'The database is not running.' }
    Log '  starting the database'
    Invoke-Docker ($Compose + @('up', '-d', '--wait', '--wait-timeout', '120', 'db'))
    return $true
}

# Complete means: every file is present, not empty and matches SHA256SUMS.
function Test-BackupVerified([string]$Path) {
    $sumsFile = Join-Path $Path 'SHA256SUMS'
    if (-not (Test-Path -LiteralPath $sumsFile)) { return $false }
    $expected = @{}
    foreach ($line in Get-Content -LiteralPath $sumsFile) {
        if ($line -match '^([0-9a-fA-F]{64})\s+\*?(\S+)$') { $expected[$Matches[2]] = $Matches[1].ToLowerInvariant() }
    }
    foreach ($name in $BackupFiles) {
        $file = Join-Path $Path $name
        if (-not $expected.ContainsKey($name) -or -not (Test-Path -LiteralPath $file)) { return $false }
        if ((Get-Item -LiteralPath $file).Length -eq 0) { return $false }
        if ((Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant() -ne $expected[$name]) { return $false }
    }
    return $true
}

# Newest first. Folders that are empty or fail verification are not backups: they never count towards -Keep and are never removed here.
function Get-VerifiedBackups([string]$Path) {
    return @(Get-ChildItem -LiteralPath $Path -Directory | Where-Object { $_.Name -match '^\d{8}T\d{6}Z$' } |
        Sort-Object Name -Descending | Where-Object { Test-BackupVerified $_.FullName })
}

function Remove-OldBackups([string]$Path, [int]$KeepCount) {
    foreach ($dir in @(Get-VerifiedBackups $Path | Select-Object -Skip $KeepCount)) {
        Remove-Item -LiteralPath $dir.FullName -Recurse -Force
        Log "  removed old backup $($dir.Name)"
    }
}

# Protected with Windows DPAPI: only this Windows user on this computer can read it.
function Get-BackupPassphrase {
    if (-not (Test-Path -LiteralPath $PassphraseFile)) { throw 'No backup passphrase is set. Run scripts\windows\set-backup-passphrase.ps1 once.' }
    $secure = (Get-Content -LiteralPath $PassphraseFile -Raw).Trim() | ConvertTo-SecureString
    $ptr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)
    try { return [Runtime.InteropServices.Marshal]::PtrToStringBSTR($ptr) }
    finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($ptr) }
}

# The passphrase goes through a short-lived file in the user's own profile folder, never on stdin: when the console input
# code page is UTF-8, .NET writes a BOM into a redirected stdin, gpg takes it as part of the passphrase, and the result
# opens only under the same conditions and never with the passphrase as typed. It stays off the command line as well.
function Invoke-Gpg([string[]]$Arguments, [string]$Passphrase) {
    if (-not (Test-Path -LiteralPath $Gpg)) { throw "gpg not found at $Gpg" }
    $passFile = Join-Path (Split-Path $PassphraseFile) ("gpg-{0}.tmp" -f [Guid]::NewGuid().ToString('N'))
    try {
        [IO.File]::WriteAllBytes($passFile, (New-Object Text.UTF8Encoding $false).GetBytes($Passphrase))
        $common = @('--batch', '--yes', '--no-tty', '--pinentry-mode', 'loopback', '--passphrase-file', $passFile, '--no-symkey-cache')
        $psi = New-Object Diagnostics.ProcessStartInfo
        $psi.FileName = $Gpg
        $psi.Arguments = (($common + $Arguments) | ForEach-Object { '"' + ($_ -replace '\\', '/') + '"' }) -join ' '
        $psi.UseShellExecute = $false
        $psi.CreateNoWindow = $true
        $psi.RedirectStandardError = $true
        $psi.RedirectStandardOutput = $true
        $process = [Diagnostics.Process]::Start($psi)
        $stdout = $process.StandardOutput.ReadToEndAsync()
        $stderr = $process.StandardError.ReadToEndAsync()
        $process.WaitForExit()
        if ($process.ExitCode -ne 0) { throw "gpg failed (exit $($process.ExitCode)): $($stderr.Result.Trim())" }
    } finally {
        Remove-Item -LiteralPath $passFile -Force -ErrorAction SilentlyContinue
    }
}

function Send-OffsiteCopy([string]$BackupDir, [string]$Stamp) {
    $parent = Split-Path $OffsiteRoot
    if (-not (Test-Path -LiteralPath $parent)) { throw "$parent is not available. Is Google Drive running and signed in?" }
    $passphrase = Get-BackupPassphrase
    $work = Join-Path (Split-Path $BackupDir) ".partial-offsite-$Stamp"
    $name = "$Stamp.tar.gpg"
    $temp = Join-Path $OffsiteRoot "$name.tmp"
    New-Item -ItemType Directory -Force $work | Out-Null
    try {
        $tar = Join-Path $work 'backup.tar'
        $encrypted = Join-Path $work $name
        $check = Join-Path $work 'check.tar'
        & (Join-Path $env:SystemRoot 'System32\tar.exe') -cf $tar -C (Split-Path $BackupDir) (Split-Path $BackupDir -Leaf)
        if ($LASTEXITCODE -ne 0) { throw "tar failed (exit $LASTEXITCODE)" }
        Invoke-Gpg @('--symmetric', '--cipher-algo', 'AES256', '--s2k-digest-algo', 'SHA512', '--s2k-count', '65011712', '--output', $encrypted, $tar) $passphrase
        # Prove the file opens with this passphrase before relying on it.
        Invoke-Gpg @('--decrypt', '--output', $check, $encrypted) $passphrase
        if ((Get-FileHash -LiteralPath $check).Hash -ne (Get-FileHash -LiteralPath $tar).Hash) { throw 'The encrypted copy did not decrypt to the same data.' }

        New-Item -ItemType Directory -Force $OffsiteRoot | Out-Null
        Copy-Item -LiteralPath $encrypted -Destination $temp -Force
        if ((Get-FileHash -LiteralPath $temp).Hash -ne (Get-FileHash -LiteralPath $encrypted).Hash) { throw 'The copy in the off-site folder does not match.' }
        Move-Item -LiteralPath $temp -Destination (Join-Path $OffsiteRoot $name) -Force
        Log "  encrypted copy placed in $OffsiteRoot (syncing to the cloud is up to the sync app)"

        Get-ChildItem -LiteralPath $OffsiteRoot -File | Where-Object { $_.Name -match '^\d{8}T\d{6}Z\.tar\.gpg$' } | Sort-Object Name -Descending |
            Select-Object -Skip $OffsiteKeep | ForEach-Object {
                Remove-Item -LiteralPath $_.FullName -Force
                Log "  removed old off-site copy $($_.Name)"
            }
    } finally {
        Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
        if (Test-Path -LiteralPath $temp) { Remove-Item -LiteralPath $temp -Force -ErrorAction SilentlyContinue }
    }
}

# Best effort: a notification that cannot be shown must not hide the failure, which is already in the log and the exit code.
function Show-FailureNotice([string]$Message) {
    try {
        $null = [Windows.UI.Notifications.ToastNotificationManager, Windows.UI.Notifications, ContentType = WindowsRuntime]
        $null = [Windows.Data.Xml.Dom.XmlDocument, Windows.Data.Xml.Dom.XmlDocument, ContentType = WindowsRuntime]
        $xml = New-Object Windows.Data.Xml.Dom.XmlDocument
        $escaped = [Security.SecurityElement]::Escape($Message)
        $xml.LoadXml("<toast><visual><binding template='ToastGeneric'><text>Office AI backup failed</text><text>$escaped</text></binding></visual></toast>")
        $appId = '{1AC14E77-02E7-4E5D-B744-2EB1AE5198B7}\WindowsPowerShell\v1.0\powershell.exe'
        [Windows.UI.Notifications.ToastNotificationManager]::CreateToastNotifier($appId).Show((New-Object Windows.UI.Notifications.ToastNotification $xml))
    } catch {
        Log "  could not show the failure notification: $($_.Exception.Message)"
    }
}

$exitCode = 0
$partial = $null
$startedDatabase = $false
try {
    New-Item -ItemType Directory -Force $OutRoot | Out-Null
    # Left by a run that was killed before it could clean up.
    Get-ChildItem -LiteralPath $OutRoot -Directory -Filter '.partial-*' | ForEach-Object { Remove-Item -LiteralPath $_.FullName -Recurse -Force }

    $stamp = (Get-Date).ToUniversalTime().ToString('yyyyMMddTHHmmssZ')
    $out = Join-Path $OutRoot $stamp
    Log "Backup to $out"

    $startedDatabase = Start-Database

    $partial = Join-Path $OutRoot ".partial-$stamp"
    New-Item -ItemType Directory -Force $partial | Out-Null

    Invoke-Docker ($Compose + @('exec', '-T', 'db', 'pg_dump', '-U', 'officeai', '-d', 'officeai', '--format=custom', '--no-owner', '-f', '/tmp/officeai-backup.dump'))
    Invoke-Docker ($Compose + @('exec', '-T', 'db', 'pg_restore', '--list', '/tmp/officeai-backup.dump'))
    Invoke-Docker ($Compose + @('cp', 'db:/tmp/officeai-backup.dump', (Join-Path $partial 'db.dump')))
    Invoke-Docker ($Compose + @('exec', '-T', 'db', 'rm', '-f', '/tmp/officeai-backup.dump'))
    Log '  database done'

    Invoke-Docker @('run', '--rm', '-v', 'officeai-repos:/data:ro', '-v', "${partial}:/backup", 'alpine:3.20', 'sh', '-c', 'tar -czf /backup/repos.tar.gz -C /data . && tar -tzf /backup/repos.tar.gz > /dev/null')
    Log '  repositories done'

    # Same SHA256SUMS format as backup.sh, so scripts/restore.sh can verify it.
    $sums = foreach ($f in $BackupFiles) { '{0}  {1}' -f (Get-FileHash (Join-Path $partial $f) -Algorithm SHA256).Hash.ToLowerInvariant(), $f }
    [IO.File]::WriteAllText((Join-Path $partial 'SHA256SUMS'), ($sums -join "`n") + "`n")
    if (-not (Test-BackupVerified $partial)) { throw 'The backup failed verification.' }

    Rename-Item -LiteralPath $partial -NewName $stamp
    $partial = $null
    $size = (Get-ChildItem -LiteralPath $out | Measure-Object Length -Sum).Sum / 1MB
    Log ('  written and verified: {0:N1} MB' -f $size)

    Remove-OldBackups $OutRoot $Keep

    if (-not $NoOffsite) {
        # The local backup is good either way, so this failure gets its own message and exit code.
        try {
            Send-OffsiteCopy $out $stamp
        } catch {
            $exitCode = 2
            Log "OFF-SITE ERROR: $($_.Exception.Message)"
            Show-FailureNotice "Local backup is fine, but the off-site copy failed. See $LogFile"
        }
    }
} catch {
    $exitCode = 1
    $error0 = $_.Exception.Message
    Log "ERROR: $error0"
    $latest = @(Get-VerifiedBackups $OutRoot | Select-Object -First 1)
    $lastGood = if ($latest.Count -gt 0) { "Last good backup: $($latest[0].Name)." } else { 'No good backup exists.' }
    Log "  $lastGood"
    Show-FailureNotice "$lastGood See $LogFile"
} finally {
    if ($partial -and (Test-Path -LiteralPath $partial)) { Remove-Item -LiteralPath $partial -Recurse -Force -ErrorAction SilentlyContinue }
    if ($startedDatabase) {
        try { Invoke-Docker ($Compose + @('stop', 'db')); Log '  stopped the database again' }
        catch { Log "  could not stop the database: $($_.Exception.Message)" }
    }
}
exit $exitCode
