---
name: document-first
description: Use at the START of any substantive change (new feature, system improvement, multi-layer refactor) to decide and write the required PRD or ADR before touching code, and to remember the README-before-commit rule. Also use when unsure whether something needs a PRD vs an ADR, or when the doc-first-guard hook blocks a code edit.
---

# 문서화 우선 (Documentation-First)

이 프로젝트의 운영 규칙: **시스템 개선/신규 기능 등 실질적 작업은 코드보다 문서가 먼저다.**
목표는 개발 과정의 모든 결정을 영속적으로 남겨, 이후 기능 추가·개선 시 재활용하는 것.
근거와 결정 자체는 [ADR-0001](../../../docs/adr/0001-adopt-documentation-first-harness.md) 참고.

## 강제 메커니즘

`.claude/hooks/doc-first-guard.sh` (PreToolUse) 가 **worktree 안** `src/`·`tests/` 의 `.cs`/`.csproj`
편집을, 해당 worktree에 `docs/adr/**` 또는 `docs/prd/**` 변경(커밋/uncommitted)이 없으면 **차단**한다.
이 가드를 우회하지 마세요 — 막히면 "문서를 아직 안 썼다"는 신호다. `/doc` 로 문서를 먼저 만들면 풀린다.

면제(가드가 발동 안 함): `.claude/**`, 모든 `*.md`, 문서 자체, 그리고 main 체크아웃의 in-place 편집
(이건 worktree-isolation-guard가 관할 — 사용자가 명시적으로 허용한 사소한 수정).

## PRD vs ADR — 무엇을 쓰나

| | PRD (Product Requirements) | ADR (Architecture Decision) |
|---|---|---|
| 답하는 질문 | **무엇을, 누구를 위해, 왜** | **어떻게, 왜 그 선택** |
| 트리거 | 신규 기능 / 사용자가 보는 동작 추가 | 기술/구조 결정 (라이브러리·패턴·데이터 흐름·계층 배치) |
| 예 | "주문 대량 업로드 기능", "재고 알림" | "폴링 outbox → CDC 교체", "멱등 저장소를 Redis로" |
| 위치 | `docs/prd/NNNN-<slug>.md` | `docs/adr/NNNN-<slug>.md` |
| 템플릿 | `.claude/templates/prd-template.md` | `.claude/templates/adr-template.md` |

판단 흐름:
1. 사용자가 보는 **새 능력/동작**을 추가하나? → **PRD**.
2. 한 가지 **기술/구조 갈림길**에서 선택을 하나(대안이 있었나)? → **ADR**.
3. 둘 다(큰 기능 + 핵심 기술 결정)? → **PRD 1개 + ADR 1개 이상**, 서로 링크.
4. 어느 쪽도 아닌 **사소한 수정/버그픽스**? → 문서 불필요(가드도 main에선 발동 안 함). 단 worktree에서 작업한다면 어떤 변경인지 한 줄짜리 ADR이라도 남기는 게 목표에 부합.

애매하면 ADR로 기운다 — 결정의 *왜* 를 남기는 게 핵심 목표이므로.

## 절차 (작업 시작 시)

```
1. 분류        → 이 작업은 PRD인가 ADR인가 (위 표).      verify: 둘 중 하나 확정
2. /doc <type> <제목>  → 다음 번호로 템플릿 스캐폴딩.      verify: docs/{prd,adr}/NNNN-*.md 생성됨
3. 채우기      → 맥락/문제, 결정 동인, (ADR)선택지·결정 / (PRD)목표·인수기준.
                                                          verify: 플레이스홀더 <...> 가 실제 내용으로
4. 인덱스      → docs/<type>/README.md 표에 행 추가.       verify: 목록에 보임
5. 이제 코드   → worktree 안에서 구현 시작. 가드가 풀린다.  verify: src/ 편집이 차단되지 않음
```

> Phase 0로서의 위치: `harness-loop`, `work-orchestrator`, `worktree-workflow` 모두 **Plan 전(또는 Plan 직후, 코드 전)** 에 이 단계를 둔다. `/add-cqrs` 도 슬라이스 코드 전에 PRD/ADR을 요구한다.

## README 검수·갱신 (commit 직전, 필수)

merge 사이클에서 **커밋 직전** README를 검수한다. 변경이 다음 중 하나라도 건드리면 README를 갱신:
- 새 API 엔드포인트 / 요청·응답 형식 변경
- 새 설정 키(`appsettings.json`) / 환경 변수
- 새 동작·모드(예: 점검 모드, 멱등성 같은 사용자가 보는 기능)
- 디렉터리 트리 변화(새 슬라이스·새 프로젝트)

해당 없으면 "README 변경 불필요"라고 명시. README는 `.md` 라 가드에 안 막히니 worktree 안에서 자유롭게 수정.

## 안티패턴

- ❌ 빈/형식뿐인 문서로 가드만 통과시키기 — 문서의 목적은 *왜* 를 남기는 것. 코드 리뷰가 이를 본다.
- ❌ 코드부터 짜고 나중에 문서 — 가드가 막고, 사후 문서는 결정의 맥락을 잃는다.
- ❌ 기존 문서 번호 재사용·덮어쓰기 — 번호는 단조 증가. 뒤집힌 결정은 상태만 `Superseded`/`Dropped`.
- ❌ doc-first-guard `--no-verify` 우회 — 막히면 문서를 쓰라는 뜻.

## 관련

- `.claude/commands/doc.md` — `/doc` 스캐폴딩
- `.claude/templates/{adr,prd}-template.md` — 표준 템플릿
- `.claude/skills/worktree-workflow/SKILL.md` — Phase 0가 끼워지는 전체 사이클
- `docs/adr/README.md`, `docs/prd/README.md` — 인덱스 + 컨벤션
