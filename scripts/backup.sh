#!/usr/bin/env bash
# Backs up the PostgreSQL database (including auth-cookie keys and the audit log) and the Git repositories volume.
# Usage: scripts/backup.sh [output-dir]       (run from the repository root; the stack must be up)
set -euo pipefail

STAMP="$(date -u +%Y%m%dT%H%M%SZ)"
OUT="${1:-backups/$STAMP}"
mkdir -p "$OUT"
OUT_ABS="$(cd "$OUT" && pwd)"
REPOS_VOLUME="${REPOS_VOLUME:-officeai-repos}"

echo "→ database"
docker compose exec -T db pg_dump -U officeai -d officeai --format=custom --no-owner > "$OUT/db.dump"

echo "→ repositories volume ($REPOS_VOLUME)"
MSYS_NO_PATHCONV=1 docker run --rm -v "$REPOS_VOLUME":/data:ro -v "$OUT_ABS":/backup alpine:3.20 \
  tar -czf /backup/repos.tar.gz -C /data .

( cd "$OUT" && sha256sum db.dump repos.tar.gz > SHA256SUMS )
echo "✓ backup written to $OUT"
cat "$OUT/SHA256SUMS"
