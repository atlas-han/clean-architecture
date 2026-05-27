---
description: Verify Clean Architecture layer-dependency invariants across the repo and report violations.
argument-hint: [optional: file glob or "diff" to scope]
---

Run the architecture audit per `.claude/skills/verify-architecture/SKILL.md`.

Scope:
- No arguments → full repo audit.
- `diff` → only inspect files changed in `git diff main...HEAD` (or `git diff` if on main).
- A glob like `src/CleanArchitecture.Domain/**` → restrict to those paths.

Argument provided: `$ARGUMENTS`

Use the 7 checks in the skill (Domain csproj deps, Domain `.cs` imports, Application→outer-layer imports, Application EF Core impl calls, Infrastructure→Api imports, `Program.cs` partial declaration, CQRS slice consistency).

For anything beyond a quick spot-check, delegate to `@clean-arch-guardian` for the canonical structured verdict format. Inline grep is fine when the user just wants a one-shot reassurance.

Output:
- Pass: `architecture: PASS (all 7 invariants)` + one-line summary of what was checked.
- Fail: list each violation with `file:line`, which invariant, and a concrete fix suggestion (push abstraction inward, push implementation outward).
