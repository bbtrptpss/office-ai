# Starts Office AI on Windows: Docker Desktop, then the compose stack, then opens the web app.
# Used by the desktop icon and the Windows Startup shortcut (see install-shortcuts.ps1).
param(
    # Start everything but do not open the browser (used at sign-in).
    [switch]$NoBrowser
)

$ErrorActionPreference = 'Stop'
$Root = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$WebUrl = 'http://localhost:3000'
$ApiHealth = 'http://localhost:5000/health'
$LogFile = Join-Path $env:LOCALAPPDATA 'OfficeAI\start.log'

New-Item -ItemType Directory -Force (Split-Path $LogFile) | Out-Null
$Host.UI.RawUI.WindowTitle = 'Office AI - starting'

function Log([string]$Message) {
    $line = '{0:yyyy-MM-dd HH:mm:ss}  {1}' -f (Get-Date), $Message
    Write-Host $line
    Add-Content -Path $LogFile -Value $line -Encoding utf8
}

# PowerShell 5.1 turns a native command's stderr into errors, so docker calls run with 'Continue'
# and are judged by their exit code.
function Test-DockerEngine {
    $ErrorActionPreference = 'Continue'
    docker info --format '{{.ServerVersion}}' *> $null
    return $LASTEXITCODE -eq 0
}

function Wait-Until([scriptblock]$Condition, [int]$Seconds, [string]$What) {
    $deadline = (Get-Date).AddSeconds($Seconds)
    while ((Get-Date) -lt $deadline) {
        if (& $Condition) { return $true }
        Start-Sleep -Seconds 3
    }
    Log "Timed out after $Seconds s waiting for $What"
    return $false
}

function Test-Url([string]$Url) {
    try {
        $r = Invoke-WebRequest -Uri $Url -UseBasicParsing -TimeoutSec 5
        return $r.StatusCode -lt 500
    } catch {
        return $false
    }
}

try {
    Log "Office AI start ($Root)"

    if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
        throw 'Docker is not installed. Install Docker Desktop first.'
    }

    if (-not (Test-DockerEngine)) {
        $desktop = Join-Path $env:ProgramFiles 'Docker\Docker\Docker Desktop.exe'
        if (-not (Test-Path $desktop)) { throw "Docker Desktop not found at $desktop" }
        Log 'Starting Docker Desktop...'
        Start-Process -FilePath $desktop
        if (-not (Wait-Until { Test-DockerEngine } 240 'the Docker engine')) {
            throw 'Docker Desktop did not become ready. Open it manually and try again.'
        }
    }
    Log 'Docker engine is ready'

    Log 'Starting containers (docker compose up -d)...'
    $ErrorActionPreference = 'Continue'
    docker compose --project-directory $Root -f (Join-Path $Root 'docker-compose.yml') up -d 2>&1 |
        ForEach-Object { Log "  $_" }
    $composeExit = $LASTEXITCODE
    $ErrorActionPreference = 'Stop'
    if ($composeExit -ne 0) { throw "docker compose failed (exit $composeExit)" }

    # Claude Code bridge for the dashboard chat (only when it is set up in .env and Node is installed).
    $envText = Get-Content (Join-Path $Root '.env') -Raw -ErrorAction SilentlyContinue
    if ($envText -match '(?m)^CLAUDE_BRIDGE_TOKEN=\S{24,}' -and (Get-Command node -ErrorAction SilentlyContinue)) {
        if (Test-Url 'http://127.0.0.1:8765/health') { Log 'Claude bridge is already running' }
        else {
            Start-Process -FilePath (Get-Command node).Source -ArgumentList "`"$(Join-Path $PSScriptRoot 'claude-bridge.mjs')`"" -WorkingDirectory $Root -WindowStyle Hidden
            Log 'Started the Claude bridge'
        }
    }

    if (-not (Wait-Until { Test-Url $ApiHealth } 180 'the API')) { throw 'The API did not start. See: docker compose logs api' }
    if (-not (Wait-Until { Test-Url $WebUrl } 120 'the web app')) { throw 'The web app did not start. See: docker compose logs web' }
    Log "Office AI is running at $WebUrl"

    if (-not $NoBrowser) { Start-Process $WebUrl }
    exit 0
} catch {
    Log "ERROR: $($_.Exception.Message)"
    $Host.UI.RawUI.WindowTitle = 'Office AI - failed to start'
    Write-Host ''
    Write-Host "Log: $LogFile"
    Read-Host 'Press Enter to close' | Out-Null
    exit 1
}
