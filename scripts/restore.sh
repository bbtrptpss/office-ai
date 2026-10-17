#!/usr/bin/env bash
# Restores a backup made by backup.sh.
# Usage: scripts/restore.sh <backup-dir>
#   RESTORE_DB=<name>        restore into another database instead of "officeai" (used for restore drills)
#   RESTORE_VOLUME=<name>    restore repositories into another volume instead of "officeai-repos"
# Restoring over the live database stops the api, web and sandbox-runner services first.
set -euo pipefail

DIR="${1:?usage: scripts/restore.sh <backup-dir>}"
DIR_ABS="$(cd "$DIR" && pwd)"
DB="${RESTORE_DB:-officeai}"
VOLUME="${RESTORE_VOLUME:-officeai-repos}"

( cd "$DIR" && sha256sum -c SHA256SUMS )

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
  sh -c 'rm -rf /data/* /data/.[!.]* 2>/dev/null; tar -xzf /backup/repos.tar.gz -C /data'

if [[ "$LIVE" == true ]]; then
  echo "→ starting services"
  docker compose start sandbox-runner api web
fi
echo "✓ restore complete"
