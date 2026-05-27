---
description: Review the current diff against project conventions (Clean Architecture / CQRS / C# 9 / test discipline).
argument-hint: [diff scope: main..HEAD (default) | staged | working-tree]
---

Delegate to `@dotnet-code-reviewer`.

Scope: `$ARGUMENTS` — default is `git diff main...HEAD`. Other accepted values:
- `staged` → `git diff --cached`
- `working-tree` → `git diff`
- A commit range like `abc123..def456` → use directly

The reviewer checks:
1. Layer boundaries (Domain isolation, Application→outer-layer leakage, Infrastructure→Api leakage).
2. CQRS slice consistency (Handler + Validator + tests present, folder layout).
3. C# 9 discipline (no `required`, no file-scoped namespaces, no raw strings, positional records).
4. Domain rules (private setters, mutation via methods, `DomainException` for invariants).
5. Validator ↔ DomainException duality (both layers defend, distinct messages).
6. Exception → HTTP mapping (handlers throw the right exception type; `ApiExceptionFilter` covers new types).
7. Tests (`TestDbContextFactory.Create()`, no mocking libs, integration via `WebApplicationFactory<Program>`).
8. Composition root (DI registrations in the right `DependencyInjection.cs` file; `public partial class Program {}` intact).

Output verdict: `APPROVE` / `COMMENT` / `REQUEST_CHANGES` with prioritized findings (critical→low), each with file:line and a concrete suggestion.
