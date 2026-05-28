# Database migrations (Flyway, SQL-first)

이 프로젝트는 스키마 변경에 **Flyway 만** 사용합니다. EF Core Migrations 는 도입하지 않으며 `Database.EnsureCreated` 도 호출하지 않습니다 — 런타임은 사전에 적용된 스키마를 가정합니다.

```
db/
  ├── flyway.conf            ← Flyway 설정 (URL/유저는 env 로 주입)
  ├── migrations/
  │   ├── V1__init_schema.sql
  │   └── V{N}__<설명>.sql   ← SQL 진실의 원천
  └── README.md              ← (이 파일)
docker-compose.yml           ← flyway 서비스 (host MSSQL 1433 으로 연결)
.env (gitignored)            ← MSSQL_SA_PASSWORD 등 (.env.example 참조)
```

## 실행

```bash
cp .env.example .env             # 첫 셋업 시 1 회
scripts/db-migrate.sh            # 미적용 마이그레이션 실행
scripts/db-info.sh               # 상태 확인
scripts/db-validate.sh           # 체크섬 검증
```

기본 동작: Flyway 컨테이너가 `host.docker.internal:1433` 으로 호스트 MSSQL 에 연결 → `dbo` 스키마에 마이그레이션 적용. 자체 추적 테이블은 `dbo.flyway_schema_history`.

## 엔티티 변경 워크플로

1. 새 마이그레이션 파일 `V{N+1}__describe.sql` 작성. **기존 파일은 절대 수정 금지** — Flyway 가 체크섬을 기록하므로 사후 편집은 `validate` 가 깨뜨립니다.
2. Domain 엔티티와 `src/CleanArchitecture.Infrastructure/Persistence/Configurations/*Configuration.cs` 를 새 스키마와 일치하게 수정.
3. `scripts/db-migrate.sh` 로 dev DB 갱신 → 앱 실행 → 동작 확인.

EF Core 가 모델에서 DDL 을 자동 생성해주지 않습니다 — 양쪽 (SQL / C#) 을 손으로 맞추는 것이 SQL-first 의 비용입니다.

## InMemory 폴백과의 관계

`ConnectionStrings:DefaultConnection` 이 빈 문자열이면 Infrastructure 는 자동으로 `UseInMemoryDatabase` 로 폴백합니다. 이는 `Application.UnitTests` / `Api.IntegrationTests` 가 사용하는 경로이며, 이 모드에서는 Flyway 가 필요 없습니다. 실제 MSSQL 을 가리킬 때만 Flyway 가 먼저 적용되어 있어야 합니다.

## 마이그레이션 이름 규칙

- 버전 접두사: `V{숫자}__` (밑줄 두 개) — 예: `V2__add_product_category.sql`
- 설명: 소문자 + 밑줄, 영어 권장 (Flyway 가 파일명을 메타로 저장)
- Repeatable 마이그레이션 (`R__`) 은 이 프로젝트에서 사용하지 않음
