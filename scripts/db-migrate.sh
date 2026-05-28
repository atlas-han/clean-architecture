#!/usr/bin/env bash
# Apply pending Flyway migrations to the local MSSQL.
# Reads .env at repo root for credentials.
set -euo pipefail
cd "$(dirname "$0")/.."
exec docker compose run --rm flyway migrate "$@"
