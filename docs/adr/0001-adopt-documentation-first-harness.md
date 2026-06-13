# ADR-0001: 하네스에 문서화 우선(PRD/ADR) 워크플로 도입

- **상태(Status)**: Accepted
- **날짜(Date)**: 2026-06-13
- **결정자(Deciders)**: 프로젝트 오너
- **관련(Related)**: `.claude/skills/document-first/SKILL.md`, `.claude/hooks/doc-first-guard.sh`, `/doc` 커맨드

> 이 ADR은 새 워크플로를 도입한 결정 자체를 도그푸딩으로 기록한다 — 앞으로의 모든
> 시스템 개선/신규 기능이 따를 패턴의 첫 사례.

## 1. 맥락과 문제 (Context & Problem)

프로젝트 개발 과정의 결정과 요구사항이 코드와 커밋 메시지에만 흩어져 남아, 이후 기능 추가·개선
시 "왜 이렇게 했는가"를 추적하기 어렵다. 목표는 **개발 과정의 모든 실질적 결정을 문서로 남겨**
나중에 재활용하는 것이다. 하네스는 이미 layer-guard·worktree-isolation을 하드 차단 훅으로
운영 중이므로, 문서화도 같은 강도의 운영 규칙으로 끌어올릴 수 있다.

## 2. 결정 동인 (Decision Drivers)

- 결정 이력의 영속성 — 코드만으로는 *왜* 가 사라진다.
- 기존 가드와의 일관성 — 하드 훅이 이미 프로젝트의 강제 메커니즘.
- 과설계 회피 — 사소한 수정까지 문서를 강요하면 마찰만 커진다(실질 작업에만 적용).
- 재활용성 — 표준 템플릿·번호 체계가 있어야 나중에 검색·연결이 된다.

## 3. 검토한 선택지 (Considered Options)

1. **하드 훅 차단** — PreToolUse가 worktree 안 `src/`·`tests/` 코드 편집을, 해당 worktree에 PRD/ADR 변경이 없으면 차단.
2. **소프트 경고 훅** — 같은 조건이지만 경고만, 편집은 진행.
3. **지시문 레벨만** — 훅 없이 harness 스킬/에이전트의 Phase 0로만 강제.

| 옵션 | 장점 | 단점 |
|------|------|------|
| 하드 훅 | 우회 불가, 기존 가드와 일관, "모든 것을 문서로" 목표에 부합 | 사소한 작업 마찰 — 적용 범위를 좁혀 완화 |
| 소프트 경고 | 마찰 적음 | 무시되기 쉬움 → 목표 미달 |
| 지시문만 | 가장 가벼움 | Claude가 우회 가능, 강제력 약함 |

## 4. 결정 (Decision Outcome)

**선택: 옵션 1 — 하드 훅 차단.** 단 적용 범위를 **실질적 작업**(worktree 안 `src/`·`tests/`의
`.cs`/`.csproj`)으로 한정한다. `.claude/**`·`*.md`·문서 자체는 면제하고, main 체크아웃의
in-place 수정은 기존 worktree-isolation-guard가 관할하므로 자연히 면제된다.

구성 요소:
- `.claude/hooks/doc-first-guard.sh` — worktree 안 코드 편집 시 `docs/adr/` 또는 `docs/prd/` 변경(커밋/uncommitted) 존재를 요구.
- `.claude/templates/{adr,prd}-template.md` — 하네스가 관리하는 표준 템플릿.
- `/doc <adr|prd> <제목>` — 다음 번호로 템플릿 스캐폴딩.
- `document-first` 스킬 + harness/work-orchestrator/worktree-workflow의 **Phase 0 — Document**.
- commit 직전 **README 검수·갱신** 단계를 머지 사이클에 추가.

## 5. 결과 (Consequences)

**긍정적**
- 모든 실질 작업이 PRD/ADR로 시작 → 결정 이력이 영속적으로 남는다.
- 기존 가드와 동일한 강도 → 우회 불가, 팀 전체 일관.
- README가 커밋마다 최신 상태 유지.

**부정적 / 감수하는 비용**
- 기능 작업 시작에 문서 작성 한 단계 추가(의도된 마찰).
- doc-first-guard의 판정은 "doc 파일이 브랜치에 존재하는가"라는 휴리스틱 — 빈 문서로 통과시키는 우회가 이론상 가능(코드 리뷰에서 막는다).

**후속 과제(Follow-ups)**
- 첫 신규 기능 작업 시 PRD-0001 작성으로 PRD 흐름 검증.
- 빈/형식뿐인 문서 방지가 필요하면 후속 ADR에서 내용 검증 강화 검토.

## 6. 계층 영향 (Clean Architecture)

해당 없음 — 하네스(`.claude/`)와 문서(`docs/`)만 변경, 애플리케이션 계층(Domain/Application/
Infrastructure/Api) 코드는 건드리지 않는다.

## 7. 링크 (Links)

- `.claude/skills/document-first/SKILL.md` — PRD vs ADR 판단 + 절차
- `.claude/hooks/doc-first-guard.sh` — 강제 훅
- `.claude/commands/doc.md` — `/doc` 스캐폴딩
- `.claude/templates/adr-template.md`, `.claude/templates/prd-template.md`
