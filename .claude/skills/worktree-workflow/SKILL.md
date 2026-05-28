---
name: worktree-workflow
description: Use when the user asks how the worktree + parallel-team workflow operates, or when you (or another agent) need a refresher on the rules for isolated work + auto-merge in this repo. Also: invoke at the start of any code-modifying task to remind yourself of the lifecycle.
---

# Worktree + parallel team workflow

The project's standing rule: **any code modification is isolated in a git worktree, verified, then auto-merged on green — always as a linear (fast-forward) merge so `git log --graph` stays a straight line, no merge commits.** This skill documents the standard lifecycle so every agent follows the same shape.

## When the rule applies

| Action | Worktree required? |
|--------|--------------------|
| Reading / searching / explaining code | No |
| Answering questions | No |
| Editing `.cs`, `.csproj`, `appsettings.json`, etc. | **Yes** |
| Editing `.claude/**` (the harness itself) | Optional — usually no (low risk + small) |
| One-line typo fix the user explicitly said "just do it in place" | No |

## The 6 phases

1. **Plan** (in current cwd, no edits)
   - Decompose the goal into the smallest independent units.
   - For each unit: what file(s), which layer, parallel vs sequential, which agent best handles it.
   - Output a numbered plan with parallelism noted explicitly.

2. **Isolate**
   - `EnterWorktree(name: "<verb>-<topic>")` — name is lowercase, hyphen-separated, descriptive (e.g. `feat-cancel-order`, `fix-validator-overflow`, `refactor-mapping-profile`).
   - After the call your cwd is `.claude/worktrees/<name>/`. Confirm via `pwd` or by checking a known file path.

3. **Execute**
   - **Solo**: one unit → do it yourself with Edit/Write/Bash.
   - **Team**: ≥ 2 independent units → `TeamCreate(team_name: "team-<topic>")`, spawn one `Agent` per unit with a self-contained prompt. Coordinate via `TaskCreate` + `TaskUpdate owner=<name>`. Sequence dependencies with `addBlockedBy`.
   - **Same-file constraint**: never let two agents edit the same file concurrently — make that a dependency edge.

4. **Verify** (always inside the worktree)
   ```bash
   dotnet build --nologo
   dotnet test --nologo --verbosity minimal
   ```
   - On Domain/csproj changes, also delegate to `@clean-arch-guardian`.

5. **Review + Merge (linear / fast-forward only)** (only on green)
   - Delegate to `@dotnet-code-reviewer` (`git diff main...HEAD` is the default scope).
   - Address `REQUEST_CHANGES` findings in-place; re-verify; re-review. Max 2 cycles.
   - Commit (`git add -A && git commit -m "..."`).
   - **Linearize on top of base** (still inside the worktree):
     ```bash
     git rebase main          # or whichever branch you forked from
     ```
     If rebase reports conflicts, run `git rebase --abort`, leave the worktree, and report — never resolve blindly.
   - **Fast-forward merge from main** (back in the original checkout):
     ```bash
     ExitWorktree(action: "keep")        # returns cwd to the main checkout, worktree stays on disk
     git merge --ff-only <branch>        # MUST be ff-only; --no-ff is forbidden in this repo
     git worktree remove .claude/worktrees/<name>
     git branch -d <branch>
     ```
   - The result is a straight-line history — the worktree's commits sit directly on top of `main` with no merge commit.
   - **Forbidden**: `git merge --no-ff`, `git merge` without `--ff-only`, or anything that creates a merge commit. If ff-only refuses, that means main moved during the rebase window — re-rebase, don't fall back to a merge commit.

6. **Failure handling**
   - Leave the worktree intact. Do **not** force-merge or force-remove.
   - Report: status, worktree path, branch name, blockers, suggested next step.
   - The user can `cd .claude/worktrees/<name>` to investigate themselves.

## Parallel decomposition examples

- **"Add Create and Cancel commands for Order"** → 2 slices, independent file sets → 2 parallel `cqrs-feature-scaffolder` agents.
- **"Add Order entity + first Create command"** → Domain entity (must come first) → blocks → Application slice → blocks → controller endpoint. Sequential, but tests for the entity can run in parallel with writing the Command.
- **"Refactor MappingProfile to split per-feature"** → mostly single file. Solo.
- **"Add OrderConfiguration to Infrastructure + add tests"** → 2 units (Infrastructure config write, test write) → parallel.

## Anti-patterns

- ❌ Editing on `main` without a worktree because "it's just one line" — if it's truly trivial, do it in place after asking; otherwise worktree.
- ❌ Spawning a team and assigning conflicting files (= silent merge conflict inside the worktree).
- ❌ Force-removing a failed worktree to "start fresh" — you lose diagnostic state and may discard real work.
- ❌ Merging before build + test pass.
- ❌ Using `git merge --no-ff` or plain `git merge` (without `--ff-only`) — both can create merge commits and break the linear-history invariant.
- ❌ `git push` from inside the worktree — harness policy denies it; pushes are the user's call.

## Mental model

A worktree is a checkpoint you can throw away cheaply. Use that property: try the bold refactor inside a worktree; if tests don't come back green, you've lost nothing.
