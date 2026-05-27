---
name: add-cqrs-feature
description: Use when the user asks to add a new Command or Query to the Application layer (e.g. "add CancelOrder command", "add a query that returns top-selling products", "create an UpdateStock command"). Walks through the full vertical slice — record, handler, validator, tests, controller endpoint — under the project's exact conventions, inside a worktree.
---

# Add a CQRS feature slice

This is the standard procedure for adding a vertical CQRS slice to CleanArchitecture. The slice is **one folder, four files plus tests**, and must build + test green before merging.

## 1. Clarify inputs

Before any worktree work, pin down:

- **Feature / aggregate** (e.g. `Product`, `Order`) — must already be a Domain entity, *or* this slice creates one (add the entity first in Domain).
- **Action** (`Create`, `Update`, `Delete`, `GetById`, `GetAll`, custom `Cancel`, `AdjustStock`, …).
- **Kind**: `command` (state-changing, validator required) or `query` (read).
- **Request shape**: positional record fields with types.
- **Response shape**: `Guid` (Create), `Unit` (Update/Delete), `ProductDto` (GetById), `IReadOnlyList<ProductDto>` (GetAll), etc.

If any of those are missing from the user's request, ask *one* focused question. Don't invent fields.

## 2. Enter worktree

```
EnterWorktree(name: "feat-<action>-<feature>")
```

All edits from this point are inside `.claude/worktrees/feat-<action>-<feature>/`. Verify cwd before editing.

## 3. (If needed) Add or extend the Domain entity first

If the action needs a new entity method or invariant, edit the entity *first* in `src/CleanArchitecture.Domain/Entities/<Feature>.cs`:

- Private setters; mutate through a method named for the action.
- Validate invariants → throw `DomainException` on violation.
- Add a Domain unit test in `tests/CleanArchitecture.Domain.UnitTests/Entities/<Feature>Tests.cs`.

The Domain hook will block any external `using` here.

## 4. Application slice — file layout

```
src/CleanArchitecture.Application/<Feature>s/Commands/<Action><Feature>/
  ├── <Action><Feature>Command.cs
  ├── <Action><Feature>CommandHandler.cs
  └── <Action><Feature>CommandValidator.cs   # Commands only
```

or for queries:

```
src/CleanArchitecture.Application/<Feature>s/Queries/<Action><Feature>/
  ├── <Action><Feature>Query.cs
  └── <Action><Feature>QueryHandler.cs
```

DTOs (if new) → `Application/<Feature>s/Queries/Dtos/<X>Dto.cs` with `record` + `init` properties. Map via `AutoMapper` `MappingProfile.cs`.

## 5. Templates

Mirror the exact patterns in `CreateProductCommand*` / `GetProductByIdQuery*`:

- Records are **positional** (`public record FooCommand(string Bar, int Baz) : IRequest<Guid>;`).
- Handler injects `IApplicationDbContext` (and `IMapper` for queries that project).
- Use `await _context.Products.FindAsync(id, cancellationToken)` then throw `NotFoundException(nameof(Product), id)` if null.
- Mutate via domain methods, then `await _context.SaveChangesAsync(cancellationToken)`.
- Validator: `RuleFor(x => x.Field).NotEmpty().MaximumLength(N)` etc. — user-facing messages.

C# 9 only: no `required`, no file-scoped namespaces, no `"""raw strings"""`.

## 6. Tests (mandatory, parallel-eligible)

Once the handler exists, validator and handler-test are independent. If you have a team, do them in parallel; otherwise sequentially.

**Handler test** — `tests/Application.UnitTests/<Feature>s/Commands/<Action><Feature>/<Action><Feature>CommandHandlerTests.cs`:
- Use `TestDbContextFactory.Create()` — never `Infrastructure.ApplicationDbContext`.
- One happy-path `[Fact]` minimum; add `[Fact]` per branching path (not-found, conflict, etc.).

**Validator test** (Commands only) — `<Action><Feature>CommandValidatorTests.cs`:
- One `[Theory]` per rule, asserting failures and the exact error key.

## 7. Wire up the controller

Add the endpoint to `src/CleanArchitecture.Api/Controllers/<Feature>sController.cs`:

```csharp
[HttpPost]
public async Task<ActionResult<Guid>> Create([FromBody] CreateProductCommand command)
{
    var id = await Mediator.Send(command);
    return CreatedAtAction(nameof(GetById), new { id }, id);
}
```

Match HTTP verb + return code to convention (POST→201, PUT→204, DELETE→204, GET→200/404).

## 8. Verify

```bash
dotnet build --nologo
dotnet test --nologo --verbosity minimal
```

All 28 existing tests + your new ones must pass. The PostToolUse hook already formatted `.cs` files; the PreToolUse hook already blocked any forbidden Domain imports.

## 9. Optional architecture check

If you added new csprojs or touched Domain dependencies, delegate to `@clean-arch-guardian` before merging.

## 10. Commit + merge

```bash
git add -A
git commit -m "feat(<feature>): <action> via CQRS slice"
```

Then `ExitWorktree(action: "merge")` (or `git switch main && git merge --no-ff <branch> && git worktree remove ...`). On test failure, **do not** force a merge — keep the worktree and report.

## Parallel team option

If the user is adding **multiple slices at once** (e.g. "add Create, Update, Delete for Order"), call `TeamCreate` and spawn one `cqrs-feature-scaffolder` per slice. Independent slices have no shared files (each in its own folder), so they parallelize cleanly. Controller edits *do* conflict — serialize the controller insertions.
