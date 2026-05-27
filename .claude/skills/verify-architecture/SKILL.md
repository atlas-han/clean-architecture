---
name: verify-architecture
description: Use when the user asks to "check the architecture", "verify layers", "make sure Domain is clean", or before merging changes that touched csproj files / `using` directives / DI wiring. Returns a structured pass/fail with the specific violations.
---

# Verify Clean Architecture invariants

## What gets checked

| # | Invariant | How |
|---|-----------|-----|
| 1 | Domain has zero `<ProjectReference>` / `<PackageReference>` | grep `src/CleanArchitecture.Domain/*.csproj` |
| 2 | Domain `.cs` files import nothing outside `System.*` / its own namespace | grep `^using ` under `Domain/` |
| 3 | Application doesn't import `CleanArchitecture.Infrastructure` or `.Api` | grep under `Application/` |
| 4 | Application doesn't call EF Core *implementation* APIs (`UseInMemoryDatabase`, `UseSqlServer`) | grep under `Application/` |
| 5 | Infrastructure doesn't import `CleanArchitecture.Api` | grep under `Infrastructure/` |
| 6 | `Program.cs` ends with `public partial class Program {}` (required for integration tests) | grep `Api/Program.cs` |
| 7 | CQRS slice consistency: each Command has Handler + Validator + tests | glob the `Commands/` tree, diff with tests tree |

## Quick command set

```bash
# 1. Domain csproj
grep -E '<(ProjectReference|PackageReference)' src/CleanArchitecture.Domain/*.csproj && echo "VIOLATION" || echo "ok: Domain zero deps"

# 2. Domain .cs imports (whitelist System.* only)
grep -rhE '^using ' src/CleanArchitecture.Domain --include='*.cs' \
  | grep -vE '^using (System|CleanArchitecture\.Domain)' \
  | sort -u

# 3. Application boundary
grep -rE '^using CleanArchitecture\.(Infrastructure|Api)' src/CleanArchitecture.Application && echo "VIOLATION" || echo "ok"

# 4. Application EF Core impl
grep -rE 'UseInMemoryDatabase|UseSqlServer|UseNpgsql|UseSqlite' src/CleanArchitecture.Application && echo "VIOLATION" || echo "ok"

# 5. Infrastructure → Api
grep -rE '^using CleanArchitecture\.Api' src/CleanArchitecture.Infrastructure && echo "VIOLATION" || echo "ok"

# 6. Program.cs partial
grep -n 'public partial class Program' src/CleanArchitecture.Api/Program.cs || echo "VIOLATION: missing"

# 7. Slice consistency (compare folder lists)
diff <(find src/CleanArchitecture.Application -type d -path '*Commands*' -not -name 'Commands' | sed 's|.*Commands/||' | sort) \
     <(find tests/CleanArchitecture.Application.UnitTests -type d -path '*Commands*' -not -name 'Commands' | sed 's|.*Commands/||' | sort)
```

## When to delegate vs do inline

- **Quick check after small edit** → run the commands above inline.
- **Full audit (after large refactor, before release, after merge)** → delegate to `@clean-arch-guardian` which reports in the canonical structured format.

## Output

If everything passes, one-line: `architecture: PASS (all 7 invariants)`.

If something fails, list each violation with file + offending line + which invariant. Suggest the layered fix (push abstraction inward, push implementation outward) — never suggest just deleting the `using`.
