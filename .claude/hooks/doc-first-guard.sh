#!/usr/bin/env bash
# PreToolUse guard: documentation-first — require a PRD/ADR before substantive code edits.
#
# Composes with worktree-isolation-guard:
#   - That guard forces all src/test CODE edits into a worktree (main checkout is blocked).
#   - This guard then requires a docs/adr/** or docs/prd/** change to exist in that worktree
#     (committed since main OR uncommitted) BEFORE any src/test code edit is allowed.
# Net effect: 실질적 작업(worktree 안 코드) = PRD/ADR 먼저. (see .claude/CLAUDE.md → 문서화 우선)
#
# Exemptions (exit 0 / no block):
#   - non-code files (.claude config, docs, *.md) — only src/tests .cs/.csproj are gated
#   - edits in the main checkout (not under .claude/worktrees/) — worktree-isolation-guard governs those;
#     a trivial in-place fix the user explicitly allowed is not this guard's concern
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

# Only enforce for source/test CODE files. Everything else (config, docs, *.md) edits freely.
case "$file_path" in
  */src/*.cs|*/src/*.csproj|*/tests/*.cs|*/tests/*.csproj) ;;
  *) exit 0 ;;
esac

# Only enforce inside a worktree. In the main checkout, worktree-isolation-guard already blocks
# code edits; user-approved trivial in-place fixes are out of scope for documentation-first.
case "$file_path" in
  */.claude/worktrees/*) ;;
  *) exit 0 ;;
esac

# Derive the worktree root from the path string (NOT from `git -C $(dirname)`): a brand-new
# slice file lives in a dir that doesn't exist yet at edit time, which would make git fail and
# the guard wrongly allow. The case match above guarantees `/.claude/worktrees/` is in the path.
prefix="${file_path%%/.claude/worktrees/*}"   # everything before /.claude/worktrees/
rest="${file_path#*/.claude/worktrees/}"      # <name>/<...>
wtname="${rest%%/*}"                          # <name>
root="$prefix/.claude/worktrees/$wtname"

if [[ ! -d "$root" ]] || ! git -C "$root" rev-parse --is-inside-work-tree >/dev/null 2>&1; then
  exit 0   # not a git worktree we can reason about → don't block
fi

# A PRD/ADR "exists for this work" if docs/adr/** or docs/prd/** is:
#   (a) changed in the working tree (staged / unstaged / untracked), or
#   (b) committed on this branch since main.
uncommitted="$(git -C "$root" status --porcelain -- docs/adr docs/prd 2>/dev/null || true)"
committed="$(git -C "$root" diff --name-only main...HEAD -- docs/adr docs/prd 2>/dev/null || true)"

if [[ -n "$uncommitted$committed" ]]; then
  exit 0
fi

{
  echo "[doc-first-guard] BLOCKED: 코드 편집 전에 PRD 또는 ADR을 먼저 작성하세요."
  echo "  File: $file_path"
  echo "  Rule: 시스템 개선/신규 기능 등 실질적 작업은 docs/prd/ 또는 docs/adr/ 문서로 시작합니다 (see .claude/CLAUDE.md → 문서화 우선)."
  echo "  Fix:"
  echo "    - 신규 기능(무엇/누구를 위해) → PRD:  /doc prd <제목>   (템플릿: .claude/templates/prd-template.md)"
  echo "    - 기술/구조 결정(어떻게/왜)   → ADR:  /doc adr <제목>   (템플릿: .claude/templates/adr-template.md)"
  echo "    PRD vs ADR 판단: .claude/skills/document-first/SKILL.md"
  echo "  문서를 이 worktree 안 docs/{prd,adr}/ 에 추가하면 차단이 풀립니다."
} >&2
exit 2
