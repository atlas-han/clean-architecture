---
description: Run an autonomous Plan → Generate → Evaluate loop for CleanArchitecture. Repeats Plan/Generate when Evaluate fails (up to 3 cycles); merges on green.
argument-hint: <goal description>
---

You are starting a **harness loop** — an autonomous 3-phase pipeline (Plan → Generate → Evaluate) that iterates until the goal is verified or the iteration budget is exhausted. The goal:

$ARGUMENTS

This is the single orchestration entry point for multi-step code changes. **It exits at the first GREEN evaluation** — so a goal that's right on the first attempt costs exactly one Plan → Generate → Evaluate pass (the old `/work` behavior, now folded in). When Evaluate *fails*, you do **not** stop — you feed the failure back into a new Plan/Generate cycle, up to **3 iterations total**, which is what makes it suited to goals where the path to "green" is not obvious upfront (e.g. fix a flaky test until it stabilizes, refactor until all layers pass `/check-arch`, implement a feature where the right shape only emerges after seeing test failures).

Cost scales with iterations, not with invocation: simple goals finish cheap in one pass; only genuinely hard ones spend the full budget. For Claude's *own* autonomous orchestration of a routine change (not user-typed), use the `work-orchestrator` agent directly — `/harness` stays user-typed only (never auto-invoke).

Follow `.claude/skills/harness-loop/SKILL.md`. Summary of the cycle:

## Phase 1 — Plan (in-place, no edits)

1. Restate the goal in your own words.
2. Map to layers (Domain / Application / Infrastructure / Api / Tests).
3. Decompose into the smallest independent units. Mark parallel vs sequential. Note the agent best suited to each unit (`cqrs-feature-scaffolder`, `clean-arch-guardian`, `dotnet-test-runner`, `dotnet-code-reviewer`, `general-purpose`).
4. State explicit **success criteria** — what `dotnet test` output, what `clean-arch-guardian` verdict, what behavioral check makes Evaluate pass. Weak criteria ("make it work") will derail the loop; be concrete. If the goal does not make the criteria unambiguous, **call `AskUserQuestion`** to pin them down before Phase 2 — do not start Generate against guessed criteria.
5. On iteration ≥ 2, the Plan must reference the previous Evaluate's failure summary and explain *what changes* this iteration vs. the last.

If the goal is a one-line tweak, recommend a direct edit instead and stop.

## Phase 2 — Generate (inside worktree)

1. On iteration 1: call `EnterWorktree` with name `harness-<topic>`.
2. On iterations 2 and 3: stay in the same worktree (do **not** create a new one — re-use the branch).
3. Execute the plan:
   - Single independent unit → solo (you do it with Edit/Write/Bash).
   - Two or more independent units → `TeamCreate` (name `team-harness-<topic>`) and spawn one `Agent` per unit in a single message. Track via `TaskCreate` / `TaskUpdate owner=<name>` / `addBlockedBy`. Never let two agents edit the same file at once.
4. Stage but **do not commit yet** — commit happens after Evaluate passes.

## Phase 3 — Evaluate (inside worktree)

Run in this order:

```bash
dotnet build --nologo
dotnet test --nologo --verbosity minimal
```

Then layered checks (only if the corresponding layer was touched):

- Domain or any `.csproj` modified → delegate to `@clean-arch-guardian`.
- Any code change → delegate to `@dotnet-code-reviewer` for diff review (includes a **SOLID** pass; a clear SRP/OCP/LSP/ISP/intra-layer-DIP violation is a `high` finding → `REQUEST_CHANGES`, gating the merge).

**Verdict computation:**

| build | tests | arch verdict | review verdict | outcome |
|-------|-------|--------------|----------------|---------|
| ok | ok | PASS (or skipped) | APPROVE / COMMENT | **GREEN** → commit, linear-merge (see below), done |
| ok | ok | PASS | REQUEST_CHANGES | **YELLOW** → loop back to Plan with review findings |
| ok | fail | — | — | **RED** → loop back to Plan with failing test names + assertions |
| fail | — | — | — | **RED** → loop back to Plan with compile errors |
| ok | ok | FAIL | — | **RED** → loop back to Plan with arch violations |

On RED/YELLOW, capture the failure summary (one bullet per concrete failure, with file:line where possible) — this becomes the input to the next Plan phase.

**Linear-merge sequence (only on GREEN):** the repo invariant is a straight-line `git log` — no merge commits. After committing, run:

```bash
# inside the worktree
git rebase main                       # linearize on top of current main
ExitWorktree(action: "keep")          # back to main checkout, worktree preserved on disk
git merge --ff-only <branch>          # MUST be ff-only — --no-ff is forbidden
git worktree remove .claude/worktrees/harness-<topic>
git branch -d <branch>
```

If `git rebase` reports conflicts, `git rebase --abort`, keep the worktree, and report — never push through conflicts blindly. If `--ff-only` is refused (main moved during rebase), re-rebase and try again; never fall back to a merge-commit-producing strategy.

## Iteration budget

- **Max 3 iterations.** If iteration 3 ends RED/YELLOW, stop and report failure (see below).
- Between iterations, **never** discard the worktree. The next Plan/Generate amends or extends what's there.
- If two consecutive iterations produce the same failure signature, stop early — looping is not making progress. Report as failure.

## Failure report

If the budget runs out or progress stalls:

```
status: FAILED (harness loop)
iterations_used: <n>
worktree: .claude/worktrees/harness-<topic>
branch: <branch-name>
last_evaluation:
  build: <ok | failed>
  tests: <n passed / m failed>
  arch: <pass | fail | skipped>
  review: <approve | comment | request_changes | skipped>
blockers:
  - <one line each, concrete failure>
suggested_next_step: <what you'd try next>
```

Leave worktree + branch intact for the user to inspect.

## Constraints

- `<LangVersion>9.0</LangVersion>` is fixed — no C# 10+ syntax.
- Domain layer guard (`.claude/hooks/domain-layer-guard.sh`) is a signal, not an obstacle. If it blocks, the plan is wrong — re-plan.
- `git push`, `git reset --hard`, `git branch -D`, `git worktree remove --force` are denied by harness policy. Use safe variants.
- Never use `--no-verify` / `--no-edit` / `--amend` to paper over a failed hook. Failed hook = create a *new* commit after fixing the underlying issue.
- Each iteration's user-facing output: **≤ 5 lines** (plan delta, what changed in Generate, Evaluate verdict). Detail belongs in tasks + commit messages.

## Final output (on success)

Four sections only:

1. **Iterations** — how many cycles ran and what changed between them.
2. **What shipped** — the final diff summary.
3. **Verification** — build / test / arch / review verdicts.
4. **Next steps** — anything the user should know (follow-up tickets, deferred concerns).
