#!/usr/bin/env bash
# Show Flyway migration status (applied / pending / failed).
set -euo pipefail
cd "$(dirname "$0")/.."
exec docker compose run --rm flyway info "$@"
