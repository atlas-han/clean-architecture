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
│  - CQRS (in-project ISender), Validators, DTO, IF   │
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

- **자체 CQRS 디스패처** — `Application/Common/Messaging/` 에 `IRequest`, `IRequest<T>`, `IRequestHandler<>`, `IRequestHandler<,>`, `ISender`, `Sender` 를 직접 정의. 외부 NuGet 의존성 없음. `Sender` 가 `IServiceProvider` 로 핸들러를 찾고, FluentValidation 을 인라인으로 실행한 뒤 디스패치
- **FluentValidation 11** — Command 검증 (`Sender` 안에서 자동 실행)
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

## 점검 모드 (Stop / Resume)

정기점검 시 **재배포 없이** API 와 백그라운드 배치를 동시에 멈췄다가 재개하는 런타임 스위치입니다.
프로세스 내부의 공유 싱글톤(`IMaintenanceState`) 하나를 API 미들웨어·관리용 엔드포인트·배치 워커가 함께 바라봅니다.

### 동작

- **API**: 점검 중에는 `Api/Middleware/MaintenanceMiddleware.cs` 가 요청을 즉시 가로채
  **`503 Service Unavailable`** + `Retry-After` 헤더 + 표준 `ErrorResponse` 봉투(`SERVICE_UNAVAILABLE`)로 응답합니다.
  컨트롤러/핸들러까지 도달하지 않습니다.

  ```http
  HTTP/1.1 503 Service Unavailable
  Retry-After: 120
  Content-Type: application/json

  {
    "traceId": "0af7651916cd43dd8448eb211c80319c",
    "timestamp": "2026-06-12T05:00:00.000Z",
    "error": {
      "code": "SERVICE_UNAVAILABLE",
      "message": "Service is under maintenance. Please retry later."
    }
  }
  ```

- **항상 열려 있는 경로(점검 중에도 200)**:
  - `GET /health` — 로드밸런서/오케스트레이터의 liveness·readiness 프로브가 죽지 않도록 **언제나** 정상 동작.
  - `/admin/maintenance*` — 점검을 해제(resume)할 제어 경로가 막히면 안 되므로 면제.

- **백그라운드 배치**: `Infrastructure/BackgroundServices/DemoBatchWorker.cs` 가 매 주기 시작에서
  `IMaintenanceState.IsStopped` 를 확인하고, 점검 중이면 해당 틱 작업을 건너뜁니다(워커 자체는 살아 있음).
  실제 배치를 추가할 때도 동일하게 작업 단위 앞에서 gate 를 확인하면 됩니다.

### 사용 방법

```bash
# 점검 시작 — 이후 모든 /api 요청이 503
curl -X POST http://localhost:5000/admin/maintenance/stop

# 현재 상태 조회 -> {"stopped": true}
curl http://localhost:5000/admin/maintenance

# 점검 중에도 헬스체크는 정상
curl http://localhost:5000/health        # 200

# 점검 종료 — 정상 운영 복귀
curl -X POST http://localhost:5000/admin/maintenance/resume
```

| Method | Path | 설명 |
|--------|------|------|
| POST | `/admin/maintenance/stop` | 점검 모드 진입 (API 503, 배치 일시정지) |
| POST | `/admin/maintenance/resume` | 점검 모드 해제 (정상 운영) |
| GET | `/admin/maintenance` | 현재 상태 조회 (`{"stopped": bool}`) |

### 설정 (`appsettings.json`)

```jsonc
"Maintenance": {
  "Enabled": false,        // 시작 시 기본 상태. true 면 부팅 직후부터 점검 모드
  "RetryAfterSeconds": 120 // 503 응답의 Retry-After 헤더 값(초)
}
```

- `Enabled` 는 **시작 시점의 기본값**일 뿐이며, 런타임 상태의 최종 권한은 위 엔드포인트에 있습니다.
  재시작하면 다시 이 기본값으로 돌아갑니다(상태는 의도적으로 영속화하지 않음).
- 상태는 **프로세스 메모리**에 보관됩니다. 다중 인스턴스로 배포한 경우 인스턴스마다 stop/resume 을 호출하세요.

> ⚠️ 이 샘플에는 인증이 없습니다. 실제 배포에서는 `/admin/maintenance*` 를 **인증·네트워크 정책으로 보호**해
> 운영자만 스위치를 조작할 수 있도록 해야 합니다.

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
      → ISender.Send(command)                      ◄ Application/Common/Messaging/Sender
        → FluentValidation 인라인 실행             ◄ Sender.ValidateAsync (전 ValidationBehavior)
          → CreateProductCommandHandler            ◄ 실제 비즈니스 로직 (리플렉션 디스패치)
            → IApplicationDbContext.SaveChangesAsync
              → ApplicationDbContext.ApplyAuditFields  ◄ CreatedAt/UpdatedAt 자동
```

## 요청 Deadline 전파 (`X-Request-Deadline`)

서비스 간 호출에서 상위 서비스가 이미 타임아웃된 뒤에도 하위 서비스가 계속 일하는 **"좀비 요청"** 을 막기 위해, 절대 시각 기반 deadline 을 헤더로 전파합니다 (API 디자인 가이드 §7.4).

- **헤더**: `X-Request-Deadline: <Unix epoch milliseconds>` — 요청 처리가 끝나야 하는 절대 시각.
- **담당**: `Api/Middleware/DeadlinePropagationMiddleware.cs` (요청 파이프라인의 `RequestLoggingMiddleware` *안쪽* 에 등록 → fast-fail 한 504 도 access log 에 남음).

동작:

| 헤더 상태 | 남은 예산 | 미들웨어 동작 |
|-----------|-----------|---------------|
| 없음 | — | 그대로 통과 (기능은 **opt-in**) |
| 파싱 불가 (예: 비-숫자) | — | 무시하고 통과 (상위 hop 의 힌트일 뿐 클라이언트 입력이 아니므로 400 으로 거부하지 않음) |
| 있음 | `< 50ms` | **즉시 `504 Gateway Timeout`** — `DEADLINE_EXCEEDED`. 하위 호출 자체를 시작하지 않음 |
| 있음 | `≥ 50ms` | `HttpContext.Items["RequestDeadline"]` 에 deadline 보관 후 통과 |

50ms 는 네트워크·핸들러 레이턴시조차 버티기 어려운 최소 마진입니다 (가이드의 10~50ms 범위 중 미들웨어 기준값). 만료(또는 임박) 응답은 `ApiExceptionFilter` 와 동일한 §4.3 `ErrorResponse` 봉투(`ApiResult.Error` 로 생성 — `traceId` / `timestamp` / `error.code` / `error.message`)로 내려갑니다:

```json
{
  "traceId": "4bf92f3577b34da6a3ce929d0e0e4736",
  "timestamp": "2026-05-28T09:30:00.1234567Z",
  "error": {
    "code": "DEADLINE_EXCEEDED",
    "message": "The request deadline (X-Request-Deadline) was exceeded before processing could start. Retry with a fresh deadline."
  }
}
```

```bash
# 이미 지난 deadline → 504 DEADLINE_EXCEEDED (fast-fail)
curl -i http://localhost:5000/api/products \
  -H "X-Request-Deadline: 1000000000000"
```

> **스코프**: 이 샘플은 가이드 §7.4 의 **진입점 fast-fail (1단계)** 까지 구현합니다. 살아있는 deadline 을 `CancellationTokenSource(remaining)` 로 만들어 DB·다운스트림 호출에 전파하는 2·3단계는, 보관된 `HttpContext.Items["RequestDeadline"]` 를 핸들러가 읽어 쓰는 확장점으로 열어 두었습니다 (모든 컨트롤러를 건드리는 변경이라 샘플에는 미반영).

## 테스트

xUnit 만 사용 (외부 어설션·모킹 라이브러리 없음). 3개 테스트 프로젝트:

| 프로젝트 | 대상 | 격리 방식 |
|----------|------|-----------|
| `Domain.UnitTests` | `Product` 엔티티 불변식 / 동작 | 외부 의존성 0 |
| `Application.UnitTests` | Command/Query 핸들러, FluentValidation 검증기 | `TestDoubles/TestDbContext` (`IApplicationDbContext` 구현) + EF Core InMemory. 테스트마다 새 GUID DB 이름으로 격리 |
| `Api.IntegrationTests` | HTTP 파이프라인 전체 (필터·검증·매핑·EF Core) | `WebApplicationFactory<Program>` — `public partial class Program {}` 가 진입점 노출 |

```bash
dotnet test                                           # 전체 (91 tests)
dotnet test tests/CleanArchitecture.Domain.UnitTests          # Domain만
dotnet test tests/CleanArchitecture.Application.UnitTests     # Application만
dotnet test tests/CleanArchitecture.Api.IntegrationTests      # API 통합만
```

`Application.UnitTests` 가 Infrastructure 가 아닌 자체 `TestDbContext` 를 두는 이유: `IApplicationDbContext` 추상화가 진짜 Application 의 경계임을 테스트로도 강제하기 위함. Infrastructure 변경이 Application 테스트를 깨뜨리지 않습니다.

## 확장 포인트

- **실제 DB**: `Infrastructure/DependencyInjection.cs` 의 `UseInMemoryDatabase("...")` → `UseSqlServer(...)` / `UseNpgsql(...)`. Migrations 는 Infrastructure 프로젝트에 추가.
- **인증/인가**: `Api/Program.cs` 에 `AddAuthentication`/`UseAuthentication`/`UseAuthorization` 추가. 핸들러가 사용자 정보를 필요로 하면 `Application` 에 `ICurrentUser` 같은 인터페이스 정의 → `Api` 또는 `Infrastructure` 가 구현.
- **Domain Events**: `BaseEntity` 에 도메인 이벤트 컬렉션 추가 → `DbContext.SaveChangesAsync` 에서 `ISender` 와 유사한 자체 `IPublisher` 를 추가해 디스패치.

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
    │   │   ├── Messaging/{IRequest, IRequestHandler, ISender, Sender}.cs  ◄ 자체 CQRS 디스패처
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
