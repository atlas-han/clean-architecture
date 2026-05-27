---
name: dotnet-debug
description: Use when build or tests fail with a confusing error in this .NET 9 / C# 9 / MediatR / EF Core InMemory / xUnit stack. Has recipes for the failure modes most likely on this codebase.
---

# .NET debugging recipes for CleanArchitecture

## 1. "Feature is not available in C# 9.0" build error

You used C#10+ syntax. The project is locked to C# 9 by design.

| Forbidden | C# 9 replacement |
|-----------|------------------|
| `namespace X.Y;` (file-scoped) | `namespace X.Y { ... }` |
| `public required string Name` | `public string Name { get; init; } = string.Empty;` + validate in ctor |
| `"""raw"""` | `"escaped\nstrings"` or `@"verbatim"` |
| `record struct Foo(int X)` | `class Foo` with `init` props, or positional `record Foo(int X)` |
| `with` on non-record | refactor to `record` |

## 2. MediatR handler not found at runtime

```
System.InvalidOperationException: No handler registered for request type ...Command.
```

- Handler class must be `public` and **not** generic at the class level.
- Handler must implement `IRequestHandler<TRequest, TResponse>` (or `IRequestHandler<TRequest>` for `Unit`).
- DI scan happens in `Application/DependencyInjection.cs` via `AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<...>())`. If the handler lives in a *new* assembly, register it there too.
- Validator class is registered via `services.AddValidatorsFromAssemblyContaining<...>()` in the same DI file.

## 3. ValidationBehavior didn't run

Symptoms: handler executed with invalid data; no `ValidationException` thrown.

- Validator class must be `public`, named `<RequestName>Validator`, and inherit `AbstractValidator<TRequest>`.
- It must be in the same assembly that `AddValidatorsFromAssemblyContaining<>` scans.
- The pipeline behavior is registered in `Application/DependencyInjection.cs` — confirm `services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));` is present.

## 4. EF Core InMemory test isolation bleed

Symptoms: tests pass individually but fail when run together, often "entity already tracked" or finding pre-seeded data.

`TestDbContextFactory.Create()` generates a fresh GUID DB name per call. If your test reuses a context across operations, dispose and recreate per logical "request" the handler would see in prod. Don't share a `TestDbContext` across multiple `Handle()` calls expecting separate units of work.

## 5. Integration test cannot resolve Program

```
WebApplicationFactory<Program> ... Program is inaccessible due to its protection level
```

The line `public partial class Program {}` at the bottom of `src/CleanArchitecture.Api/Program.cs` must exist. If a refactor deleted it, restore it.

## 6. AutoMapper "Unmapped members" exception

Add the property to `MappingProfile.cs`:

```csharp
CreateMap<Product, ProductDto>()
    .ForMember(d => d.SomeNewField, opt => opt.MapFrom(s => s.SomeNewField));
```

Or, if it's a real omission, add the matching property on `ProductDto`.

## 7. `ApiExceptionFilter` returned 500 for a known exception

The filter only maps `ValidationException`, `NotFoundException`, `DomainException`. If you introduced a new exception type and want it mapped, edit `src/CleanArchitecture.Api/Filters/ApiExceptionFilter.cs`. Don't wrap-throw in handlers as a workaround.

## 8. Build is green locally but `dotnet test` says "no test is available"

You added a test class but xUnit can't see it:
- Class must be `public`.
- Method must be `public Task` or `public void`, decorated with `[Fact]` or `[Theory]`.
- Tests project file must reference `Microsoft.NET.Test.Sdk`, `xunit`, `xunit.runner.visualstudio` (already present in existing tests projects — copy a working `.csproj` if you created a new test project).

## 9. PreToolUse hook blocked a Domain edit

The hook (`.claude/hooks/domain-layer-guard.sh`) refuses external imports inside `Domain/`. **The hook is right**. Move the abstraction:

- Need EF Core? Define an interface in `Application/Common/Interfaces/` and implement it in `Infrastructure/`.
- Need MediatR? Domain stays pure events (POCO); MediatR `Publish` happens in `Infrastructure/Persistence/ApplicationDbContext.SaveChangesAsync`.
- Need DateTime? Use the existing `IDateTime` abstraction.

## 10. dotnet format keeps changing my file

The PostToolUse hook runs `dotnet format whitespace`. It only touches whitespace, not style. If output bothers you, the project's `.editorconfig` (or its absence) decides the rules — adjust there, not by removing the hook.
