# PRD-NNNN: <기능 이름>

- **상태(Status)**: Draft | In Review | Approved | Shipped | Dropped
- **날짜(Date)**: YYYY-MM-DD
- **담당(Owner)**: <이름 / 역할>
- **관련(Related)**: <ADR-XXXX, 이슈/PR, 구현 슬라이스 — 없으면 `-`>

> PRD(Product Requirements Document)는 **신규 기능/제품 요구사항**의 *무엇을, 누구를 위해, 왜*를 남긴다.
> 기술적으로 *어떻게* 구현할지의 구조적 결정은 ADR에 적는다(→ `.claude/templates/adr-template.md`).
> 큰 기능은 PRD(무엇) + ADR(핵심 기술 결정)를 함께 둘 수 있다.

## 1. 배경 (Background)

<왜 이 기능이 필요한가. 어떤 사용자/운영 문제를 푸는가. 현재 무엇이 부족한가.>

## 2. 목표 / 비목표 (Goals / Non-Goals)

**목표**
- <이 기능이 달성해야 하는 것 — 측정 가능하게>

**비목표(Non-Goals)**
- <이번에 *의도적으로* 하지 않는 것. 범위를 좁혀 과설계를 막는다.>

## 3. 요구사항 (Requirements)

**기능 요구사항(Functional)**
- <사용자/시스템이 할 수 있어야 하는 것. 가능하면 "~할 때 ~한다" 형태.>

**비기능 요구사항(Non-Functional)**
- <성능(예: p99 지연), 정합성, 동시성, 멱등성, 보안, 운영 등 — 해당하는 것만>

## 4. 인수 기준 (Acceptance Criteria)

> 하네스 Evaluate 단계가 검증할 **구체적** 기준. 모호한 기준("동작하면 됨")은 금지.

- [ ] <검증 가능한 조건 1 — 어떤 테스트/응답/지표로 확인하는가>
- [ ] <검증 가능한 조건 2>
- [ ] <...>

## 5. 범위 밖 (Out of Scope)

- <이 PRD가 다루지 않는 인접 영역. 후속 PRD로 미루는 것.>

## 6. 미해결 질문 (Open Questions)

- <결정 전 해소해야 할 불확실성. 해소되면 ADR로 승격되기도 한다.>

## 7. 링크 (Links)

- <관련 ADR(핵심 기술 결정), 구현 슬라이스 경로(`Application/<Feature>/...`), API 엔드포인트, PR>
