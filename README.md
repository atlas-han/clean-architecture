# CleanArchitecture

ASP.NET Core 9 (.NET 9) 기반 Clean Architecture 샘플 API. 언어는 C# 9 (`<LangVersion>9.0</LangVersion>`) 로 고정해 record / init / top-level statements 데모에 집중합니다.

## 계층 구조

```
┌─────────────────────────────────────────────────────┐
│  Api (Presentation)                                 │
│  - Controllers, Filters, DI Composition Root        │
└──────────────┬──────────────────────────────────────┘
               │ depends on
               ▼
┌─────────────────────────────────────────────────────┐
│  Infrastructure                                     │  ──► Application
│  - EF Core DbContext, External Services             │      (구현체 제공)
└─────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────┐
│  Application                                        │
│  - CQRS (MediatR), Validators, DTO, Interfaces      │
└──────────────┬──────────────────────────────────────┘
               │ depends on
               ▼
┌─────────────────────────────────────────────────────┐
│  Domain                                             │
│  - Entities, Domain Exceptions                      │
│  - 외부 라이브러리 의존성 0                          │
└─────────────────────────────────────────────────────┘
```

의존성 흐름은 항상 안쪽(Domain) 방향. 바깥 계층의 구현체는 안쪽 계층의 **인터페이스**(`IApplicationDbContext`, `IDateTime`)를 통해 주입됩니다 (Dependency Inversion).

## C# 9 데모 포인트

| 기능 | 위치 |
|------|------|
| `record` (positional) | `CreateProductCommand`, `UpdateProductCommand`, `GetProductByIdQuery` 등 |
| `record` + `init` properties | `ProductDto` |
| Top-level statements | `Api/Program.cs` (Startup 클래스 없음 — 최소 호스팅) |
| Target-typed `new()` | DI 등록 등 |
| Pattern matching (`is null`) | Command/Query 핸들러 |

## 사용 라이브러리

- **MediatR 12** — CQRS 파이프라인 (`IRequest`, `IRequestHandler`, `IPipelineBehavior`)
- **FluentValidation 11** — Command 검증 (파이프라인 Behavior 로 자동 실행)
- **AutoMapper 13** — `Product` → `ProductDto` `ProjectTo` 매핑
- **EF Core 9 (InMemory)** — 영속성. 실제 DB 전환은 `Infrastructure/DependencyInjection.cs` 의 `UseInMemoryDatabase(...)` 한 줄만 교체
- **Swashbuckle 7** — OpenAPI / Swagger UI

## 빌드 & 실행

```bash
cd CleanArchitecture
dotnet build
dotnet run --project src/CleanArchitecture.Api
```

기본 포트 (`launchSettings.json`):
- HTTPS: https://localhost:5001
- HTTP:  http://localhost:5000
- Swagger UI: 루트(`/`) — `app.UseSwaggerUI(c => c.RoutePrefix = string.Empty)`

## API 엔드포인트

| Method | Path | 응답 |
|--------|------|------|
| GET    | `/api/products?page=1&pageSize=20` | 200, `ProductDto[]` |
| GET    | `/api/products/{id}` | 200 / 404 |
| POST   | `/api/products` | 201, 생성된 `Guid` |
| PUT    | `/api/products/{id}` | 204 / 400 / 404 |
| DELETE | `/api/products/{id}` | 204 / 404 |

## 요청 예시

```bash
# 생성
curl -X POST http://localhost:5000/api/products \
  -H "Content-Type: application/json" \
  -d '{"name":"Keyboard","description":"Mechanical","price":129000,"stock":50}'

# 조회
curl http://localhost:5000/api/products
```

## 예외 처리 흐름

`Api/Filters/ApiExceptionFilter.cs` 한 곳에서 매핑:

| 예외 | 응답 |
|------|------|
| `Application.Common.Exceptions.ValidationException` (FluentValidation 실패) | 400 + `ValidationProblemDetails` |
| `Application.Common.Exceptions.NotFoundException` | 404 + `ProblemDetails` |
| `Domain.Exceptions.DomainException` | 400 + `ProblemDetails` (도메인 불변식 위반) |
| 그 외 | 500 + `ProblemDetails` |

## 요청 파이프라인

```
HTTP request
  → [ApiController] 모델 바인딩
    → ProductsController.Method
      → IMediator.Send(command)
        → ValidationBehavior<TRequest,TResponse>   ◄ FluentValidation 실행
          → CreateProductCommandHandler            ◄ 실제 비즈니스 로직
            → IApplicationDbContext.SaveChangesAsync
              → ApplicationDbContext.ApplyAuditFields  ◄ CreatedAt/UpdatedAt 자동
```

## 테스트

xUnit 만 사용 (외부 어설션·모킹 라이브러리 없음). 3개 테스트 프로젝트:

| 프로젝트 | 대상 | 격리 방식 |
|----------|------|-----------|
| `Domain.UnitTests` | `Product` 엔티티 불변식 / 동작 | 외부 의존성 0 |
| `Application.UnitTests` | Command/Query 핸들러, FluentValidation 검증기 | `TestDoubles/TestDbContext` (`IApplicationDbContext` 구현) + EF Core InMemory. 테스트마다 새 GUID DB 이름으로 격리 |
| `Api.IntegrationTests` | HTTP 파이프라인 전체 (필터·검증·매핑·EF Core) | `WebApplicationFactory<Program>` — `public partial class Program {}` 가 진입점 노출 |

```bash
dotnet test                                           # 전체 (28 tests)
dotnet test tests/CleanArchitecture.Domain.UnitTests          # Domain만
dotnet test tests/CleanArchitecture.Application.UnitTests     # Application만
dotnet test tests/CleanArchitecture.Api.IntegrationTests      # API 통합만
```

`Application.UnitTests` 가 Infrastructure 가 아닌 자체 `TestDbContext` 를 두는 이유: `IApplicationDbContext` 추상화가 진짜 Application 의 경계임을 테스트로도 강제하기 위함. Infrastructure 변경이 Application 테스트를 깨뜨리지 않습니다.

## 확장 포인트

- **실제 DB**: `Infrastructure/DependencyInjection.cs` 의 `UseInMemoryDatabase("...")` → `UseSqlServer(...)` / `UseNpgsql(...)`. Migrations 는 Infrastructure 프로젝트에 추가.
- **인증/인가**: `Api/Program.cs` 에 `AddAuthentication`/`UseAuthentication`/`UseAuthorization` 추가. 핸들러가 사용자 정보를 필요로 하면 `Application` 에 `ICurrentUser` 같은 인터페이스 정의 → `Api` 또는 `Infrastructure` 가 구현.
- **Domain Events**: `BaseEntity` 에 도메인 이벤트 컬렉션 추가 → `DbContext.SaveChangesAsync` 에서 MediatR `Publish` 로 디스패치.

## 디렉터리 트리

```
clean-architecture/
├── clean-architecture.sln
├── README.md
└── src/
    ├── CleanArchitecture.Domain/
    │   ├── Common/BaseEntity.cs
    │   ├── Entities/Product.cs
    │   └── Exceptions/DomainException.cs
    │
    ├── CleanArchitecture.Application/
    │   ├── DependencyInjection.cs
    │   ├── Common/
    │   │   ├── Behaviors/ValidationBehavior.cs
    │   │   ├── Exceptions/{NotFoundException, ValidationException}.cs
    │   │   ├── Interfaces/{IApplicationDbContext, IDateTime}.cs
    │   │   └── Mappings/MappingProfile.cs
    │   └── Products/
    │       ├── Commands/
    │       │   ├── CreateProduct/{Command, Handler, Validator}.cs
    │       │   ├── UpdateProduct/{Command, Handler, Validator}.cs
    │       │   └── DeleteProduct/{Command, Handler}.cs
    │       └── Queries/
    │           ├── Dtos/ProductDto.cs
    │           ├── GetProductById/{Query, Handler}.cs
    │           └── GetProducts/{Query, Handler}.cs
    │
    ├── CleanArchitecture.Infrastructure/
    │   ├── DependencyInjection.cs
    │   ├── Persistence/
    │   │   ├── ApplicationDbContext.cs
    │   │   └── Configurations/ProductConfiguration.cs
    │   └── Services/DateTimeService.cs
    │
    └── CleanArchitecture.Api/
        ├── Program.cs          ◄ 최소 호스팅, 끝에 `public partial class Program {}`
        ├── appsettings*.json
        ├── Properties/launchSettings.json
        ├── Controllers/{ApiControllerBase, ProductsController}.cs
        └── Filters/ApiExceptionFilter.cs

tests/
├── CleanArchitecture.Domain.UnitTests/
│   └── Entities/ProductTests.cs
├── CleanArchitecture.Application.UnitTests/
│   ├── TestDoubles/{TestDbContext, TestDbContextFactory}.cs
│   └── Products/
│       ├── Commands/CreateProduct/{HandlerTests, ValidatorTests}.cs
│       ├── Commands/UpdateProduct/HandlerTests.cs
│       └── Queries/GetProductByIdQueryHandlerTests.cs
└── CleanArchitecture.Api.IntegrationTests/
    └── ProductsControllerTests.cs
```
