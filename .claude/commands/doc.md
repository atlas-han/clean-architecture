---
description: Scaffold a numbered PRD or ADR from the harness template into docs/{prd,adr}/ (the documentation-first entry point).
argument-hint: <adr|prd> <title…>
---

문서화 우선(documentation-first) 진입점. 새 PRD 또는 ADR을 표준 템플릿에서 다음 번호로 생성합니다.

인자: `$ARGUMENTS` (형식: `<adr|prd> <제목…>`, 예: `adr replace polling outbox with CDC`, `prd order bulk import`).

타입이나 제목이 빠졌으면 **하나의** 집중 질문으로 확인한 뒤 진행. PRD/ADR 중 무엇을 쓸지 모호하면 `.claude/skills/document-first/SKILL.md` 의 판단 기준을 적용(신규 기능의 *무엇/누구* → PRD, 기술/구조 결정의 *어떻게/왜* → ADR; 큰 기능은 둘 다).

## 절차

1. **타입 결정** — `adr` 또는 `prd`. 대상 디렉터리: `docs/adr/` 또는 `docs/prd/`.

2. **다음 번호 산출** — 대상 디렉터리의 기존 `NNNN-*.md` 중 최대 번호 + 1, 4자리 zero-pad. 비어 있으면 `0001`.
   ```bash
   ls docs/<type>/ | grep -E '^[0-9]{4}-' | sed -E 's/^([0-9]{4}).*/\1/' | sort -n | tail -1
   ```

3. **slug 생성** — 제목을 소문자·하이픈으로. 예: `replace polling outbox with CDC` → `replace-polling-outbox-with-cdc`. 파일명: `docs/<type>/<NNNN>-<slug>.md`.

4. **템플릿 복사 + 채우기** — `.claude/templates/<type>-template.md` 를 읽어 새 파일로:
   - 제목 줄의 `NNNN` → 실제 번호, `<...제목...>` → 인자 제목.
   - **날짜(Date)** → 오늘 날짜(`YYYY-MM-DD`). 시스템 컨텍스트의 currentDate 사용.
   - 나머지 `<...>` 플레이스홀더는 그대로 두고 사용자/후속 작업이 채우도록 둠. (이 커맨드는 *뼈대*만 만든다 — 내용은 작업 맥락에서 채운다.)

5. **인덱스 갱신** — `docs/<type>/README.md` 의 목록 표에 새 행 추가(번호 링크 / 제목 / 상태 / 날짜).

6. **위치 주의 (중요)** — doc-first-guard는 **코드와 같은 worktree 안**에 문서가 있어야 `src/`·`tests/` 편집 차단을 풉니다.
   - 이미 작업 worktree 안이면 거기에 생성.
   - 아직 worktree 밖이고 곧 코딩을 시작할 거라면, 문서가 작업의 시작이므로 `EnterWorktree(name: feat-<topic>)` 후 worktree 안에 생성.
   - 코딩 없이 문서만 초안 잡는 거라면 현재 위치(main 체크아웃)에 생성해도 됨 — 나중에 작업 worktree로 가져가면 된다.

7. 생성 후 파일 경로를 보고하고, 채워야 할 핵심 섹션(맥락/결정 또는 목표/인수기준)을 한 줄로 안내.

## 제약

- 번호는 단조 증가·재사용 금지. 기존 문서를 덮어쓰지 말 것.
- 템플릿 구조(`## 1. …` 헤더)는 유지. 섹션을 임의로 지우지 말 것.
- 문서 자체는 `.md` 라 어떤 가드에도 막히지 않는다 — 먼저 만들고, 그다음 코드.
