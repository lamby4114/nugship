#!/bin/sh
# Fix ownership of the SQLite volume (Fly mounts volumes owned by root), then run the app as "node".
# `exec` keeps node as PID 1's direct replacement so SIGTERM reaches it for graceful shutdown.
set -e
if [ "$(id -u)" = "0" ]; then
  DATA_DIR="$(dirname "${DB_PATH:-/data/oreseas.db}")"
  mkdir -p "$DATA_DIR"
  chown -R node:node "$DATA_DIR"
  exec setpriv --reuid=node --regid=node --init-groups "$@"
fi
exec "$@"
