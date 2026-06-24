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
| `record` + `init` properties | `ProductDto`, `OrderDto`, `OrderItemDto` |
| `record` 값 객체 (값 동등성 + 연산자 오버로드) | `Domain/ValueObjects/Money.cs` |
| Top-level statements | `Api/Program.cs` (Startup 클래스 없음 — 최소 호스팅) |
| Target-typed `new()` | DI 등록 등 |
| Pattern matching (`is null`) | Command/Query 핸들러 |

## 사용 라이브러리

- **자체 CQRS 디스패처** — `Application/Common/Messaging/` 에 `IRequest`, `IRequest<T>`, `IRequestHandler<>`, `IRequestHandler<,>`, `IPipelineBehavior<>` / `IPipelineBehavior<,>`, `ISender`, `Sender` 를 직접 정의. 외부 NuGet 의존성 없음. `Sender` 가 `IServiceProvider` 로 핸들러를 찾아 등록된 파이프라인 비헤이비어로 감싼 뒤 리플렉션으로 디스패치 (MediatR 미사용)
- **FluentValidation 11** — Command/Query 검증. `Application/Common/Behaviors/ValidationBehavior.cs` 가 **파이프라인 비헤이비어**로 핸들러 앞단에서 자동 실행 → 실패 시 `Application.Common.Exceptions.ValidationException`
- **수동 매핑 (AutoMapper 미사용)** — `Application/Common/Mappings/{ProductMappings, OrderMappings}.cs` 에 `Expression<Func<Entity, Dto>>` 를 직접 정의. EF Core 가 이 식을 그대로 SQL projection 으로 번역하므로 `ProjectTo` 와 동일한 효과를 NuGet 없이 얻습니다
- **EF Core 9** — 영속성. `ConnectionStrings:DefaultConnection` 이 있으면 **SQL Server**, 없으면 **InMemory** 폴백 (`Infrastructure/DependencyInjection.cs`). 값 객체 `Money` 는 value converter 로 `decimal` 컬럼에 매핑
- **Asp.Versioning.Mvc 8** — URI 기반 API 버저닝(`/api/v1/...`) + `X-Api-Version` 헤더, `api-supported-versions` 응답 헤더 광고
- **HealthChecks (EFCore / SqlServer / Redis)** — `/health` 의 DB·Redis 연결 프로브
- **Redis (StackExchange) 분산 캐시** — 멱등성 저장소. `ConnectionStrings:Redis` 가 있으면 Redis, 없으면 인메모리 분산 캐시 폴백
- **Swashbuckle 7** — OpenAPI 스펙 생성(`AddSwaggerGen`). 단, `Program.cs` 의 `UseSwagger` / `UseSwaggerUI` 가 **주석 처리**되어 현재 UI·JSON 엔드포인트는 노출되지 않습니다

## 빌드 & 실행

```bash
cd CleanArchitecture
dotnet build
dotnet run --project src/CleanArchitecture.Api
```

기본 포트 (`launchSettings.json`):
- HTTPS: https://localhost:5001
- HTTP:  http://localhost:5000
- Swagger/OpenAPI: 스펙은 `AddSwaggerGen` 으로 구성돼 있으나 `Program.cs` 의 `UseSwagger` / `UseSwaggerUI` 호출이 **주석 처리**되어 현재 UI·JSON 엔드포인트는 노출되지 않습니다. 활성화하려면 해당 두 줄의 주석을 해제하세요.

## API 엔드포인트

모든 리소스는 **URI 버저닝**(`/api/v{version}/...`) 아래 노출됩니다. `v1.0` 이 기본값이라 버전 세그먼트를 생략하면 v1 으로 해석되고, `X-Api-Version` 헤더로도 지정할 수 있습니다 (`Api/Controllers/ApiControllerBase.cs`). 성공 응답은 `data`(+ 페이징 시 `meta`)를, 오류 응답은 `error` 를 담은 **표준 봉투**로 내려갑니다 (아래 **표준 응답 봉투 & 에러 코드** 참조).

### Products (`/api/v1/products`)

| Method | Path | 응답 |
|--------|------|------|
| GET    | `/api/v1/products?page=1&pageSize=20` | 200, `SuccessResponse<ProductDto[]>` (+ `meta` 페이징) |
| GET    | `/api/v1/products/{id}` | 200 `SuccessResponse<ProductDto>` / 404 |
| POST   | `/api/v1/products` | 201, `SuccessResponse<ProductDto>` (+ `Location` 헤더) |
| PUT    | `/api/v1/products/{id}` | 204 / 400 / 404 / **409**(동시성 충돌) |
| DELETE | `/api/v1/products/{id}` | 204 / 404 |

### Orders (`/api/v1/orders`)

주문 애그리거트. 주문 생성 시 상품 재고를 차감하며, 상태 머신(`Pending → Confirmed | Cancelled`)을 도메인 불변식으로 강제합니다.

| Method | Path | 응답 | 멱등성 |
|--------|------|------|--------|
| GET    | `/api/v1/orders?page=1&pageSize=20` | 200, `SuccessResponse<OrderDto[]>` (+ `meta`) | — |
| GET    | `/api/v1/orders/{id}` | 200 `SuccessResponse<OrderDto>` / 404 | — |
| POST   | `/api/v1/orders` | 201, `SuccessResponse<OrderDto>` | `[Idempotent]` |
| POST   | `/api/v1/orders/place` | 201, `SuccessResponse<OrderDto>` | `[Idempotent]` |
| POST   | `/api/v1/orders/{id}/confirm` | 204 / 404 / 422 | — |
| POST   | `/api/v1/orders/{id}/cancel` | 204 / 404 / 422 | — |

- **`POST /orders` vs `POST /orders/place`** — 둘 다 주문을 만들고 재고를 차감하지만, `create` 는 단일 `SaveChanges`(암시적 트랜잭션)로, `place` 는 `ExecuteInTransactionAsync` 로 재고 차감과 주문 저장을 **명시적 트랜잭션**으로 묶는 패턴을 보여줍니다(여러 번의 쓰기 사이에 작업이 끼어도 원자성 보장). 둘 다 `[Idempotent]` 라 `Idempotency-Key` 재시도는 원본 응답을 재생합니다.
- **`confirm` / `cancel`** — 상태 전이만 수행(부수효과 없음). `confirm` 은 `Pending` 만 허용, `cancel` 은 이미 취소된 주문이면 거부 — 위반 시 도메인 불변식이 깨져 **422 `BUSINESS_RULE_VIOLATION`**. 취소는 **재고를 복구하지 않습니다**(샘플 단순화).

## 요청 예시

```bash
# 생성 → 201 SuccessResponse<ProductDto> (+ Location 헤더)
curl -X POST http://localhost:5000/api/v1/products \
  -H "Content-Type: application/json" \
  -d '{"name":"Keyboard","description":"Mechanical","price":129000,"stock":50}'

# 목록 조회 → 200 { "data": [ ... ], "meta": { page, pageSize, totalCount, totalPages } }
curl http://localhost:5000/api/v1/products
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

- **백그라운드 배치**: `Infrastructure/BackgroundServices/OutboxProducerWorker.cs` 가 매 주기 시작에서
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

### 점검 설정 (`Maintenance`)

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

## 표준 응답 봉투 & 에러 코드

모든 응답은 API 디자인 가이드 §4.3 의 봉투로 통일됩니다 (`Api/Common/ApiResult.cs` + `Api/Common/Responses/ApiResponses.cs`). `traceId` 는 W3C trace-id(§4.4)라 응답을 서버 로그와 상관시킬 수 있습니다.

**성공** — `SuccessResponse<T>` (`data`, 목록은 `meta` 페이징 동반):

```json
{
  "traceId": "4bf92f3577b34da6a3ce929d0e0e4736",
  "timestamp": "2026-06-12T09:30:00.0000000Z",
  "data": [ { "id": "…", "name": "Keyboard", "price": 129000, "stock": 50 } ],
  "meta": { "page": 1, "pageSize": 20, "totalCount": 45, "totalPages": 3 }
}
```

**오류** — `ErrorResponse` (`error.code` / `error.message`, 검증 오류는 `error.details[]` 의 필드별 메시지):

```json
{
  "traceId": "4bf92f3577b34da6a3ce929d0e0e4736",
  "timestamp": "2026-06-12T09:30:00.0000000Z",
  "error": {
    "code": "VALIDATION_ERROR",
    "message": "One or more validation errors occurred.",
    "details": [ { "field": "name", "message": "Name must not be empty." } ]
  }
}
```

`error.code` 는 §6.2 매핑(`Api/Common/ErrorCodes.cs`): `VALIDATION_ERROR`(400) · `NOT_FOUND`(404) · `CONFLICT`(409) · `BUSINESS_RULE_VIOLATION`(422) · `DEADLINE_EXCEEDED`(504) · `SERVICE_UNAVAILABLE`(503) · `INTERNAL_ERROR`(500).

## 예외 처리 흐름

`Api/Filters/ApiExceptionFilter.cs` 한 곳에서 허용된 예외군을 위 `ErrorResponse` 봉투로 매핑합니다 (핸들러/컨트롤러는 응답을 직접 만들지 않고 던지기만 합니다). 파생 예외는 타입 계층을 따라 베이스 타입 매핑을 상속합니다.

| 예외 | 상태 | `error.code` |
|------|------|--------------|
| `Application.Common.Exceptions.ValidationException` (FluentValidation 실패) | 400 | `VALIDATION_ERROR` (+ `details[]`) |
| `Application.Common.Exceptions.NotFoundException` | 404 | `NOT_FOUND` |
| `Domain.Exceptions.DomainException` (도메인 불변식 위반) | **422** | `BUSINESS_RULE_VIOLATION` |
| `DbUpdateConcurrencyException` (낙관적 동시성 충돌) | 409 | `CONFLICT` |
| `OperationCanceledException` — deadline 소진 | 504 | `DEADLINE_EXCEEDED` |
| `OperationCanceledException` — 클라이언트 disconnect | 499 | (본문 없음 — 서버 오류로 로깅하지 않음) |
| 그 외 | 500 | `INTERNAL_ERROR` (내부 상세 비노출, `traceId` 로 로그 상관) |

## 낙관적 동시성 (Optimistic Concurrency)

재고 동시 차감으로 인한 **오버셀링**을 막기 위해 `Product.Stock` 을 EF Core **동시성 토큰**으로 둡니다 (`Infrastructure/Persistence/Configurations/ProductConfiguration.cs` 의 `.IsConcurrencyToken()` — 별도 RowVersion 컬럼 없이 비즈니스 값 자체가 토큰). 두 요청이 같은 재고를 읽고 동시에 갱신하면 나중 `SaveChanges` 가 `DbUpdateConcurrencyException` 을 던지고, `ApiExceptionFilter` 가 이를 **409 `CONFLICT`** 로 매핑합니다(클라이언트는 새로 읽어 재시도). `ProductConcurrencyTests` 가 stale write 거부를 검증합니다.

## 요청 파이프라인

미들웨어 순서 (`Api/Program.cs`) — 보안 헤더가 가장 바깥이라 점검(503)·deadline(504) 단락 응답에도 헤더가 붙고, deadline 게이트는 로깅 미들웨어 *안쪽* 이라 fast-fail 도 access log 에 남습니다:

```
HTTP request
  → SecurityHeadersMiddleware        ◄ nosniff / X-Frame-Options / X-XSS-Protection / CSP (§9.1)
  → RequestLoggingMiddleware         ◄ JSON access log + 헤더/PII 마스킹 (§14)
  → MaintenanceMiddleware            ◄ 점검 중이면 503 (단, /health · /admin/maintenance* 면제)
  → DeadlinePropagationMiddleware    ◄ X-Request-Deadline 잔여 예산 < 50ms 면 504 fast-fail (§7.4)
  → [ApiController] 모델 바인딩 (+ IdempotencyFilter — [Idempotent] 액션)
    → Controller.Method
      → ISender.Send(request, DeadlineToken)      ◄ Application/Common/Messaging/Sender
        → ValidationBehavior (IPipelineBehavior)  ◄ FluentValidation → 실패 시 ValidationException
          → <Request>Handler                      ◄ 실제 비즈니스 로직 (리플렉션 디스패치)
            → IApplicationDbContext.SaveChangesAsync
              → ApplicationDbContext.ApplyAuditFields  ◄ CreatedAt/UpdatedAt 자동
```

(개발/Local 환경은 앞단에 `UseDeveloperExceptionPage`, 비-개발 환경은 `UseHsts` 가 추가됩니다. 라우팅 뒤에는 `MapControllers` 와 `MapHealthChecks("/health")`.)

## Graceful Shutdown

SIGTERM / Ctrl+C 수신 시 in-flight 요청이 끝날 때까지 호스트 종료를 지연합니다 (`Program.cs` 의 `HostOptions.ShutdownTimeout`). 기본 30초이며 `Shutdown:Timeout`(예: `"00:00:30"`)으로 조정합니다. `ApplicationStopping` 에서 "draining in-flight requests" 를, 드레인/타임아웃 후 `ApplicationStopped` 를 로깅합니다. `GracefulShutdownTests` 가 두 라이프사이클 이벤트와 기본 30초 타임아웃을 검증합니다.

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
| 있음 | `≥ 50ms` | deadline 을 `HttpContext.Items["RequestDeadline"]` 에 보관 + 잔여 예산으로 취소 토큰(2단계) 생성 후 통과 |

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
curl -i http://localhost:5000/api/v1/products \
  -H "X-Request-Deadline: 1000000000000"
```

### 수신 측 처리 (2단계)

미들웨어는 진입 fast-fail 에서 끝나지 않고, 살아있는 deadline 을 비즈니스 로직 끝단까지 흘려보냅니다 (가이드 §7.4):

1. **진입 fast-fail** — 위 표(잔여 `< 50ms` → 504).
2. **비즈니스 로직 · DB 취소** — 살아있는 deadline 으로 `RequestAborted` 에 링크된 `CancellationToken`(`CancelAfter(remaining)`)을 만들어 `HttpContext.GetRequestCancellationToken()` (컨트롤러는 `ApiControllerBase.DeadlineToken`) 으로 노출합니다. 이를 `ISender.Send` 에 넘기면 핸들러 → EF Core 쿼리/`SaveChanges` 까지 전파되어, 예산이 소진되면 실행 중인 작업이 취소됩니다. 발생한 `OperationCanceledException` 은 `ApiExceptionFilter` 가 **504 `DEADLINE_EXCEEDED`** 로 매핑합니다 — 단, 진짜 클라이언트 disconnect(=`RequestAborted` 동반 취소)는 제외합니다.
> 가이드 §7.4 의 3단계(**다운스트림 재전파**)는 이 샘플에 아웃바운드 HTTP 호출이 없어 **미구현**입니다. 선구현돼 있던 `DeadlinePropagationHandler`(`DelegatingHandler`)는 ADR-0002 Wave 2 에서 제거했고, 다운스트림 호출이 생기면 받은 **절대** `X-Request-Deadline` 를 `AddHttpClient(...).AddHttpMessageHandler<>()` 패턴(§4.4 `traceparent` 자동 주입과 동일, 서버 간 **NTP 시계 동기화** + 최소 네트워크 마진 50ms 전제)으로 다시 붙이면 됩니다.

## 멱등성 (Idempotency-Key)

네트워크 오류로 **부작용 있는 POST 가 중복 전송**되어도 한 번만 처리되도록, 클라이언트가 만든 고유 키로 중복 요청을 걸러냅니다 (API 디자인 가이드 §7.1). 같은 키의 재시도는 **원본 응답을 그대로 재생(replay)** 할 뿐 핸들러를 다시 실행하지 않습니다 (결제 시스템의 idempotency key 패턴과 동일).

- **헤더**: `Idempotency-Key: <UUID v4>` — 클라이언트가 요청 시작 시 생성하고, 재시도 시 **같은 키를 재사용**.
- **담당**: `Api/Idempotency/IdempotentAttribute.cs` (`[Idempotent]`, `IFilterFactory`) + `Api/Idempotency/IdempotencyFilter.cs` (`IAsyncResourceFilter`). 리소스 필터라서 모델 바인딩 *전* 에 요청 본문으로 fingerprint 를 만들고, 응답 직렬화 결과를 *캡처* 해 저장합니다.
- **적용 엔드포인트**: `POST /api/v1/orders`, `POST /api/v1/orders/place` (주문 생성). `[Idempotent]` 를 단 액션에서만 동작합니다.

### 동작

| 키 상태 | 필터 동작 |
|---------|-----------|
| 헤더 없음 | 그대로 통과 (이 엔드포인트에서 키는 **선택적**, dedup 없음) |
| 처음 보는 키 | 키 선점(InProgress) → 액션 실행 → **2xx 응답만** 캐시(Completed) |
| 완료된 키 (같은 본문) | 캐시된 원본 응답을 **그대로 재생** + `Idempotency-Replayed: true` 헤더 (원본과 동일한 201/200) |
| 처리 중인 키 (동시 요청) | **409 Conflict** — 같은 키가 아직 처리 중 (425 아님, 가이드 §7.1) |
| 같은 키 + 다른 본문 | **409 Conflict** — 키 재사용 오용 방지 (가이드의 422 또는 409 중 409 선택) |
| 만료(24h 경과) 후 | 기록 없음 → **새 요청으로 처리** |

- **성공만 캐시**: 4xx/5xx 응답은 저장하지 않고 선점한 키를 **해제** 하므로, 클라이언트가 본문을 고쳐 같은 키로 다시 시도할 수 있습니다.
- **잠금 해제 보장**: deadline 취소·예외·클라이언트 disconnect 등 어떤 실패에서도 `CancellationToken.None` 으로 키를 해제합니다 — 정당한 재시도가 "처리 중(409)" 으로 영구 차단되지 않도록 (가이드 §7.4 주의사항). 해제마저 실패하면 24h TTL 이 백스톱.

키 재사용/처리 중 충돌은 `ApiExceptionFilter` 와 동일한 §4.3 `ErrorResponse` 봉투로 내려갑니다:

```json
{
  "traceId": "4bf92f3577b34da6a3ce929d0e0e4736",
  "timestamp": "2026-06-12T09:30:00.1234567Z",
  "error": {
    "code": "CONFLICT",
    "message": "A request with this Idempotency-Key is already being processed."
  }
}
```

```bash
# 같은 키로 두 번 POST → 두 번째는 원본 201 을 그대로 재생 (주문은 1건만 생성)
KEY=$(uuidgen)
curl -s -X POST http://localhost:5000/api/v1/orders/place \
  -H "Content-Type: application/json" -H "Idempotency-Key: $KEY" \
  -d '{"customerName":"Alice","items":[{"productId":"<id>","quantity":3}]}'

curl -i -X POST http://localhost:5000/api/v1/orders/place \
  -H "Content-Type: application/json" -H "Idempotency-Key: $KEY" \
  -d '{"customerName":"Alice","items":[{"productId":"<id>","quantity":3}]}'
# → 201 Created + 헤더 `Idempotency-Replayed: true` (동일한 응답 본문, 재고는 한 번만 차감)
```

### 저장소 (Redis / 폴백)

키와 캐시된 응답은 `IDistributedCache` 에 **24시간 TTL** 로 저장합니다 (`Application/Common/Interfaces/IIdempotencyStore.cs` 포트 → `Infrastructure/Idempotency/DistributedCacheIdempotencyStore.cs` 구현). 다중 인스턴스에서도 dedup 이 성립하도록 분산 캐시를 사용합니다.

```jsonc
// appsettings.json — Redis 연결 문자열이 있으면 Redis, 없으면 인메모리 폴백
"ConnectionStrings": {
  "Redis": "localhost:6379"   // 설정 시 AddStackExchangeRedisCache (운영)
                              // 미설정 시 AddDistributedMemoryCache (개발/테스트 — Redis 불필요)
}
```

- `DefaultConnection` 유무로 SQL Server ↔ EF InMemory 를 가르는 기존 폴백 패턴과 동일합니다.
- 통합 테스트는 Redis 없이 인메모리 분산 캐시로 동일한 store 로직을 실행합니다.

> ⚠️ 현재 키 선점(`TryBeginAsync`)은 get-then-set 이라 미세한 경합 창이 있습니다. 운영 Redis 에서는 `SET key val NX PX`(원자적 set-if-absent)로 교체하는 것이 정석입니다(코드 주석에 후속으로 표기). 또한 replay 는 status / Content-Type / body / Location 만 재현하며, 그 밖의 응답 헤더는 보존하지 않습니다.

## Health Check (`/health`)

로드밸런서·오케스트레이터 프로브용 엔드포인트로, 점검 모드에서도 **항상 200 경로**입니다. 커스텀 JSON 으로 각 체크 결과를 내려줍니다 (`Program.cs` 의 `MapHealthChecks` ResponseWriter):

```json
{
  "status": "Healthy",
  "checks": [
    { "name": "application", "status": "Healthy", "description": "Application is running" },
    { "name": "database", "status": "Healthy", "description": null }
  ]
}
```

| 체크 | 등록 조건 | 내용 |
|------|-----------|------|
| `application` | 항상 | liveness — 프로세스 생존 (`Program.cs`) |
| `database` | 항상 | `DefaultConnection` 설정 시 `AddSqlServer`(SELECT 1), 미설정 시 `AddDbContextCheck`(InMemory) |
| `redis` | `ConnectionStrings:Redis` 설정 시에만 | Redis ping. 미설정(개발/테스트)이면 체크를 등록하지 않아 `/health` 는 Healthy 유지 |

연결 문자열이 없는 개발/테스트에서도 폴백 백엔드로 green 을 유지하도록 설계되었습니다.

## 보안 헤더 (Security Headers)

`Api/Middleware/SecurityHeadersMiddleware.cs` 가 **모든** 응답에 아래 헤더를 추가합니다 (파이프라인 최바깥이라 503/504 단락 응답에도 적용, §9.1):

| 헤더 | 값 |
|------|-----|
| `X-Content-Type-Options` | `nosniff` |
| `X-Frame-Options` | `SAMEORIGIN` |
| `X-XSS-Protection` | `1; mode=block` |
| `Content-Security-Policy` | `default-src 'self'; frame-ancestors 'self'` |

추가로 비-개발 환경에서는 `UseHsts()` 가 `Strict-Transport-Security`(max-age 365d, includeSubDomains, preload)를 붙입니다. HSTS 는 HTTPS 에서만 의미가 있어 개발/Local 에서는 제외됩니다.

## 액세스 로그 & 민감정보 마스킹

`Api/Middleware/RequestLoggingMiddleware.cs` 가 요청마다 한 줄의 **구조화 JSON 로그**(`Api/Logging/JsonConsoleFormatter.cs`, formatter name `unified_json`)를 남깁니다. 콘솔 로깅은 `Program.cs` 에서 이 포매터로 고정됩니다 (가이드 §14.3 / §14.6).

- **기록 필드**: `timestamp`, `level`, `http.request.method`, `path`, `query_string`(쿼리 없으면 생략), `http.response.status_code`, `duration`(ms), `req_body_bytes`, `res_body_bytes`, `client.address`, `host`, `trace_id`, `span_id`, `request_id`, `endpoint_handler`, 요청/응답 헤더(`req_header_*` / `res_header_*`). 예외 시 `error_type` / `exception.*` 추가. HTTP 속성은 OTel 시맨틱 컨벤션의 점 표기(`http.request.method` 등), 인프라 필드는 snake_case. `content-length`/`host` 요청 헤더와 `x-request-id`/`date`/`server`/`location` 응답 헤더는 로그에서 제외.
- **요청 ID**: `X-Request-Id`/`X-Correlation-Id` 등 인입 상관 ID 를 재사용, 없으면 UUIDv7 생성 → 응답 `X-Request-Id` 헤더로 반향.
- **헤더 마스킹** (`Api/Logging/HeaderMasker.cs`): `Authorization`, `Proxy-Authorization`, `Cookie`, `Set-Cookie`, `X-Api-Key` 값을 `***` 로 가림.
- **본문 PII 마스킹** (`Api/Logging/PiiMasker.cs`, 본문은 `Debug` 레벨에서만 로깅): `customerName`, `email`, `phone`, `password`, `ssn`, `cardNumber`, `cvv`, `dateOfBirth` 등 키의 문자열은 첫 글자만 남기고 마스킹 — 중첩 깊이 무관·대소문자 무시.

## 설정 (`appsettings.json`)

| 키 | 기본값 | 의미 |
|----|--------|------|
| `ConnectionStrings:DefaultConnection` | `""` | 있으면 SQL Server, 없으면 EF InMemory 폴백 |
| `ConnectionStrings:Redis` | (없음) | 있으면 Redis 분산 캐시, 없으면 인메모리 폴백(개발/Local). **비-개발 환경에서 미설정 시 부팅 실패**(fail-fast) |
| `Shutdown:Timeout` | `00:00:30` | graceful shutdown 드레인 한도 |
| `Maintenance:Enabled` | `false` | 시작 시 점검 모드 기본값(런타임 토글) |
| `Maintenance:RetryAfterSeconds` | `120` | 점검 503 의 `Retry-After`(초) |
| `Idempotency:KeyLifetime` | `1.00:00:00` | 멱등성 키 TTL(24h) |
| `Logging:Console:FormatterName` | `unified_json` | 구조화 JSON 콘솔 로그 |

로컬 SQL Server/Redis 자격증명은 `.env`(템플릿 `.env.example`)로 주입합니다.

## 로컬 개발 & DB 마이그레이션

기본값(연결 문자열 없음)에서는 EF InMemory + 인메모리 캐시라 **외부 의존성 없이** 곧바로 실행됩니다. 실제 SQL Server 로 전환할 때 스키마는 **Flyway**(Docker)로 관리합니다:

- `docker-compose.yml` — Flyway 러너. `host.docker.internal:1433` 의 로컬 MSSQL 에 접속하며 자격증명은 `.env` 에서 주입(`.env.example` 복사 후 수정).
- `db/` — `V1__init_schema.sql`(초기 DDL), `flyway.conf`.
- `scripts/` — `db-migrate.sh`(migrate) · `db-validate.sh`(validate) · `db-info.sh`(info).
- `postman/` — API 컬렉션 + 환경 변수(`*.postman_collection.json`, `*.postman_environment.json`).
- `docs/order-feature-spec.md` — Order 애그리거트 스펙(도메인 모델·CQRS·엔드포인트·테스트 범위).

## 테스트

xUnit 만 사용 (외부 어설션·모킹 라이브러리 없음). 3개 테스트 프로젝트:

| 프로젝트 | 대상 | 격리 방식 |
|----------|------|-----------|
| `Domain.UnitTests` | `Product` / `Order` / `OrderItem` 엔티티 불변식 + `Money` 값 객체 | 외부 의존성 0 |
| `Application.UnitTests` | Products·Orders Command/Query 핸들러, FluentValidation 검증기 | `TestDoubles/TestDbContext` (`IApplicationDbContext` 구현) + EF Core InMemory. 테스트마다 새 GUID DB 이름으로 격리 |
| `Api.IntegrationTests` | HTTP 파이프라인 전체 (필터·검증·매핑·동시성·점검·deadline·멱등성·헬스·보안헤더·access log·graceful shutdown) | `WebApplicationFactory<Program>` — `public partial class Program {}` 가 진입점 노출 |

```bash
dotnet test                                           # 전체 (219 tests: Domain 42 / Application 64 / Api 113)
dotnet test tests/CleanArchitecture.Domain.UnitTests          # Domain만
dotnet test tests/CleanArchitecture.Application.UnitTests     # Application만
dotnet test tests/CleanArchitecture.Api.IntegrationTests      # API 통합만
```

`Application.UnitTests` 가 Infrastructure 가 아닌 자체 `TestDbContext` 를 두는 이유: `IApplicationDbContext` 추상화가 진짜 Application 의 경계임을 테스트로도 강제하기 위함. Infrastructure 변경이 Application 테스트를 깨뜨리지 않습니다.

## CI / 시크릿 스캔 (GitHub Actions)

`main` push 와 모든 PR 에서 두 워크플로가 돕니다. 둘 다 실패 시 Slack 봇으로 알림을 보냅니다.

| 워크플로 | 파일 | 하는 일 | 실패 알림 |
|----------|------|---------|-----------|
| **Tests** | `.github/workflows/test.yml` | `global.json` 기준 .NET 9 SDK → `dotnet restore/build/test -c Release` (전체 솔루션) | 빌드/테스트 실패 시 Slack |
| **Gitleaks** | `.github/workflows/gitleaks.yml` | gitleaks `v8.30.1` 로 전체 git 히스토리 시크릿 스캔 (`gitleaks git . --exit-code 1`) | 시크릿 탐지 시 Slack(탐지 내역 = 룰·파일·라인·커밋·**리댁트된** match 포함) + JSON 리포트 아티팩트 업로드 |

### 필요한 GitHub Secrets

Slack 알림은 **봇 토큰** 방식(`slackapi/slack-github-action@v2`, `chat.postMessage`)입니다. 리포 Settings → Secrets and variables → Actions 에 등록:

| Secret | 값 | 비고 |
|--------|----|----|
| `SLACK_BOT_TOKEN` | `xoxb-...` | Slack 앱의 Bot User OAuth Token. `chat:write` 스코프 필요. 봇을 알림 채널에 초대(`/invite @봇`)해 둘 것 |
| `SLACK_CHANNEL_ID` | `C0XXXXXXX` | 채널 ID (채널명 아님). 채널 우클릭 → "Copy link" 끝의 `C...` 값 |

> 시크릿이 없거나 fork 에서 온 PR(=시크릿 미주입)에서는 Slack 스텝이 실패하지만, 잡은 이미 빌드/테스트(또는 gitleaks) 결과로 red/green 이 확정된 뒤이므로 게이트 판정에는 영향이 없습니다.

### gitleaks false positive

기본 룰셋으로 스캔하며, 현재 리포는 clean 입니다. 의도된 예시 값(예: `.env.example`)이 향후 오탐되면 리포 루트에 `.gitleaksignore`(탐지 핑거프린트 한 줄씩) 또는 `.gitleaks.toml`(커스텀 룰/allowlist) 을 추가하세요.

## 확장 포인트

- **실제 DB**: `Infrastructure/DependencyInjection.cs` 의 `UseInMemoryDatabase("...")` → `UseSqlServer(...)` / `UseNpgsql(...)`. Migrations 는 Infrastructure 프로젝트에 추가.
- **인증/인가**: `Api/Program.cs` 에 `AddAuthentication`/`UseAuthentication`/`UseAuthorization` 추가. 핸들러가 사용자 정보를 필요로 하면 `Application` 에 `ICurrentUser` 같은 인터페이스 정의 → `Api` 또는 `Infrastructure` 가 구현.
- **Domain Events**: `BaseEntity` 에 도메인 이벤트 컬렉션 추가 → `DbContext.SaveChangesAsync` 에서 `ISender` 와 유사한 자체 `IPublisher` 를 추가해 디스패치.

## 문서 (PRD / ADR) — 문서화 우선

시스템 개선·신규 기능은 **코드보다 문서가 먼저**입니다 (도입 결정: [`docs/adr/0001`](docs/adr/0001-adopt-documentation-first-harness.md)).

- **PRD** (`docs/prd/`) — *무엇을·누구를 위해*. 신규 기능 요구사항.
- **ADR** (`docs/adr/`) — *어떻게·왜 그 선택*. 기술/구조 결정 기록.
- 새 문서: `/doc <prd|adr> <제목>` (템플릿: `.claude/templates/`). 판단·절차는 `.claude/skills/document-first/SKILL.md`.
- `.claude/hooks/doc-first-guard.sh` 가 worktree 안 `src/`·`tests/` 코드 편집을 PRD/ADR 없이는 차단합니다.

## 디렉터리 트리

```
clean-architecture/
├── clean-architecture.sln
├── global.json                       ◄ .NET 9 SDK 핀
├── README.md  ·  CLAUDE.md
├── docker-compose.yml  ·  .env.example   ◄ Flyway 러너 + 로컬 DB 자격증명 템플릿
├── db/                               ◄ V1__init_schema.sql, flyway.conf
├── scripts/                          ◄ db-migrate.sh / db-validate.sh / db-info.sh
├── postman/                          ◄ collection + environment json
├── docs/
│   ├── adr/                          ◄ Architecture Decision Records (NNNN-*.md + README 인덱스)
│   ├── prd/                          ◄ Product Requirements Documents (NNNN-*.md + README 인덱스)
│   └── order-feature-spec.md  ·  kafka-event-publishing.md
│
├── src/
│   ├── CleanArchitecture.Domain/
│   │   ├── Common/BaseEntity.cs
│   │   ├── Entities/{Product, Order, OrderItem}.cs
│   │   ├── Enums/OrderStatus.cs
│   │   ├── ValueObjects/Money.cs
│   │   └── Exceptions/DomainException.cs
│   │
│   ├── CleanArchitecture.Application/
│   │   ├── DependencyInjection.cs
│   │   ├── Common/
│   │   │   ├── Messaging/{IRequest, IRequestHandler, IPipelineBehavior, ISender, Sender}.cs  ◄ 자체 CQRS 디스패처
│   │   │   ├── Behaviors/ValidationBehavior.cs
│   │   │   ├── Exceptions/{NotFoundException, ValidationException}.cs
│   │   │   ├── Interfaces/{IApplicationDbContext, IDateTime, IIdempotencyStore, IMaintenanceState}.cs
│   │   │   ├── Mappings/{ProductMappings, OrderMappings}.cs       ◄ Expression 수동 매핑
│   │   │   └── Models/{PagedResult, IdempotencyRecord}.cs
│   │   ├── Products/Commands/{CreateProduct, UpdateProduct, DeleteProduct}/{Command, Handler, Validator}.cs
│   │   ├── Products/Queries/{Dtos/ProductDto, GetProductById, GetProducts}/…
│   │   ├── Orders/Commands/{CreateOrder, PlaceOrder, ConfirmOrder, CancelOrder}/{Command, Handler, Validator}.cs
│   │   └── Orders/Queries/{Dtos/{OrderDto, OrderItemDto}, GetOrderById, GetOrders}/…
│   │
│   ├── CleanArchitecture.Infrastructure/
│   │   ├── DependencyInjection.cs                 ◄ SQL/InMemory · Redis/메모리 폴백 · 헬스체크
│   │   ├── BackgroundServices/OutboxProducerWorker.cs
│   │   ├── Idempotency/DistributedCacheIdempotencyStore.cs
│   │   ├── Persistence/ApplicationDbContext.cs
│   │   ├── Persistence/Configurations/{Product, Order, OrderItem}Configuration.cs
│   │   └── Services/{DateTimeService, MaintenanceState}.cs
│   │
│   └── CleanArchitecture.Api/
│       ├── Program.cs          ◄ 최소 호스팅, 끝에 `public partial class Program {}`
│       ├── appsettings*.json  ·  Properties/launchSettings.json
│       ├── Common/{ApiResult, ErrorCodes, HttpContextExtensions}.cs  ·  Common/Responses/ApiResponses.cs
│       ├── Controllers/{ApiControllerBase, ProductsController, OrdersController, MaintenanceController}.cs
│       ├── Filters/ApiExceptionFilter.cs
│       ├── Idempotency/{IdempotentAttribute, IdempotencyFilter}.cs
│       ├── Logging/{JsonConsoleFormatter, HeaderMasker, PiiMasker}.cs
│       └── Middleware/{SecurityHeaders, RequestLogging, Maintenance, DeadlinePropagation}Middleware.cs
│
└── tests/
    ├── CleanArchitecture.Domain.UnitTests/
    │   ├── Entities/{ProductTests, OrderTests}.cs
    │   └── ValueObjects/MoneyTests.cs
    ├── CleanArchitecture.Application.UnitTests/
    │   ├── TestDoubles/{TestDbContext, TestDbContextFactory}.cs
    │   ├── Products/{Commands, Queries}/…          ◄ 핸들러 + 검증기 테스트
    │   └── Orders/{Commands, Queries}/…
    └── CleanArchitecture.Api.IntegrationTests/
        ├── {Products, Orders}ControllerTests.cs
        ├── {Idempotency, Maintenance, HealthCheck, SecurityHeaders, GracefulShutdown, ProductConcurrency}Tests.cs
        ├── {DeadlinePropagationMiddleware, RequestLoggingMiddleware}Tests.cs
        ├── {JsonConsoleFormatter, PiiMasker, HeaderMasker}Tests.cs
        ├── {ApiErrorResponse, DistributedCacheIdempotencyStore, InfrastructureDependencyInjection}Tests.cs
        └── Infrastructure/{CapturingLoggerProvider, ErrorResponseTestFactory, LoggingTestFactory, ThrowingTestController}.cs
```
