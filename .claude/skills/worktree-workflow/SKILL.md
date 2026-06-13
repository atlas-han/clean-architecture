---
name: worktree-workflow
description: Use when the user asks how the worktree + parallel-team workflow operates, or when you (or another agent) need a refresher on the rules for isolated work + auto-merge in this repo. Also: invoke at the start of any code-modifying task to remind yourself of the lifecycle.
---

# Worktree + parallel team workflow

The project's standing rule: **any code modification is isolated in a git worktree, verified, then auto-merged on green — always as a linear (fast-forward) merge so `git log --graph` stays a straight line, no merge commits.** This skill documents the standard lifecycle so every agent follows the same shape.

## When the rule applies

| Action | Worktree required? | 문서(PRD/ADR) 먼저? |
|--------|--------------------|---------------------|
| Reading / searching / explaining code | No | No |
| Answering questions | No | No |
| Editing `.cs`, `.csproj`, `appsettings.json`, etc. (실질적 기능/개선) | **Yes** | **Yes** — doc-first-guard가 강제 |
| Editing `.claude/**` (the harness itself) | Optional — usually no (low risk + small) | No |
| One-line typo fix the user explicitly said "just do it in place" | No | No |

> 문서화 우선: worktree 안 `src/`·`tests/` 코드 편집은 `docs/prd/**` 또는 `docs/adr/**` 변경이 있어야 풀린다 (`.claude/hooks/doc-first-guard.sh`). 판단·절차는 `.claude/skills/document-first/SKILL.md`.

## The phases (0 Document → 6 Failure)

0. **Document (문서화 우선, before code)**
   - 신규 기능(무엇/누구) → PRD, 기술/구조 결정(어떻게/왜) → ADR, 큰 기능은 둘 다. 애매하면 ADR. `/doc <prd|adr> <제목>` 으로 다음 번호 스캐폴딩.
   - 문서는 **코드와 같은 worktree 안**에 둔다 → Isolate(2) 직후 생성. doc-first-guard가 문서 없으면 코드 편집을 막는다.
   - 실제 섹션을 채우고 `docs/{prd,adr}/README.md` 인덱스에 행 추가. PRD/ADR의 인수기준이 곧 성공 기준.

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
   - **README 검수 (커밋 직전)**: diff가 API 엔드포인트·`appsettings.json` 키·사용자가 보는 동작·디렉터리 트리를 건드리면 `README.md` 갱신. 아니면 "README 변경 불필요" 명시. Phase 0 PRD/ADR이 최종 형태와 맞는지 확인하고 상태 갱신.
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
