---
name: harness-loop
description: Use ONLY when the user types `/harness <goal>` (or explicitly asks for the harness loop). Executes an autonomous Plan → Generate → Evaluate cycle that exits at the first GREEN pass and iterates up to 3 times on failure, merging the worktree only when build + tests + (optional) arch + review all pass. Do NOT auto-invoke for normal feature/bugfix requests — for Claude's own autonomous orchestration of a routine change, drive the `work-orchestrator` agent directly instead.
---

# Harness Loop (Plan → Generate → Evaluate)

The harness loop is the project's autonomous self-correction cycle. It exists for goals where the *first* implementation attempt is unlikely to be correct, so the agent must observe a concrete failure (test output, arch violation, review finding) and re-plan against it.

## When to use

- User typed `/harness <goal>` directly. (Required — never auto-invoke.)
- The goal has a measurable success criterion that's easy to check but hard to satisfy on the first try (e.g. "all 28 tests green after refactoring `OrderHandler`", "DeleteProduct slice passes both validator + integration tests").
- The user explicitly asks for "iterative", "self-correcting", or "loop until green".

## When NOT to use

- One-line edits or single trivial changes → direct edit.
- Adding a CQRS slice with no special twist → `/add-cqrs`.
- Pure read-only investigation / questions → just answer.

Multi-step but linear work where you can plan once and execute is *also* served by this skill — the loop simply exits after the first GREEN pass, so there's no separate lightweight command. For Claude's own autonomous (non-user-typed) orchestration of such a change, drive the `work-orchestrator` agent directly instead of this skill.

## The cycle

Each iteration is **Plan → Generate → Evaluate**, preceded once by **Document** (문서화 우선). The cycle exits either via GREEN (merge + success) or by exhausting the iteration budget (max 3) or stalling (two identical failures in a row).

### Document (문서화 우선, iteration 1 only, before code)

Substantive work starts with a PRD or ADR — see `.claude/skills/document-first/SKILL.md`.
- New user-facing capability → **PRD** (`/doc prd <title>`); technical/structural decision → **ADR** (`/doc adr <title>`); big feature + key tech choice → both, cross-linked. Unsure → lean ADR.
- The doc must live in the **same worktree** as the code, so create it after `EnterWorktree` (the Generate worktree) — `.claude/hooks/doc-first-guard.sh` blocks `src/`·`tests/` edits until a `docs/prd/**` or `docs/adr/**` change exists in the worktree. Don't bypass it.
- Fill the real sections + add the `docs/{prd,adr}/README.md` index row. The doc's acceptance criteria become the Plan's success criteria.
- Iterations 2–3 don't re-document; if the approach shifts materially, append a note (ADR) or update the PRD.

### Plan (no edits)

Output a numbered plan with:
- Goal restatement.
- Layer map (Domain / Application / Infrastructure / Api / Tests).
- Unit decomposition with parallel/sequential markers and best-fit agent per unit.
- **Concrete success criteria** — what command output or verdict the Evaluate phase must see. Vague criteria = failed loop, so if the goal does not already make the criteria unambiguous, **call `AskUserQuestion`** to pin them down before generating any code. Never start the Generate phase against guessed success criteria.
- (Iteration ≥ 2) Reference to previous failure + explicit delta from prior plan.

### Generate (inside worktree)

- Iteration 1: `EnterWorktree(name: "harness-<topic>")`.
- Iterations 2–3: stay in the same worktree.
- Single unit → solo. Multiple independent units → `TeamCreate` + parallel `Agent` spawns (one message, multiple tool calls). Use `TaskCreate` / `TaskUpdate owner=<name>` / `addBlockedBy` to express dependencies.
- Two agents must never edit the same file in parallel — model as sequential.
- Do not commit until Evaluate is GREEN.

### Evaluate (inside worktree)

Order matters:

1. `dotnet build --nologo`
2. `dotnet test --nologo --verbosity minimal`
3. If Domain or any `.csproj` changed → delegate to `@clean-arch-guardian`.
4. Any code change → delegate to `@dotnet-code-reviewer` (this review now includes a **SOLID** pass — a clear SRP/OCP/LSP/ISP/intra-layer-DIP violation surfaces as a `high` finding → `REQUEST_CHANGES`, so SOLID gates the merge just like arch — and a **YAGNI** pass — a clear single-use abstraction / unread surface / unrequested flexibility / impossible-state defensive code is likewise `high` and gates the merge, while mandated structure like `IApplicationDbContext` and the CQRS slice contract is carved out).

Compute the verdict:

| build | tests | arch | review | verdict |
|-------|-------|------|--------|---------|
| ok | ok | PASS (or n/a) | APPROVE / COMMENT | **GREEN** |
| ok | ok | PASS | REQUEST_CHANGES | YELLOW (re-loop) |
| ok | fail | — | — | RED |
| fail | — | — | — | RED |
| ok | ok | FAIL | — | RED |

On GREEN, **before committing**: review `README.md` against the diff — update it if the change touches an API endpoint, an `appsettings.json` key, a user-facing behavior, or the directory tree; otherwise state "README 변경 불필요". Confirm the Phase-0 PRD/ADR matches the shipped shape and bump its status. Then commit with a clear message and **linear-merge** the worktree (see `worktree-workflow/SKILL.md` "Review + Merge"):

```bash
# inside the worktree, after committing:
git rebase main                       # linearize on top of current main
# then:
ExitWorktree(action: "keep")          # back to the main checkout
git merge --ff-only <branch>          # MUST be ff-only — no merge commits
git worktree remove .claude/worktrees/harness-<topic>
git branch -d <branch>
```

`--no-ff` and plain `git merge` are forbidden — they create merge commits and break `git log --graph` linearity. If `--ff-only` is refused (main moved), re-rebase and try again; never fall back to a merge commit. Done.

On RED/YELLOW: capture a **failure summary** (one concrete bullet per failure, with file:line where possible). Pass it as the seed for the next Plan iteration.

## Iteration budget and stall detection

- 3 iterations max.
- Stall detection: if iteration N's failure summary matches iteration N-1's failure signature (same test names + same assertion deltas, or same arch violations), stop — looping is making no progress.
- On budget exhaustion or stall: print the failure report (see `/harness` command doc) and leave the worktree intact.

## What this skill must NOT do

- Never bypass the Domain layer hook (`.claude/hooks/domain-layer-guard.sh`). If it blocks an edit, the plan is wrong — re-plan, don't `--no-verify`.
- Never `git push`, `git reset --hard`, `git branch -D`, `git worktree remove --force`. The harness denies these.
- Never use `--amend` to paper over a failed pre-commit hook. A failed hook means the commit didn't happen → fix and create a *new* commit.
- Never introduce C# 10+ syntax (`required`, file-scoped namespaces, raw string literals). `<LangVersion>9.0</LangVersion>` is fixed.

## Related

- `.claude/skills/worktree-workflow/SKILL.md` — the standard single-pass worktree cycle that this skill iterates.
- `.claude/agents/work-orchestrator.md` — the single-pass orchestrator engine. The harness loop is conceptually "run this N times with failure-driven re-planning"; a single GREEN pass through it is what `/work` used to be.
- `.claude/agents/clean-arch-guardian.md`, `.claude/agents/dotnet-code-reviewer.md`, `.claude/agents/dotnet-test-runner.md` — the evaluators used in Phase 3.
