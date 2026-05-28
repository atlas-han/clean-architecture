#!/usr/bin/env bash
# Validate that applied migrations match the SQL files on disk.
set -euo pipefail
cd "$(dirname "$0")/.."
exec docker compose run --rm flyway validate "$@"
