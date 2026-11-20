#!/bin/bash
set -e

export PGDATA="/var/lib/postgresql/18/docker"

echo "Waiting for primary..."
until pg_isready -h pg_primary -p 5432 >/dev/null 2>&1; do
  sleep 2
done
echo "Primary is ready."

if [ ! -f "$PGDATA/PG_VERSION" ]; then
  echo "Cloning primary data with pg_basebackup..."
  mkdir -p "$PGDATA"
  chown -R postgres:postgres /var/lib/postgresql
  export PGPASSWORD=replpass
  gosu postgres pg_basebackup \
    -h pg_primary -p 5432 -U replicator \
    -D "$PGDATA" -Fp -Xs -P -R
  echo "Clone finished."
fi

exec docker-entrypoint.sh postgres
