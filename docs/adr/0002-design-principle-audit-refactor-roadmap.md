# ADR-0002: 설계 원칙 감사 결과와 리팩토링 로드맵

- **상태(Status)**: Proposed
- **날짜(Date)**: 2026-06-24
- **결정자(Deciders)**: 프로젝트 오너
- **관련(Related)**: [ADR-0001](0001-adopt-documentation-first-harness.md), `.claude/CLAUDE.md`(계층 의존 규칙), `.claude/agents/clean-arch-guardian`, `.claude/agents/dotnet-code-reviewer`

> 이 ADR은 코드를 바꾸지 않는다. "개발 설계 원칙을 위배하는 코드가 있는가"라는 조사의 **검증된 결과**와,
> 그에 따른 **리팩토링 로드맵(무엇을 고치고 무엇을 의도적으로 보존하는가)** 을 결정으로 남긴다.
> 실제 리팩토링은 각 Wave를 별도 worktree 사이클로 실행한다(§5 후속 과제).

## 1. 맥락과 문제 (Context & Problem)

요청: 설계 원칙(Clean Architecture 계층 의존 · SOLID · YAGNI · DRY · CQRS 슬라이스 규약 · C# 9 LangVersion 제약)을
위반하는 코드를 조사하고 리팩토링 **계획**을 수립한다.

조사 방법: 6개 원칙축을 4개 병렬 감사로 나눠 전수 스캔한 뒤, **머지 게이트에 올릴 핵심 발견은 직접 코드 정독 +
grep 으로 재검증**했다. 이 저장소는 record / init / top-level statement / 손수 만든 mediator(`IRequest`/`IRequestHandler`)
같은 패턴을 **의도적으로 시연하는 데모**이므로, 감사에서 가장 중요한 판단은 *"의도된 교육용 패턴 vs 실제 위반"* 의 구분이다.
실제로 두 감사가 `PlaceOrder`/`CreateOrder` 중복을 두고 정반대 결론을 냈고(고심각 중복 vs 의도된 데모), 코드의 명시적
주석을 읽고서야 후자로 확정됐다.

**결론 미리보기**: 구조적 골격(계층 의존 · C# 9 제약 · CQRS 규약)은 **건강**하다. 위반은 전부 *intra-code* 수준의
작은 항목이며, 가장 눈에 띄는 "중복" 중 하나는 의도된 데모다. 따라서 로드맵의 핵심은 *대규모 재설계가 아니라*,
미사용 표면 제거 + 데모와 무관한 중복의 소규모 추출 + 오너 판단이 필요한 2건의 명시화다.

## 2. 결정 동인 (Decision Drivers)

- **Surgical changes (CLAUDE.md §3)** — 의도된 데모/시연 패턴을 망가뜨리지 않는다. "중복처럼 보인다"는 이유로 교육 가치를 지우지 않는다.
- **YAGNI / 단순성 (CLAUDE.md §2)** — 소비자가 없는 추상화·설정·dead 멤버를 제거해 표면을 줄인다.
- **회귀 위험 최소화** — 문서화된 144개 테스트 스위트를 매 Wave 종료 시 GREEN 으로 유지(성공 기준).
- **위험 대비 가치 우선순위** — 무위험 dead-code 제거를 먼저, 오너 판단이 필요한 항목을 뒤로.
- **계층 불변식 보존** — 어떤 수정도 `Domain→0, Application→Domain, Infrastructure→Application, Api→{App,Infra}` 화살표를 건드리지 않는다.

## 3. 감사 결과 (검증 완료)

### 3.1 통과 (위반 없음)

| 축 | 결과 | 근거 |
|----|------|------|
| **Clean Architecture 계층 의존** | **PASS** | Domain은 NuGet/프로젝트 참조 0. Application은 추상화(`Microsoft.EntityFrameworkCore`의 `DbSet<>`)만 사용, provider/`UseInMemoryDatabase`는 Infrastructure에만 존재. Worker는 Infrastructure만 참조하는 **정당한 제2 컴포지션 루트**, Benchmarks는 test-tier 소비자. `clean-arch-guardian` 검증 PASS. |
| **C# 9 LangVersion 제약** | **PASS** | 9개 csproj 모두 `<LangVersion>9.0</LangVersion>`. file-scoped namespace / `required` / raw string / `u8` / global using **0건** (`required` grep 히트는 전부 문자열·주석). |
| **CQRS 슬라이스 규약** | **준수** | 7개 Command 전부 Validator 보유, 모든 슬라이스가 `TestDbContextFactory` 기반 대응 테스트 보유, DTO는 `Queries/Dtos/`에 정상 배치, 예외 규율 정상(핸들러는 `NotFoundException` throw, `ProblemDetails` 직접 생성 0건). |

### 3.2 검증된 발견 (Findings)

심각도(sev)·검증신뢰도(conf)는 직접 재검증 후의 값. "데모?"는 의도된 시연 여부.

| # | 위치 | 원칙 | 설명 | sev | conf | 데모? |
|---|------|------|------|-----|------|-------|
| F1 | `src/.../Api/Http/DeadlinePropagationHandler.cs`(전체) + `Api/Program.cs:43` | YAGNI | `DelegatingHandler`가 `AddTransient`로 등록만 되고 어떤 `HttpClient`에도 attach되지 않음. 앱에 outbound HTTP 호출 자체가 없음(`AddHttpClient`/`AddHttpMessageHandler` 0건). API 설계 가이드 §7.4 step3을 **소비자 없이** 선구현. | med | high | 아니오(스펙 선구현) |
| F2 | `src/.../Application/.../CancelOrder/...Handler.cs:22`, `ConfirmOrder/...Handler.cs:22`, `Products/.../UpdateProduct`·`DeleteProduct` 핸들러 | DRY | "id로 로드 → null이면 `NotFoundException` → 변경 → SaveChanges" 가드가 4개+ 핸들러에 복붙. | med | med | 아니오 |
| F3 | `Application/.../UpdateProduct/UpdateProductCommandValidator.cs:10` vs `CreateProduct/...Validator.cs:9` | DRY | Name/Description/Price/Stock 규칙 중복(Update는 `Id`만 추가). 메시지 드리프트(Update가 `.WithMessage` 누락). | med | med | 아니오 |
| F4 | `src/.../Application/Common/Models/PagedResult.cs:34` | dead-code | `public const int DefaultPageSize = 20;` — 전 저장소에서 참조 0(컨트롤러는 `pageSize = 20`을 인라인으로 중복). | low | high | 아니오 |
| F5 | `src/.../Domain/Exceptions/DomainException.cs:8` | dead-code | `DomainException(string, Exception)` 2-인자 생성자 — 호출자 0(throw 19곳 전부 1-인자). | low | high | 아니오 |
| F6 | `src/.../Domain/ValueObjects/Money.cs:24` | YAGNI | `operator *(int, Money)` 역방향 오버로드 — 프로덕션은 `Money * int`만 사용, 호출자는 교환법칙 테스트(`MoneyTests.cs:65`) 하나뿐. | low | high | 경계(VO 교환법칙 시연일 수 있음) |
| F7 | `src/.../Api/Program.cs:107-112` | dead-code | 주석 처리된 Swagger 블록(`UseSwagger`/`UseSwaggerUI`). | low | high | 아니오 |
| F8 | `src/.../Infrastructure/Idempotency/DistributedCacheIdempotencyStore.cs:35` | YAGNI | `public KeyLifetime` getter가 오직 `InfrastructureDependencyInjectionTests`의 바인딩 검증에서만 읽힘(프로덕션 미사용). 약한 test-only seam. | low | med | 아니오 |
| F9 | `OrdersController.cs:43-48,52-57` · `ProductsController.cs:39-45` | DRY | "create command Send → `Send(GetByIdQuery)` → `CreatedAtAction(...Success...)`" 3회 복붙. | low | med | 아니오 |
| F10 | `Orders/.../GetOrders/...Handler.cs:22` + `Products/.../GetProducts/...Handler.cs:22` | DRY | 동일 페이징 쿼리 형태(Normalize→Count→OrderByDesc(CreatedAt)/Skip/Take→`PagedResult.Create`) 2회. | low | low | 아니오 |

### 3.3 의도된 데모 — 함부로 제거 금지 (Carve-out)

| 항목 | 위치 | 왜 보존하는가 |
|------|------|----------------|
| **`PlaceOrder` ↔ `CreateOrder` 핸들러 쌍** | `Application/Orders/Commands/{PlaceOrder,CreateOrder}/*Handler.cs` | 두 핸들러 모두 *상호 명시 주석*으로 "single-SaveChanges vs 명시적 트랜잭션(multi-SaveChanges) 시연"임을 선언. 교육 가치가 존재 이유. **핸들러 본체는 보존.** (단 부수 중복·이중 라이브 엔드포인트는 F11에서 별도 판단) |
| 손수 만든 mediator | `Application/Common/Messaging/` | CLAUDE.md가 MediatR 금지·자체구현을 규정. `IRequest`/`IRequestHandler`/`ISender`/`IPipelineBehavior`·`Sender`의 리플렉션 디스패치는 의도된 시연. |
| `IApplicationDbContext` 경계 | `Application/Common/Interfaces/` | Application↔Infrastructure 경계를 강제하는 **필수 구조**(YAGNI 예외). |
| CQRS 슬라이스 계약 | 각 슬라이스 폴더 | 작은 슬라이스라도 Command/Handler/Validator 형태 유지가 규약. |
| `record`/`init`/top-level statement | 전역 | C# 9 시연이 프로젝트의 목적. |

### 3.4 다운그레이드/검토 후 제외 (false-positive 방지)

다음은 감사 후보였으나 검증에서 **실제 위반이 아니거나 방어 가능**으로 판정 — 로드맵에서 추적하지 않는다.

- **`IEventPublisher` ISP** (`Messaging/IEventPublisher.cs:16`): `PublishAsync`는 "프리미티브"이고 `PublishBatchAsync`가 **default interface method**로 이를 순차 호출, Kafka만 batch를 override. Logging fallback이 `PublishAsync`를 실제로 사용하므로 dead가 아니며, primitive+default-batch+override는 일관된 template-method 설계 → 명확한 ISP 위반 아님. (원하면 §4 Wave3 옵션으로 단일화 검토 가능, 저우선.)
- **`ApiExceptionFilter` OCP** (`Filters/ApiExceptionFilter.cs:29-36`): 예외→응답 매핑이 `Dictionary<Type,…>`이고 base-type 순회(line 49)로 부분적으로 open. 승인된 닫힌 예외 집합(§6.2 계약)에 대해 acceptable.
- **`MaintenanceMiddleware` DIP** (`Middleware/MaintenanceMiddleware.cs:29-33`): `IConfiguration`에서 키를 직접 바인딩하나, `IConfiguration` 자체가 추상화이고 프로젝트가 의도적으로 Options/Binder 패키지를 피하는 컨벤션과 일치.
- `RequestLoggingMiddleware`(303줄, 단일 책임 응집), `Infrastructure/DependencyInjection.cs`(컴포지션 루트의 본분), `ValidationBehavior<T>`/`<T,R>` 쌍(공통 로직은 이미 `ValidationRunner`로 추출) — 전부 비위반.

## 4. 결정 (리팩토링 로드맵)

**선택: 위험 오름차순으로 4개 Wave + 1개 "언급만". 각 Wave = 한 worktree 사이클(ADR-0001 문서화 우선 준수), 종료 시 144 테스트 GREEN.**
의도된 데모(§3.3)는 보존하고, 오너 판단이 필요한 2건(F1, F11)은 *결정을 강요하지 않고 옵션과 권고를 제시*한다.

### Wave 1 — 무위험 dead-code 제거 (F4, F5, F7, F6)
- **무엇**: `DefaultPageSize` 상수 삭제(또는 컨트롤러/쿼리 record가 이를 참조하도록 배선 — 권고: **삭제**, 인라인 `20`이 이미 동작), `DomainException` 2-인자 생성자 삭제, 주석 Swagger 블록 삭제, `Money` 역방향 `*` 오버로드 삭제(+ 전용 테스트). F6는 VO 교환법칙 시연을 남기고 싶다면 보존 가능 — **권고: 삭제**(프로덕션 무사용).
- **계층**: Domain(F5,F6), Application(F4), Api(F7). 계층 영향 없음(import 변화 없음 → domain-layer-guard 무관).
- **수용 기준**: 빌드 OK, 144 테스트 GREEN(F6 삭제 시 `MoneyTests` 교환법칙 케이스 1건 동반 제거). 제거 항목에 대한 참조 0 재확인.
- **위험**: 매우 낮음. 단일 커밋 권장.

### Wave 2 — YAGNI 표면 정리 (F1, F8)
- **F1 `DeadlinePropagationHandler`** — outbound HTTP가 생기기 전까지 소비자 없는 선구현. **옵션**:
  - (a) **제거** — 핸들러 + `AddTransient` 등록 + (불필요해지면)`AddHttpContextAccessor` + 관련 테스트 정리. *권고*: 현재 다운스트림 호출 계획이 없으면 (a).
  - (b) **유지하되 명시** — §7.4 참조 구현으로 보존한다고 결정한다면, 코드/문서에 "의도적 미배선 참조"임을 표시(F1을 carve-out으로 승격).
  - → **오너 결정 필요**(API 설계 가이드 §7.4 로드맵 의존).
- **F8 `KeyLifetime`** — 바인딩 테스트를 유지할 가치가 있으면 보존, 아니면 `_keyLifetime` 인라인 + getter 삭제. **권고**: 컴포지션 루트에서 바인딩을 검증하도록 테스트를 옮기고 getter 제거(저우선).
- **수용 기준**: 빌드 OK, 144 테스트 GREEN(F1 (a) 선택 시 해당 핸들러 테스트 동반 제거). `@dotnet-code-reviewer` APPROVE.
- **위험**: 낮음(공개 표면 제거 — 외부 소비자 없음 확인됨).

### Wave 3 — 데모와 무관한 DRY 추출 (F2, F3, 옵션 F9·F10)
- **F2**: `IApplicationDbContext`에 `FindOrThrowAsync<T>(id, ct)` 헬퍼(또는 작은 확장)로 "로드-or-NotFound" 가드 단일화 → 4개+ 핸들러 적용. *주의*: Application 경계 안에서만(확장 메서드 위치는 Application).
- **F3**: 공유 `ProductRules` 규칙 세트로 Create/Update validator 중복 제거 + 메시지 드리프트 일치. (CQRS 규약상 슬라이스별 Validator 클래스는 유지하되 규칙만 공유.)
- **F9(옵션)**: `ApiControllerBase`에 `CreatedFrom<TQuery>(id)` protected 헬퍼 추출(컨트롤러 3곳). 저우선.
- **F10(옵션)**: `IQueryable<T>` 페이징 확장. **권고: 보류** — 사이트 2곳 + 프로젝트 YAGNI 기조상 가치 낮음.
- **수용 기준**: 빌드 OK, 144 테스트 GREEN, `@clean-arch-guardian` PASS(계층 미위반), `@dotnet-code-reviewer` APPROVE. 추출된 헬퍼에 대한 단위 테스트 추가.
- **위험**: 중간(다수 핸들러/validator 동시 수정) → F2/F3을 별도 커밋으로 분리.

### Wave 4 — `PlaceOrder` 중복: 데모 보존 + 부수 중복만 (F11)
F11 = §3.3의 핸들러 쌍에 딸린 *부수* 중복과 API 표면 문제. **핸들러 본체는 보존.**
- (i) **byte-identical validator** (`PlaceOrderCommandValidator.cs:5` ≡ `CreateOrderCommandValidator.cs:5`) 와 **item DTO** (`PlaceOrderItemDto` ≡ `CreateOrderItemDto`)는 데모를 깨지 않고 공유 입력 계약으로 단일화 가능 → *권고*: 공유 검토(단, 슬라이스 독립성과 trade-off).
- (ii) **이중 라이브 엔드포인트** (`POST /orders` + `POST /orders/place`)가 거의 동일 동작 → **오너 결정 필요**: 둘 다 공개 API로 유지할지, `/place`를 문서용으로만(라우팅 제외) 둘지. API 가이드 일관성 영향.
- **수용 기준**: (i) 적용 시 빌드+테스트 GREEN; (ii)는 결정만 기록(코드 변경은 결정에 종속). 데모 주석/의도 보존 확인.
- **위험**: 낮음(코드) / 정책(엔드포인트는 제품 결정).

### 언급만 (추적 안 함)
§3.4의 IEventPublisher / ApiExceptionFilter / MaintenanceMiddleware — 현재 비위반. 향후 다운스트림·예외집합 확장이 생기면 재평가.

## 5. 결과 (Consequences)

**긍정적**
- 미사용 표면(F1·F4·F5·F6·F7·F8)이 줄어 읽는 사람이 "왜 있지?"를 묻지 않게 된다.
- 데모와 무관한 중복(F2·F3)이 단일 소스로 모여 메시지 드리프트 같은 버그 표면이 사라진다.
- **의도된 교육용 패턴이 보존**된다 — 감사 자동화의 흔한 실패(데모를 위반으로 오인해 삭제)를 회피.

**부정적 / 감수하는 비용**
- F1·F11(ii)은 **오너 판단**이 필요해 이 ADR만으로 종결되지 않는다(의도된 미결).
- F2의 `FindOrThrowAsync` 헬퍼는 작은 추상화 — 사이트가 4개+이므로 YAGNI 예외에 해당하나, 1~2개로 줄면 inline이 더 단순.
- Wave 분할로 커밋/worktree 사이클이 여러 번 발생(문서화 우선 마찰은 의도된 것).

**후속 과제 (Follow-ups)**
- 각 Wave를 `feat-refactor-wave<N>` worktree로 실행(또는 `/harness <Wave N 목표>`). Wave 1→4 순서 권장(위험 오름차순).
- F1·F11(ii) 오너 결정 후 이 ADR 상태를 `Accepted`로 갱신하고 결정 사항을 §4에 반영(또는 별도 ADR로 분기).
- Wave 실행 시 README의 엔드포인트 표(특히 `/orders/place` 거취)·디렉터리 트리 검수.

## 6. 계층 영향 (Clean Architecture)

- Wave 1: Domain(dead ctor·op 삭제) / Application(상수) / Api(주석) — import·참조 변화 없음, 계층 규칙 무관.
- Wave 2: Api(핸들러·DI) / Infrastructure(getter) — Api→Infra, Infra 내부. 위반 없음.
- Wave 3: Application(헬퍼·validator 규칙) — Application 내부, Domain 의존만 유지. 위반 없음.
- Wave 4: Application(validator/DTO) / Api(엔드포인트) — 위반 없음.
- **어떤 Wave도 계층 화살표를 바꾸지 않는다.** Domain 편집(F5·F6)은 `using` 변화가 없어 domain-layer-guard와 무관.

## 7. 링크 (Links)

- [ADR-0001](0001-adopt-documentation-first-harness.md) — 문서화 우선 워크플로(각 Wave 실행 시 준수).
- `.claude/CLAUDE.md` — 계층 의존 규칙 · CQRS 규약 · 검증/예외 매핑 약속.
- 감사 대상 파일: 위 §3.2/§3.3 표의 `file:line`.
- 실행 진입점: `/harness <Wave 목표>` 또는 `work-orchestrator` 에이전트, 검증은 `/check-arch` + `/test` + `@dotnet-code-reviewer`.

## 8. 실행 이력 (Execution Log)

- **2026-06-24 — Wave 1 실행 완료**: 무위험 dead-code 4건 제거 —
  F4 `PageRequest.DefaultPageSize` 상수(`PagedResult.cs`),
  F5 `DomainException(string, Exception)` 2-인자 생성자(`DomainException.cs`),
  F6 `Money` 역방향 `operator *(int, Money)` + 교환법칙 테스트(`Money.cs` / `MoneyTests.cs`),
  F7 주석 처리된 Swagger 블록(`Program.cs`). 테스트는 교환법칙 케이스 1건 동반 제거로 −1.
  이 ADR은 나머지 Wave(2~4) + 오너 결정(F1·F11(ii)) 미결로 상태 `Proposed` 유지.
- **2026-06-24 — Wave 2 실행 완료 (오너 결정: F1=제거, F8=유지)**:
  F1 `DeadlinePropagationHandler` 제거 — 핸들러 + 통합테스트(`DeadlinePropagationHandlerTests`) +
  `AddTransient<DeadlinePropagationHandler>()`·`AddHttpContextAccessor()` 등록 + `using CleanArchitecture.Api.Http` +
  §7.4 step3 주석을 삭제. 인바운드 `DeadlinePropagationMiddleware`와 그 상수(`DeadlineHeader`/`DeadlineItemKey`)는
  여전히 사용 중이라 **보존**.
  F8 `DistributedCacheIdempotencyStore.KeyLifetime` getter는 §7.1 키 수명 바인딩 동작을 검증하는 통합테스트 4건을
  뒷받침하므로 **유지**(YAGNI보다 테스트 가치 우선). 상태는 나머지 Wave(3~4) + F11(ii) 미결로 `Proposed` 유지.
