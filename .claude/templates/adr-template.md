# ADR-NNNN: <결정 제목 — 명령형 한 줄>

- **상태(Status)**: Proposed | Accepted | Superseded by [ADR-XXXX](XXXX-...md) | Deprecated
- **날짜(Date)**: YYYY-MM-DD
- **결정자(Deciders)**: <이름 / 역할>
- **관련(Related)**: <PRD-XXXX, ADR-XXXX, 이슈/PR 링크 — 없으면 `-`>

> ADR(Architecture Decision Record)은 **기술/구조적 결정**의 *왜*를 남긴다.
> "왜 이 선택을 했는가, 어떤 대안을 버렸는가, 어떤 대가를 치르는가"를 기록한다.
> 신규 기능의 *무엇/누구를 위해* 는 PRD에 적는다(→ `.claude/templates/prd-template.md`).

## 1. 맥락과 문제 (Context & Problem)

<어떤 상황에서 어떤 문제·압박이 결정을 요구하는가. 현재 구조의 제약, 트리거가 된 요구사항.
사실 위주로. "결정해야 하는 이유"가 한 문단에 드러나야 한다.>

## 2. 결정 동인 (Decision Drivers)

- <중요하게 고려한 힘 — 성능, 단순성, 계층 의존 규칙, 운영 비용, 마감 등>
- <...>

## 3. 검토한 선택지 (Considered Options)

1. **<옵션 A>** — <한 줄 요약>
2. **<옵션 B>** — <한 줄 요약>
3. **<옵션 C>** — <한 줄 요약>

각 옵션의 장단점:

| 옵션 | 장점 | 단점 |
|------|------|------|
| A | <...> | <...> |
| B | <...> | <...> |
| C | <...> | <...> |

## 4. 결정 (Decision Outcome)

**선택: 옵션 <X>.**

<왜 이 옵션인가. 결정 동인과 어떻게 연결되는가. 반드시 근거를 적는다 —
"그게 나아 보여서"는 금지.>

## 5. 결과 (Consequences)

**긍정적**
- <이 결정으로 좋아지는 것>

**부정적 / 감수하는 비용**
- <트레이드오프, 새로 생기는 제약, 향후 갚아야 할 빚>

**후속 과제(Follow-ups)**
- <이 결정 이후 별도로 다뤄야 할 항목 — 새 이슈/ADR/PRD로 분기되면 링크>

## 6. 계층 영향 (Clean Architecture)

<Domain / Application / Infrastructure / Api 중 어디가 닿는가. 계층 의존 규칙
(`.claude/CLAUDE.md` → 계층 의존 규칙)을 위반하지 않는지 한 줄로 확인.
해당 없으면 `해당 없음`.>

## 7. 링크 (Links)

- <관련 PRD/ADR, 구현 슬라이스 경로(`Application/<Feature>/...`), PR, 외부 문서>
