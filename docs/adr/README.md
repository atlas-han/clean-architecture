# Architecture Decision Records (ADR)

기술/구조적 **결정의 *왜***를 남기는 곳. 한 결정 = 한 파일.

- **템플릿**: `.claude/templates/adr-template.md`
- **스캐폴딩**: `/doc adr <제목>` (worktree 안에서 다음 번호로 생성)
- **파일명**: `NNNN-<slug>.md` (4자리 zero-pad, 소문자 하이픈 slug). 예: `0002-replace-polling-outbox-with-cdc.md`
- **번호**: 단조 증가, 재사용 금지. 뒤집힌 결정도 파일을 **삭제하지 말고** `Superseded by ADR-XXXX`로 상태만 변경.
- **PRD vs ADR**: *무엇/누구를 위해* 는 PRD, *어떻게/왜 그 선택* 은 ADR. 판단 기준은 `.claude/skills/document-first/SKILL.md`.

## 목록 (Index)

| # | 제목 | 상태 | 날짜 |
|---|------|------|------|
| [0001](0001-adopt-documentation-first-harness.md) | 하네스에 문서화 우선(PRD/ADR) 워크플로 도입 | Accepted | 2026-06-13 |
