#!/usr/bin/env bash
# PreToolUse guard: block edits that would let the Application layer depend on Infrastructure / Api,
# or pull in a concrete EF Core provider (the "EF Core 구현체" the architecture forbids).
# Reads tool-call JSON from stdin, exits 2 (with stderr message) to block, exits 0 to allow.
set -uo pipefail

input="$(cat)"

# Parse the tool-call JSON with python3 (robust across Edit / Write / MultiEdit shapes and escaped strings).
parsed="$(printf '%s' "$input" | python3 -c '
import json, sys
try:
    data = json.load(sys.stdin)
except Exception:
    sys.exit(0)

tool_input = data.get("tool_input", data)
file_path = tool_input.get("file_path", "") or ""
chunks = []
if "new_string" in tool_input:
    chunks.append(tool_input["new_string"] or "")
if "content" in tool_input:
    chunks.append(tool_input["content"] or "")
for edit in tool_input.get("edits", []) or []:
    if isinstance(edit, dict) and "new_string" in edit:
        chunks.append(edit["new_string"] or "")

print(file_path)
print("---__BODY__---")
print("\n".join(chunks))
' 2>/dev/null || true)"

file_path="$(printf '%s' "$parsed" | sed -n '1p')"
body="$(printf '%s' "$parsed" | awk '/^---__BODY__---$/{found=1; next} found')"

if [[ -z "$file_path" ]]; then
  exit 0
fi

# Only inspect the Application layer
case "$file_path" in
  */CleanArchitecture.Application/*) ;;
  *) exit 0 ;;
esac

# .csproj edits: block ProjectReference to Infrastructure or Api.
# (PackageReference is allowed here — Application legitimately uses MediatR / FluentValidation /
#  AutoMapper / the EF Core abstraction package.)
if [[ "$file_path" == *.csproj ]]; then
  if printf '%s' "$body" | grep -E -q '<ProjectReference[^>]*CleanArchitecture\.(Infrastructure|Api)'; then
    {
      echo "[application-layer-guard] BLOCKED: Application.csproj must not ProjectReference Infrastructure or Api."
      echo "  File: $file_path"
      echo "  Rule: Application 은 Domain 만 참조 (see .claude/CLAUDE.md → 계층 의존 규칙)."
      echo "  Depend on an interface in Application and let Infrastructure implement it instead."
    } >&2
    exit 2
  fi
  exit 0
fi

# .cs edits: scan body for banned imports + concrete EF Core provider markers.
# NOTE: `using Microsoft.EntityFrameworkCore` itself is ALLOWED — Application uses DbSet /
# async LINQ (ToListAsync) / IApplicationDbContext. Only the concrete provider wiring is banned.
banned_patterns=(
  'using[[:space:]]+CleanArchitecture\.Infrastructure'
  'using[[:space:]]+CleanArchitecture\.Api'
  '\.UseInMemoryDatabase\b'
  '\.UseSqlServer\b'
  '\.UseSqlite\b'
  '\.UseNpgsql\b'
  '\.UseMySql\b'
)

hit=""
for pat in "${banned_patterns[@]}"; do
  if printf '%s' "$body" | grep -E -q "$pat"; then
    hit="${hit:+$hit, }${pat}"
  fi
done

if [[ -n "$hit" ]]; then
  {
    echo "[application-layer-guard] BLOCKED: Application must not depend on Infrastructure / Api or a concrete EF Core provider."
    echo "  File: $file_path"
    echo "  Forbidden token(s) detected: $hit"
    echo "  Rule: see .claude/CLAUDE.md → 계층 의존 규칙 (Application 절대 금지: Infrastructure, Api, EF Core 구현체)."
    echo "  Keep EF Core provider wiring (UseInMemoryDatabase 등) in Infrastructure's DependencyInjection."
  } >&2
  exit 2
fi

exit 0
