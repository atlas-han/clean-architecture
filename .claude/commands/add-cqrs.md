---
description: Add a new CQRS slice (Command or Query + Handler + Validator + tests + controller endpoint) following project conventions, inside a worktree.
argument-hint: <Feature> <Action> [command|query]
---

Goal: add a new vertical CQRS slice to CleanArchitecture.

Arguments: `$ARGUMENTS` (expected format: `<Feature> <Action> [command|query]`, e.g. `Order Cancel command`, `Product GetByName query`).

If `$ARGUMENTS` is missing required pieces, ask **one** focused clarifying question covering only the unknowns (Feature? Action? command vs query? field shape? response type?). Do **not** start the worktree until inputs are pinned down.

Then run the standard worktree cycle:

1. **Plan** — list the files you'll create/edit (Command record, Handler, Validator, HandlerTests, ValidatorTests, controller endpoint, optionally Domain entity changes + Domain test). Note which steps are sequential (Domain → Command → Handler) and which are parallelizable (Validator ∥ HandlerTests after Handler exists).

2. **EnterWorktree** with name `feat-<action>-<feature>` (lowercase, hyphen-separated).

2a. **Document first (문서화 우선)** — a slice is a new user-facing capability → write a **PRD** (`/doc prd <action> <feature>`); if it also settles a notable technical/structural choice, add an **ADR** too. Create it **inside this worktree** and fill the real sections — `.claude/hooks/doc-first-guard.sh` blocks the slice's `src/`·`tests/` edits until a `docs/prd/**` or `docs/adr/**` change exists here. The PRD's acceptance criteria are your test targets. See `.claude/skills/document-first/SKILL.md`.

3. **Implement** by delegating to `@cqrs-feature-scaffolder`. The scaffolder follows the templates in `.claude/skills/add-cqrs-feature/SKILL.md`. If the user asked for multiple slices in one call (e.g. "Create and Update for Order"), create a team and spawn one scaffolder per slice — but serialize any shared-file edits (the controller).

4. **Verify** — `dotnet build && dotnet test`. If Domain changed, also `@clean-arch-guardian`.

5. **Review** via `@dotnet-code-reviewer`. Address findings (max 2 cycles).

6. **README 검수 + Commit + linear merge** on green — the repo invariant is a straight-line `git log` (no merge commits). 커밋 직전 `README.md` 검수: 새 엔드포인트(보통 POST→201 등)·설정·동작이 추가됐으면 README의 API 엔드포인트/디렉터리 트리 섹션 갱신, 아니면 "README 변경 불필요" 명시. PRD/ADR 상태도 갱신.
   ```bash
   git commit -m "feat(<feature>): <action> via CQRS slice"
   git rebase main                       # linearize; abort on conflict and report
   ExitWorktree(action: "keep")          # back to the main checkout
   git merge --ff-only <branch>          # MUST be ff-only — --no-ff is forbidden
   git worktree remove .claude/worktrees/<name>
   git branch -d <branch>
   ```
   On failure, keep the worktree and report. Never use `--no-ff` or plain `git merge`.

Reminders:
- C# 9 only.
- Application tests use `TestDbContextFactory.Create()` — never `Infrastructure.ApplicationDbContext`.
- New exception types? Update `ApiExceptionFilter` in the same slice.
- Endpoint conventions: POST→201 + `CreatedAtAction`, PUT→204, DELETE→204, GET→200/404.
