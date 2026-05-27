#!/usr/bin/env bash
# PreToolUse guard: block edits that would introduce forbidden dependencies into the Domain layer.
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

# Only inspect the Domain layer
case "$file_path" in
  */CleanArchitecture.Domain/*) ;;
  *) exit 0 ;;
esac

# .csproj edits: block any ProjectReference / PackageReference appearing in Domain
if [[ "$file_path" == *.csproj ]]; then
  if printf '%s' "$body" | grep -E -q '<(ProjectReference|PackageReference)\b'; then
    {
      echo "[domain-layer-guard] BLOCKED: Domain.csproj must have zero ProjectReference / PackageReference."
      echo "  File: $file_path"
      echo "  Rule: Domain layer has zero external dependencies (see .claude/CLAUDE.md)."
      echo "  If a dependency is truly needed, lift the concept to Application instead."
    } >&2
    exit 2
  fi
  exit 0
fi

# .cs edits: scan body for banned `using` namespaces
banned_patterns=(
  'using[[:space:]]+MediatR'
  'using[[:space:]]+FluentValidation'
  'using[[:space:]]+AutoMapper'
  'using[[:space:]]+Microsoft\.EntityFrameworkCore'
  'using[[:space:]]+Microsoft\.AspNetCore'
  'using[[:space:]]+Microsoft\.Extensions\.DependencyInjection'
  'using[[:space:]]+CleanArchitecture\.Application'
  'using[[:space:]]+CleanArchitecture\.Infrastructure'
  'using[[:space:]]+CleanArchitecture\.Api'
)

hit=""
for pat in "${banned_patterns[@]}"; do
  if printf '%s' "$body" | grep -E -q "$pat"; then
    hit="${hit:+$hit, }${pat}"
  fi
done

if [[ -n "$hit" ]]; then
  {
    echo "[domain-layer-guard] BLOCKED: Domain layer must not depend on outer layers or external frameworks."
    echo "  File: $file_path"
    echo "  Forbidden import(s) detected: $hit"
    echo "  Rule: see .claude/CLAUDE.md → 계층 의존 규칙."
    echo "  Move the abstraction into Application (interface) and let outer layers implement it."
  } >&2
  exit 2
fi

exit 0
