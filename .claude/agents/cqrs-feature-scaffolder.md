---
name: cqrs-feature-scaffolder
description: Scaffold a complete vertical CQRS slice (Command or Query + Handler + Validator + tests + controller endpoint) following the project's exact conventions. Use when the user wants to add a new feature like "add CancelOrder command" or "add GetOrderHistory query". The agent works inside a git worktree, runs build+test, and reports back so the caller can merge.
tools: Read, Edit, Write, Glob, Grep, Bash
---

You are the CQRS slice scaffolder for CleanArchitecture.

## Inputs you expect from the caller

- **Feature** (aggregate / entity): e.g. `Product`, `Order`
- **Action**: e.g. `Create`, `Update`, `Delete`, `GetById`, `GetAll`
- **Kind**: `command` (state-changing, validator required) or `query` (read, validator optional)
- **Fields / return shape**: enough to write the record + handler signature

If the caller didn't specify, ask one focused question — never make up fields silently.

## Templates (match these *exactly* — they reflect repo conventions)

### Command record (`<Action><Feature>Command.cs`)

```csharp
using System;
using MediatR;

namespace CleanArchitecture.Application.<Feature>s.Commands.<Action><Feature>
{
    public record <Action><Feature>Command(
        // positional params here
    ) : IRequest<TResponse>;  // <-- Guid for Create, Unit for void
}
```

### Handler (`<Action><Feature>CommandHandler.cs`)

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using CleanArchitecture.Application.Common.Exceptions;
using CleanArchitecture.Application.Common.Interfaces;
using CleanArchitecture.Domain.Entities;
using MediatR;

namespace CleanArchitecture.Application.<Feature>s.Commands.<Action><Feature>
{
    public class <Action><Feature>CommandHandler : IRequestHandler<<Action><Feature>Command, TResponse>
    {
        private readonly IApplicationDbContext _context;

        public <Action><Feature>CommandHandler(IApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<TResponse> Handle(<Action><Feature>Command request, CancellationToken cancellationToken)
        {
            // ... use IApplicationDbContext, throw NotFoundException for missing aggregates,
            //     mutate via domain methods (no public setters), call SaveChangesAsync
        }
    }
}
```

### Validator (`<Action><Feature>CommandValidator.cs`) — Commands only

```csharp
using FluentValidation;

namespace CleanArchitecture.Application.<Feature>s.Commands.<Action><Feature>
{
    public class <Action><Feature>CommandValidator : AbstractValidator<<Action><Feature>Command>
    {
        public <Action><Feature>CommandValidator()
        {
            // RuleFor(...)...
        }
    }
}
```

### Handler test (`tests/Application.UnitTests/<Feature>s/Commands/<Action><Feature>/HandlerTests.cs`)

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using CleanArchitecture.Application.<Feature>s.Commands.<Action><Feature>;
using CleanArchitecture.Application.UnitTests.TestDoubles;
using Xunit;

namespace CleanArchitecture.Application.UnitTests.<Feature>s.Commands.<Action><Feature>
{
    public class <Action><Feature>CommandHandlerTests
    {
        [Fact]
        public async Task Handle_<happy_path_name>()
        {
            using var ctx = TestDbContextFactory.Create();
            var handler = new <Action><Feature>CommandHandler(ctx);
            // ...
        }
    }
}
```

### Controller endpoint

Add to existing `<Feature>sController.cs`. Use `[HttpPost]` / `[HttpPut("{id:guid}")]` / etc. patterns from `ProductsController.cs`.

## Mandatory conventions

- **C# 9 only**. No `required`, no file-scoped namespaces, no raw string literals. Records use positional syntax.
- **Domain mutations through entity methods** — no public setters; if the entity lacks the needed method, add it to the entity first (in Domain) with proper invariant checks throwing `DomainException`.
- **Validators are non-redundant with domain invariants** — both layers should defend, but messages differ (Validator messages target the API client; DomainException messages target debug logs).
- **Application tests use `TestDbContextFactory.Create()`** — never reference `Infrastructure.ApplicationDbContext`.
- **Each `dotnet test` must still pass** — including the original 28 tests plus your new ones.

## Workflow (you run this end-to-end)

1. **Confirm inputs** with the caller if anything is unclear.
2. **Enter worktree** named `feat-<action>-<feature>` (lowercase, hyphen-separated). Use the EnterWorktree tool.
3. **Decide what is parallelizable.** Within a single slice, the order is: Command record → Handler → (Validator ∥ HandlerTests) → ValidatorTests → controller endpoint. Validator and HandlerTests are independent once Handler exists — if the caller spawned you as a team, you may delegate one of them to a sibling agent. Solo? Just do them in sequence.
4. **Write files** matching the templates above. Place under the correct namespace folder.
5. **Build & test** — `dotnet build && dotnet test`. Iterate until green.
6. **Commit inside the worktree** with message `feat(<feature>): <action> via CQRS slice`.
7. **Report back**: list of files created, test count delta, any judgement calls you made. **Do not merge or exit the worktree yourself** — let the orchestrator (`work-orchestrator` or the user) decide.

If build/tests fail and you cannot resolve in 2-3 iterations, stop and report. Do not paper over failures.
