# Syncs this codebase with its copy inside Office AI (project PLATFORM, repository "office-ai"), so the AI team can work on it.
#   push : this folder's branch (default: the current one) -> the system's "main". Fast-forward only unless -Force.
#   pull : the system's "main" (AI work approved and merged in the app) -> local branch "ai/main", to review and merge here.
#   powershell -ExecutionPolicy Bypass -File scripts\windows\platform-repo.ps1 pull
#   git diff master ai/main            # review
#   git merge ai/main                  # take it
param(
    [Parameter(Mandatory)][ValidateSet('push', 'pull')][string]$Action,
    [string]$Branch,
    [switch]$Force,
    [string]$RepoId = '01a14fcadd7d7e71acce0ffbbec1d2f9'
)

$ErrorActionPreference = 'Stop'
$Root = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$Origin = "/repos/$RepoId/origin.git"

if (-not $Branch) { $Branch = (git -C $Root rev-parse --abbrev-ref HEAD).Trim() }
if ($Action -eq 'push') {
    $dirty = git -C $Root status --porcelain
    if ($dirty) { Write-Host 'Note: uncommitted changes are not pushed; only commits are.' }
    $refspec = "$(if ($Force) { '+' })refs/heads/${Branch}:refs/heads/main"
    # Run git as root, then give the files back to the API's user (uid 1654) so the app can keep writing to the repository.
    $script = "git -C /src push $Origin $refspec && chown -R 1654:1654 $Origin"
} else {
    $script = "git -C /src fetch $Origin +refs/heads/main:refs/heads/ai/main && git -C /src log --oneline -5 ai/main"
}

$ErrorActionPreference = 'Continue'
docker run --rm -v "officeai-repos:/repos" -v "${Root}:/src" alpine:3.20 sh -c "apk add --no-cache -q git >/dev/null && git config --global --add safe.directory '*' && $script"
if ($LASTEXITCODE -ne 0) { Write-Host "Failed (exit $LASTEXITCODE)."; exit $LASTEXITCODE }
Write-Host "Done: $Action"
