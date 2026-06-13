# Kafka 이벤트 발행 — 안정성 & 성능 (조사 + 개선)

주문 이벤트 발행은 누락·지연이 곧 손실로 이어지는 핵심 경로다. 이 문서는 현재 구조를 정리하고,
**(1) 정확히 한번(exactly-once)** 과 **(2) 초당 3000건 처리량** 두 목표에 대한 갭과 이번 변경에서
적용한 개선, 그리고 남은 후속 과제를 기록한다.

---

## 1. 현재 구조 (transactional outbox → Kafka)

```
요청 경로:  Order/Product.RaiseDomainEvent(...)
            → SaveChangesAsync()
              → ConvertDomainEventsToOutboxInterceptor  (같은 트랜잭션에 OutboxMessage INSERT)  ← 원자성
            → 즉시 반환 (브로커를 기다리지 않음)

드레인 경로: OutboxProducerWorker (단일 백그라운드 워커, 폴링)
            → 미처리 행 조회 (ProcessedOnUtc IS NULL AND DeadLetteredOnUtc IS NULL,
                              ORDER BY OccurredOnUtc, Id, TAKE BatchSize)
            → IEventPublisher 로 발행
            → 성공: ProcessedOnUtc 스탬프 / 실패: Attempts++, Error 기록 (MaxRetries 초과 시 dead-letter)
```

| 요소 | 위치 |
|------|------|
| 도메인 이벤트 → outbox 변환 | `Infrastructure/Outbox/ConvertDomainEventsToOutboxInterceptor.cs` |
| outbox 테이블/엔티티 | `Infrastructure/Outbox/OutboxMessage.cs`, `db/migrations/V2,V3` |
| 드레인 워커 | `Infrastructure/BackgroundServices/OutboxProducerWorker.cs` |
| Kafka 발행 | `Infrastructure/Messaging/KafkaEventPublisher.cs` |
| 전송 seam | `Infrastructure/Messaging/IEventPublisher.cs` |
| 메시지 키 | `AggregateId` (aggregate 별 파티션 고정 → aggregate 내 순서 보존) |
| 멱등 키 | `OutboxMessage.Id` (UUID v7), Kafka 헤더 `IdempotencyKey` 로 전달 |

원자성·내구성·dead-letter·graceful shutdown·관측 로그는 이미 견고하게 구현되어 있다.

---

## 2. 목표 1 — 정확히 한번 (exactly-once)

### 현재 보장: at-least-once

발행 후 `ProcessedOnUtc` 를 커밋하기 **전에** 워커가 죽으면, 그 행은 다음 틱에 **재발행**된다.
`EnableIdempotence=true` 는 *같은 producer 세션의 SDK 재시도*만 브로커에서 dedupe 하므로,
워커 재시작을 가로지르는 outbox 재생 중복은 막지 못한다. 즉 발행 측만으로는 본질적으로
exactly-once 를 만들 수 없다 (DB 커밋과 Kafka produce 가 하나의 원자 단위가 아니기 때문).

### 발행 측에서 한 것 (이번 변경)

- **Producer 하드닝** (`KafkaEventPublisher`): `Acks.All` + `EnableIdempotence=true` 에 더해
  `MaxInFlight=5`(멱등 producer 의 순서 보장 상한을 호출부에 명시), `MessageSendMaxRetries=10`
  (멱등 producer 라 재시도가 중복·재배열을 만들지 않음) 를 **명시적으로** 설정.
- **순서 보존 파이프라이닝**: 배치를 drain 순서(`OccurredOnUtc, Id`)대로 `Produce` 하므로,
  멱등 producer 가 파티션별 순서를 유지한다 → aggregate 내 이벤트 순서 불변식 유지.

### 컨슈머 dedup 계약 (end-to-end exactly-once 의 마지막 조각) — **소비자 필수 구현**

이 레포에는 컨슈머가 없다. end-to-end "정확히 한번 효과"는 **멱등 컨슈머**로 완성된다.
다운스트림 컨슈머는 다음 계약을 반드시 지켜야 한다:

1. 토픽 `clean_architecture_events` 구독.
2. **`IdempotencyKey` 헤더(= `OutboxMessage.Id`, UUID v7)로 중복을 제거한다.** 이미 처리한 키면
   메시지를 스킵(또는 부작용 없이 재적용)한다. 처리-완료 키 저장소(예: Redis SETNX / DB unique 제약)에
   처리 완료를 기록하고, 그 기록과 비즈니스 효과를 **하나의 트랜잭션**으로 커밋한다.
3. `MessageType` 헤더로 이벤트 타입을 판별하고, value(JSON)를 해당 타입으로 역직렬화한다.
4. 같은 aggregate 의 이벤트는 같은 파티션(키 = `AggregateId`)으로 들어오므로 파티션 내 순서대로 처리.

> 요약: **발행 = at-least-once + 멱등 키 보장**, **소비 = `IdempotencyKey` dedup** ⇒ 실질적 exactly-once.
> 멱등 키는 이미 발행 측에서 안정적으로 부여되므로, 소비 측 dedup 만 구현하면 된다.

---

## 3. 목표 2 — 초당 3000건 처리량

### 병목 (이전)

- 워커가 배치 내 메시지를 **순차로 `await ProduceAsync`** → 매 메시지마다 브로커 왕복에 직렬화 (latency-bound).
- 기본 폴링 5초 / 배치 100 → 기본 처리량 ≈ **20 events/sec** (목표의 1/150).
- producer 배칭 미설정(linger/compression 기본).

### 적용한 개선

1. **배치 파이프라이닝** (핵심): `IEventPublisher.PublishBatchAsync` 추가.
   - 인터페이스 **default method** = `PublishAsync` 순차 폴백(기존 구현체·테스트 더블 무변경, 동작 보존).
   - `KafkaEventPublisher` 는 이를 override: 배치 전체를 `Produce`(논블로킹)로 enqueue 후
     **단 한 번의 `Flush`** 로 모든 delivery report 를 함께 대기. 처리량이 per-message latency 가 아니라
     브로커/네트워크 대역폭에 의해 결정된다.
   - delivery report 가 슬롯별로 성공/실패를 기록 → **부분 실패가 배치 전체를 죽이지 않음**
     (행 단위 dead-letter 의미 보존).
2. **Producer 배칭**: `LingerMs=5` + `CompressionType.Lz4` → 여러 produce 가 한 브로커 요청으로 병합 + 전송 바이트 축소.
3. **처리량 지향 기본값**: `Outbox:PollInterval` 1s, `Outbox:BatchSize` 500 (기본 ≈ 500 events/sec).

### 3000 events/sec 도달 설정

드레인 상한 ≈ `BatchSize / PollInterval`. 파이프라인 발행이라 1000건 배치도 1초 안에 충분히 빠진다.

| 설정 | 상한 |
|------|------|
| `Outbox:BatchSize=3000`, `Outbox:PollInterval=00:00:01` | 3000/s |
| `Outbox:BatchSize=1000`, `Outbox:PollInterval=00:00:00.333` | ~3000/s (낮은 지연) |
| `Outbox:BatchSize=2000`, `Outbox:PollInterval=00:00:00.5` | 4000/s (여유) |

> **검증 한계**: 이 환경에는 실 Kafka 브로커가 없고 테스트는 가짜 publisher 를 쓴다. 따라서
> `dotnet test` 로 3000/s 를 **실측하지 않는다**. 본 변경의 검증은 (a) 파이프라인 메커니즘의 정합성
> (배치/부분실패/순서) 단위·통합 테스트, (b) 설계상 상한 = BatchSize/PollInterval, (c) 기존
> BenchmarkDotNet 하니스로 한정된다. 실측은 스테이징에서 실 브로커 + `kafka-producer-perf-test`
> 또는 부하 스크립트로 별도 수행해야 한다 (아래 후속 과제).

---

## 4. 설정 레퍼런스

```jsonc
{
  "Kafka": {
    "BootstrapServers": "broker:9092",     // 운영 필수 (Dev/Local 외에서 미설정 시 fail-fast)
    "Topic": "clean_architecture_events"
  },
  "Outbox": {
    "PollInterval": "00:00:01",            // 기본 1s
    "BatchSize": 500,                      // 기본 500 → 3000/s 목표 시 3000(@1s) 또는 1000(@0.333s)
    "MaxRetries": 5,                       // 초과 시 dead-letter
    "ShutdownTimeoutSeconds": 30           // Worker 전용
  }
}
```

`KafkaEventPublisher` producer 고정값: `Acks=All`, `EnableIdempotence=true`, `MaxInFlight=5`,
`MessageSendMaxRetries=10`, `LingerMs=5`, `CompressionType=Lz4`, flush timeout 30s.

---

## 5. 후속 과제 (이번 범위 밖, 권장 순)

1. **연속 드레인(drain-until-empty)**: 한 틱에서 가득 찬 배치를 비우면 다음 틱을 기다리지 않고
   즉시 재드레인 → 처리량을 폴링 간격에서 분리. 부하 급증 시 지연 최소화.
2. **파티션 샤딩 병렬 워커**: 단일 워커 대신 `AggregateId` 해시로 워커를 샤딩하면 수평 확장.
   (단일 워커 = 이중발행 방지 불변식이므로, 샤딩은 행 클레임/리스 메커니즘이 전제.)
3. **컨슈머 + dedup 저장소 구현**: §2 의 멱등 컨슈머를 실제 서비스로. (별도 서비스 범위)
4. **실 브로커 부하 검증**: 스테이징에서 3000/s 실측, p99 발행 지연 SLO 수립.
5. **processed 행 아카이빙**: 처리 완료 행 무한 증가 방지 (파티셔닝/주기적 삭제).
