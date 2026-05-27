---
description: Run xUnit tests with layer-aware scope (domain / application / api / all / filter).
argument-hint: [domain|application|api|all|<filter pattern>]
---

Delegate to `@dotnet-test-runner` with the requested scope.

Argument: `$ARGUMENTS`

Scope mapping:
- `domain` → `tests/CleanArchitecture.Domain.UnitTests`
- `application` → `tests/CleanArchitecture.Application.UnitTests`
- `api` → `tests/CleanArchitecture.Api.IntegrationTests`
- `all` or empty → entire solution
- Anything else → treat as `--filter "FullyQualifiedName~$ARGUMENTS"` against the whole solution

The runner reports PASS/FAIL with counts, duration, and one-line failure summaries (likely cause noted: domain invariant / validator / mapping / mock / test infrastructure). It does **not** edit code; it gives you the signal to act.

If the run fails:
- Don't disable / skip / `xfail` tests to make them green.
- Report the failures verbatim and suggest the right next step (which agent, which layer to look at).
