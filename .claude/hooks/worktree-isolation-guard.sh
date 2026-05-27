#!/usr/bin/env bash
# PreToolUse guard: require CODE edits to happen inside a git worktree (.claude/worktrees/<name>/).
# Source/test code edited in the main checkout is blocked (exit 2) with guidance to EnterWorktree first.
# Non-code files (.claude config, docs, etc.) are exempt and edit in place.
# Reads tool-call JSON from stdin, exits 2 (with stderr message) to block, exits 0 to allow.
set -uo pipefail

input="$(cat)"

file_path="$(printf '%s' "$input" | python3 -c '
import json, sys
try:
    data = json.load(sys.stdin)
except Exception:
    sys.exit(0)
ti = data.get("tool_input", data)
print(ti.get("file_path", "") or "")
' 2>/dev/null || true)"

if [[ -z "$file_path" ]]; then
  exit 0
fi

# Already isolated inside a worktree → allow.
case "$file_path" in
  */.claude/worktrees/*) exit 0 ;;
esac

# Only enforce for source/test CODE files. Everything else (config, docs, hooks) edits in place.
case "$file_path" in
  */src/*.cs|*/src/*.csproj|*/tests/*.cs|*/tests/*.csproj|*.sln) ;;
  *) exit 0 ;;
esac

{
  echo "[worktree-isolation-guard] BLOCKED: code edits must happen inside a git worktree."
  echo "  File: $file_path"
  echo "  Rule: 모든 코드 수정은 .claude/worktrees/<name>/ 안에서 시작합니다 (see .claude/CLAUDE.md → 작업 워크플로)."
  echo "  Fix: call EnterWorktree (name: feat-<topic>) first, then re-apply this edit inside the worktree."
} >&2
exit 2
