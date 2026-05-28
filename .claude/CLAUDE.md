# CleanArchitecture — Harness Guide

ASP.NET Core 9 (.NET 9) / **C# 9 고정** (`<LangVersion>9.0</LangVersion>`) Clean Architecture 샘플. record / init / top-level statements 데모에 집중되어 있으므로 **`required`, file-scoped namespace, raw string literal 등 C# 10+ 기능은 사용 금지**.

## 계층 의존 규칙 (가장 중요한 불변식)

```
Api ─────────┐
              ├──► Application ──► Domain
Infrastructure
```

| 계층 | 허용 의존성 | 절대 금지 |
|------|-------------|-----------|
| **Domain** | (없음) | 외부 NuGet 0개, 다른 프로젝트 참조 0개. MediatR/EF Core/AutoMapper/FluentValidation 모두 금지 |
| **Application** | Domain | Infrastructure, Api, EF Core 구현체 (`UseInMemoryDatabase` 등) |
| **Infrastructure** | Application (+ EF Core 구현체) | Api |
| **Api** | Application, Infrastructure | (composition root만) |

`.claude/hooks/domain-layer-guard.sh` 가 Domain 편집 시 금지 import 를, `.claude/hooks/application-layer-guard.sh` 가 Application 편집 시 Infrastructure/Api 참조 및 EF Core 구현체(`UseInMemoryDatabase` 등) 를 자동 차단합니다. 이 가드를 우회하지 마세요 — 우회가 필요하면 가드 자체가 잘못된 것이므로 사용자에게 보고하세요.

## CQRS 슬라이스 패턴 (필수)

새 기능은 **수직 슬라이스** 로 추가합니다. 한 슬라이스 = 한 폴더:

```
Application/<Feature>/Commands/<Action><Feature>/
  ├── <Action><Feature>Command.cs       (record + IRequest<T>)
  ├── <Action><Feature>CommandHandler.cs (IRequestHandler<,>)
  └── <Action><Feature>CommandValidator.cs (AbstractValidator) — Command 만, Query 는 옵션
```

Query 도 동일 구조 (`Queries/Get<X>/`), DTO 는 `Queries/Dtos/` 에 별도 파일.

각 슬라이스에는 **반드시** 대응 테스트가 따라옵니다:
- `tests/Application.UnitTests/<Feature>/Commands/<Action><Feature>/<Handler|Validator>Tests.cs`
- Application 테스트는 **TestDoubles/TestDbContext** (자체 `IApplicationDbContext` 구현) 사용. Infrastructure 의 `ApplicationDbContext` 를 직접 참조하지 마세요 — `IApplicationDbContext` 가 Application 의 경계임을 강제하는 의도된 분리입니다.

스캐폴딩은 `/add-cqrs <Feature> <Action>` 또는 `@cqrs-feature-scaffolder` 서브에이전트로.

## 검증·예외 매핑 약속

- **Command 검증** = `FluentValidation` (Validator 클래스). `ValidationBehavior<,>` 가 파이프라인에서 자동 실행 → 실패 시 `Application.Common.Exceptions.ValidationException`.
- **도메인 불변식** = `Domain.Exceptions.DomainException` (생성자/메서드에서 throw). Validator 에 중복으로 넣지 말고 두 곳 다 의미 있는 방어선으로 유지.
- **NotFound** = `Application.Common.Exceptions.NotFoundException` (핸들러에서 throw).
- `ApiExceptionFilter` 가 위 세 예외 → 400/404 매핑. 핸들러에서 직접 `ProblemDetails` 만들지 마세요.

## 테스트 분리 원칙

| 프로젝트 | 격리 | 무엇을 검증 |
|----------|------|-------------|
| `Domain.UnitTests` | 외부 의존 0 | 엔티티 불변식 / 도메인 메서드 동작 |
| `Application.UnitTests` | `TestDbContextFactory.Create()` (테스트마다 새 GUID InMemory DB) | Handler 로직 + Validator 규칙 |
| `Api.IntegrationTests` | `WebApplicationFactory<Program>` | HTTP 파이프라인 (필터·매핑·검증·EF Core 전체) |

`Program` 진입점 노출을 위한 `public partial class Program {}` 라인 (Program.cs 마지막) 을 삭제하지 마세요 — 통합 테스트가 깨집니다.

## 빌드 / 테스트 명령

```bash
dotnet build                                                            # 전체
dotnet test                                                             # 전체 (28 tests)
dotnet test tests/CleanArchitecture.Domain.UnitTests              # Domain 만
dotnet test tests/CleanArchitecture.Application.UnitTests         # Application 만
dotnet test tests/CleanArchitecture.Api.IntegrationTests          # API 통합 만
dotnet run --project src/CleanArchitecture.Api                    # 실행 (https://localhost:5001)
```

`/test`, `/build` 슬래시 커맨드는 위를 래핑하면서 출력을 핵심만 추려서 보여줍니다.

## 작업 워크플로 (코드 수정 = worktree 격리 필수)

**모든 코드 수정 작업은 git worktree 안에서 시작합니다.** 이는 협상 가능한 가이드라인이 아니라 하네스의 운영 규칙입니다.

```
[plan] → EnterWorktree → [implement (가능하면 team 병렬)] → build + test → 통과시 rebase + fast-forward 머지(선형 히스토리) → 실패시 worktree 보존하여 디버그
```

1. **계획 (in-place)** — 어떤 계층이 닿는지, 무엇이 병렬 가능한지 식별. 읽기/탐색만 필요한 단계에서는 worktree 를 만들지 않습니다.
2. **격리** — 코드/csproj 수정이 시작되기 직전에 `EnterWorktree` 호출 (이름: `feat-<짧은-주제>`). 이후 모든 편집은 worktree 안에서.
3. **병렬 구현** — Plan 에서 식별한 독립 단위가 둘 이상이면 `TeamCreate` 로 팀을 만들고, 단위마다 별도 Agent 를 spawn 해서 동시 진행. 의존성 있는 단위는 `TaskUpdate addBlockedBy` 로 순서를 강제. 단일 단위면 팀 없이 직접 수행.
4. **검증** — `dotnet build && dotnet test`. PostToolUse 훅이 포맷팅, PreToolUse 훅이 Domain/Application 가드를 자동 수행합니다.

> **강제**: `.claude/hooks/worktree-isolation-guard.sh` (PreToolUse) 가 `src/`·`tests/` 의 코드 파일(`.cs`/`.csproj`) 및 `.sln` 을 worktree 밖(메인 체크아웃)에서 편집하려는 시도를 차단합니다. 코드 편집은 반드시 `EnterWorktree` 후 worktree 안에서. `.claude/` 설정·문서(`.md`) 편집은 면제되어 in-place 로 가능합니다.
5. **자동 merge & cleanup (선형 히스토리)** — 빌드 + 테스트 모두 통과시 worktree 안에서 의미 있는 커밋(들) 작성 → 같은 worktree 에서 `git rebase main` 으로 베이스 위에 선형화 → `ExitWorktree(action: "keep")` 로 main 체크아웃에 복귀 → `git merge --ff-only <branch>` → `git worktree remove .claude/worktrees/<name>` → `git branch -d <branch>`. **반드시 fast-forward 머지** — `--no-ff` 금지(머지 커밋이 히스토리에 끼면 안 됨). rebase 도중 충돌이 나면 `git rebase --abort` 후 worktree 를 그대로 보존하고 사용자에게 보고. 실패시 worktree 와 브랜치를 *그대로* 두고 사용자에게 실패 요약 보고. 절대 worktree 를 강제 삭제하지 마세요.
6. **계층 검증** — Domain 이나 csproj 를 건드렸다면 머지 *전* `/check-arch` 로 의존 그래프 재확인.

이 사이클 전체는 `work-orchestrator` 에이전트가 단일 패스로 오케스트레이션합니다(Claude 자율 진행 시). 사용자가 명시적으로 `/harness <목표>` 를 입력하면 같은 사이클을 자율 루프로(첫 GREEN 에서 종료, 실패 시 최대 3 회) 돌립니다. CQRS 슬라이스 추가는 `/add-cqrs` 가 자체적으로 worktree 사이클을 포함합니다.

### 병렬 팀 구성 가이드

- **언제 팀을 만드나** — Plan 시 작업을 독립 단위로 쪼갰을 때, 단위가 ≥ 2 개이면 팀.
- **자연스러운 병렬 단위**:
  - 여러 독립 CQRS 슬라이스 (예: `CreateOrder`, `CancelOrder` 동시 추가) — 슬라이스 당 한 agent.
  - 계층별 변경이 독립인 경우 (예: Domain 엔티티 추가 + 무관한 API 필터 수정).
  - 새 기능 추가와 그 기능의 통합 테스트 작성 (Handler 가 작성된 후에는 Validator 와 IntegrationTest 가 병렬 가능).
- **자연스럽지 *않은* 병렬 단위 (순차 처리)**:
  - 같은 슬라이스 안 Command → Handler → Validator (의존성 체인).
  - 같은 파일을 여러 에이전트가 동시 편집.
- **권장 에이전트 매핑**:
  - 슬라이스 스캐폴딩 → `cqrs-feature-scaffolder`
  - 테스트 작성 / 실행 / 분석 → `dotnet-test-runner`
  - 계층 / 의존성 검증 → `clean-arch-guardian`
  - 리뷰 → `dotnet-code-reviewer`

### worktree 사용 시 주의

- worktree 는 `.claude/worktrees/<name>/` 에 생성됩니다 (`.gitignore` 에서 제외됨).
- 사용자가 명시적으로 거부한 경우에만 worktree 를 건너뜁니다 (예: "이번 한 번만 in-place 로").
- 빌드 출력(`bin/`, `obj/`) 은 worktree 안에서도 무시되므로 머지 충돌 걱정 없음.
- 실패 후 worktree 를 유지하면 사용자가 직접 들어가서 진단 가능.

## 하네스 구성

**Agents** (`.claude/agents/`)
- `clean-arch-guardian` — 계층 의존 규칙 위반 탐지
- `cqrs-feature-scaffolder` — 새 슬라이스 일괄 생성 (worktree + 내부 병렬화 포함)
- `dotnet-test-runner` — 계층 인지 테스트 실행 + 실패 요약
- `dotnet-code-reviewer` — .NET / C# 9 / Clean Arch 코드 리뷰
- `work-orchestrator` — worktree 사이클 + 병렬 팀 조립 담당 (다단계 작업의 단일 패스 엔진; `/harness` 루프가 이 엔진을 반복 호출)

**Skills** (`.claude/skills/`)
- `add-cqrs-feature` — CQRS 슬라이스 추가 절차
- `verify-architecture` — 계층 의존 검증 절차
- `dotnet-debug` — 흔한 .NET 문제 진단 레시피
- `worktree-workflow` — worktree → verify → merge 사이클 표준 절차
- `harness-loop` — Plan → Generate → Evaluate 자율 루프 (최대 3 회 반복, 실패시 worktree 보존)

**Commands** (`.claude/commands/`)
- `/harness <목표>` — 다단계 작업의 단일 오케스트레이션 진입점. Plan → Generate → Evaluate 사이클을 돌리며 **첫 GREEN 에서 종료**(= 단순 목표는 단일 패스), 실패 시에만 피드백 기반으로 최대 3 회 재시도. 자율 호출 금지 — 사용자가 직접 `/harness` 를 입력했을 때만 실행. (Claude 자율 진행이 필요하면 `/harness` 대신 `work-orchestrator` 에이전트를 직접 사용)
- `/add-cqrs` — CQRS 슬라이스 추가 (worktree 포함)
- `/check-arch` — 계층 의존 즉시 검증
- `/test [layer]` — 계층 인지 테스트
- `/review` — 변경 사항 코드 리뷰
- `/build` — 컴팩트한 dotnet build

새 작업을 시작할 때 가장 잘 맞는 에이전트가 있는지 먼저 확인하세요. 일반적으로 다단계 코드 변경은 `work-orchestrator` 에이전트(또는 사용자가 `/harness` 입력), 슬라이스 추가는 `/add-cqrs`, 빠른 검증은 `/check-arch` + `/test` 조합이 좋습니다. `/harness` 는 첫 GREEN 에서 종료되므로 단순 목표에도 부담 없이 쓸 수 있고, 한 번에 맞추기 어려워 빌드/테스트 출력을 보고 재계획해야 하는 목표에서는 자동으로 최대 3 회까지 루프를 돕니다.
