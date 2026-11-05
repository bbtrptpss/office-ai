# Sets the passphrase that encrypts the off-site backup copies (see backup-office-ai.ps1). Run it once, by hand, in a terminal:
#   powershell -ExecutionPolicy Bypass -File scripts\windows\set-backup-passphrase.ps1
# It is stored with Windows DPAPI in %LOCALAPPDATA%\OfficeAI, outside the repository: only this Windows user on this computer can read it.
# Keep the passphrase in a password manager too. If this computer is lost, that is the only way to open the copies.
$ErrorActionPreference = 'Stop'
$File = Join-Path $env:LOCALAPPDATA 'OfficeAI\backup-passphrase.dat'
$MinLength = 16

function Read-Plain([Security.SecureString]$Secure) {
    $ptr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($Secure)
    try { return [Runtime.InteropServices.Marshal]::PtrToStringBSTR($ptr) }
    finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($ptr) }
}

if (Test-Path -LiteralPath $File) {
    Write-Host 'A passphrase is already set. Copies made with it will still need the OLD passphrase to open.'
    if ((Read-Host 'Type REPLACE to change it') -cne 'REPLACE') { Write-Host 'Nothing changed.'; exit 0 }
}

$first = Read-Host "Passphrase (at least $MinLength characters)" -AsSecureString
$second = Read-Host 'Same passphrase again' -AsSecureString
$plainFirst = Read-Plain $first
if ($plainFirst.Length -lt $MinLength) { Write-Host "Too short: use at least $MinLength characters. Nothing changed."; exit 1 }
if ($plainFirst -cne (Read-Plain $second)) { Write-Host 'The two entries differ. Nothing changed.'; exit 1 }

New-Item -ItemType Directory -Force (Split-Path $File) | Out-Null
$first | ConvertFrom-SecureString | Set-Content -LiteralPath $File -Encoding ascii
Write-Host 'Saved. Make sure the same passphrase is in your password manager.'
