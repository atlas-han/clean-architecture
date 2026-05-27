#!/usr/bin/env bash
# PostToolUse: run `dotnet format whitespace` scoped to the edited file so the model sees clean output on next read.
# Best-effort: failures here must not break the user's session, so we never exit non-zero.
set -uo pipefail

input="$(cat)"

file_path="$(printf '%s' "$input" | python3 -c '
import json, sys
try:
    data = json.load(sys.stdin)
except Exception:
    sys.exit(0)
tool_input = data.get("tool_input", data)
print(tool_input.get("file_path", "") or "")
' 2>/dev/null || true)"

# Only format .cs files (skip csproj / json / md / etc.)
if [[ -z "$file_path" || "$file_path" != *.cs ]]; then
  exit 0
fi

# Skip generated / build output
case "$file_path" in
  */obj/*|*/bin/*) exit 0 ;;
esac

project_dir="${CLAUDE_PROJECT_DIR:-$(pwd)}"
cd "$project_dir" || exit 0

if ! command -v dotnet >/dev/null 2>&1; then
  exit 0
fi

rel_path="${file_path#$project_dir/}"

# Whitespace-only pass is fast and safe; full style/analyzer pass would touch unrelated files.
dotnet format whitespace --include "$rel_path" --no-restore >/dev/null 2>&1 || true

exit 0
