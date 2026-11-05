#!/usr/bin/env bash
# Restores a backup made by backup.sh.
# Usage: scripts/restore.sh <backup-dir>
#   RESTORE_DB=<name>        restore into another database instead of "officeai" (used for restore drills)
#   RESTORE_VOLUME=<name>    restore repositories into another volume instead of "officeai-repos"
# Restoring over the live database stops the api, web and sandbox-runner services first.
set -euo pipefail

DIR="${1:?usage: scripts/restore.sh <backup-dir>}"
# Git Bash shows some Windows folders under its own names (the Temp folder is /tmp), which Docker cannot find; pwd -W gives the real path.
DIR_ABS="$(cd "$DIR" && (pwd -W 2>/dev/null || pwd))"
DB="${RESTORE_DB:-officeai}"
VOLUME="${RESTORE_VOLUME:-officeai-repos}"

( cd "$DIR" && sha256sum -c SHA256SUMS )

# Everything that can be checked is checked before anything is stopped or removed.
echo "→ checking the backup can be read"
docker compose exec -T db pg_isready -q -U officeai \
  || { echo "✗ the database is not running; nothing was changed" >&2; exit 1; }
docker compose exec -T db pg_restore --list < "$DIR/db.dump" > /dev/null \
  || { echo "✗ db.dump cannot be read; nothing was changed" >&2; exit 1; }
MSYS_NO_PATHCONV=1 docker run --rm -v "$DIR_ABS":/backup:ro alpine:3.20 tar -tzf /backup/repos.tar.gz > /dev/null \
  || { echo "✗ Docker cannot read repos.tar.gz from $DIR_ABS; nothing was changed" >&2; exit 1; }

LIVE=false
if [[ "$DB" == "officeai" ]]; then
  LIVE=true
  echo "→ stopping services that use the database"
  docker compose stop api web sandbox-runner
fi

echo "→ database → $DB"
if [[ "$LIVE" == false ]]; then
  docker compose exec -T db dropdb -U officeai --if-exists "$DB"
  docker compose exec -T db createdb -U officeai "$DB"
fi
docker compose exec -T db pg_restore -U officeai -d "$DB" --clean --if-exists --no-owner < "$DIR/db.dump"

echo "→ repositories → volume $VOLUME"
docker volume create "$VOLUME" > /dev/null
MSYS_NO_PATHCONV=1 docker run --rm -v "$VOLUME":/data -v "$DIR_ABS":/backup:ro alpine:3.20 \
  sh -c 'test -s /backup/repos.tar.gz || { echo "repos.tar.gz is not visible; the volume was left as it is" >&2; exit 1; }; rm -rf /data/* /data/.[!.]* 2>/dev/null; tar -xzf /backup/repos.tar.gz -C /data'

if [[ "$LIVE" == true ]]; then
  echo "→ starting services"
  docker compose start sandbox-runner api web
fi
echo "✓ restore complete"
