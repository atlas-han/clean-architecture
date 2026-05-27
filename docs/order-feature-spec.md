# Order Feature — 구현 스펙

## 1. 개요

Product 에 이어 두 번째 도메인 애그리거트로 **Order** 를 추가합니다. Order 는 한 명의 고객이 한 번에 주문한 여러 상품 라인을 묶는 작은 애그리거트이며, 다음 두 가지 핵심 유즈케이스를 지원합니다.

- **주문 생성** — 고객 이름 + 상품 라인(상품 Id, 수량)으로 주문을 생성. 상품의 이름/단가는 주문 시점의 값으로 **스냅샷** 됩니다 (이후 Product 가격이 바뀌어도 Order 의 단가는 불변).
- **주문 취소** — Pending 상태의 주문을 Cancelled 로 전이.

조회는 단건 조회와 페이지 리스트 조회 두 가지를 제공합니다.

## 2. 계층 매핑

| 계층 | 추가/변경 파일 |
|------|----------------|
| Domain | `Entities/Order.cs`, `Entities/OrderItem.cs`, `Enums/OrderStatus.cs` |
| Application | `Common/Interfaces/IApplicationDbContext.cs` (확장), `Common/Mappings/MappingProfile.cs` (확장), `Orders/Commands/{CreateOrder, CancelOrder}/*`, `Orders/Queries/{GetOrderById, GetOrders}/*`, `Orders/Queries/Dtos/{OrderDto, OrderItemDto}.cs` |
| Infrastructure | `Persistence/ApplicationDbContext.cs` (확장), `Persistence/Configurations/{OrderConfiguration, OrderItemConfiguration}.cs` |
| Api | `Controllers/OrdersController.cs` |
| Tests | `Domain.UnitTests/Entities/OrderTests.cs`, `Application.UnitTests/Orders/**`, `Application.UnitTests/TestDoubles/TestDbContext.cs` (확장), `Api.IntegrationTests/OrdersControllerTests.cs` |

기존 Clean Architecture 의존 규칙은 그대로 유지됩니다 — Domain 은 외부 의존 0, Application 은 Domain 만 참조, Infrastructure 는 Application 을 참조.

## 3. 도메인 모델

### 3.1 `Order` (애그리거트 루트)

| 속성 | 타입 | 비고 |
|------|------|------|
| `Id` | `Guid` | `BaseEntity` 에서 상속 |
| `CustomerName` | `string` | 1~200 자, 공백 금지 |
| `Status` | `OrderStatus` | `Pending` (default) / `Confirmed` / `Cancelled` |
| `Items` | `IReadOnlyList<OrderItem>` | 백킹 필드 `_items` |
| `TotalAmount` | `decimal` (계산) | `Σ Items.LineTotal` — EF 컬럼으로 매핑되지 않음 (`Ignore`) |
| `CreatedAt`, `UpdatedAt` | `DateTime?` | `BaseEntity` 에서 상속 |

#### 도메인 메서드

- `Order(string customerName, IEnumerable<OrderItem> items)` — 생성자. 비어있는 이름 / 너무 긴 이름 / 0 개 아이템 / null items 는 `DomainException`.
- `AddItem(OrderItem item)` — Pending 이 아닌 상태에서 호출하면 `DomainException`. item 의 `OrderId` 를 자신의 `Id` 로 attach.
- `Cancel()` — 이미 Cancelled 이면 `DomainException`. 그 외 상태는 모두 Cancelled 로 전이 가능.
- `Confirm()` — Pending 일 때만 Confirmed 로 전이. 그 외에는 `DomainException`.

### 3.2 `OrderItem` (애그리거트 내부 엔티티)

| 속성 | 타입 | 비고 |
|------|------|------|
| `Id` | `Guid` | `BaseEntity` |
| `OrderId` | `Guid` | FK |
| `ProductId` | `Guid` | 원 Product 의 Id 스냅샷 |
| `ProductName` | `string` | 주문 시점 이름 |
| `UnitPrice` | `decimal` | 주문 시점 단가 |
| `Quantity` | `int` | 양수 |
| `LineTotal` | `decimal` (계산) | `UnitPrice * Quantity` |

생성자 검증: `productId` 공백 금지, `productName` 비어있음 금지, `unitPrice ≥ 0`, `quantity > 0`. 모두 `DomainException`.

### 3.3 `OrderStatus`

```csharp
public enum OrderStatus { Pending = 0, Confirmed = 1, Cancelled = 2 }
```

## 4. CQRS 슬라이스

### 4.1 `CreateOrder`

- **Command** — `record CreateOrderCommand(string CustomerName, IReadOnlyList<CreateOrderItemDto> Items) : IRequest<Guid>` (`CreateOrderItemDto(Guid ProductId, int Quantity)`).
- **Handler** — 각 라인의 `ProductId` 로 Product 를 조회 → 없으면 `NotFoundException` (404). 있으면 Product 의 현재 `Name`/`Price` 를 스냅샷한 `OrderItem` 을 만들어 `Order` 를 생성하고 저장. 반환값은 새 Order Id.
- **Validator** — `CustomerName` 필수 / 200자 이하; `Items` 비어있지 않음; 각 아이템의 `ProductId` 비어있지 않음, `Quantity > 0`.

### 4.2 `CancelOrder`

- **Command** — `record CancelOrderCommand(Guid Id) : IRequest`.
- **Handler** — Order 조회, 없으면 `NotFoundException`. `order.Cancel()` 호출 후 저장. 이미 Cancelled 이면 도메인 단에서 `DomainException` → 400 매핑.
- **Validator** — `Id` 비어있지 않음.

### 4.3 `GetOrderById`

- **Query** — `record GetOrderByIdQuery(Guid Id) : IRequest<OrderDto>`.
- **Handler** — `Include(Items)` + `AsNoTracking` 으로 단건 조회. 없으면 `NotFoundException`. AutoMapper 로 `OrderDto` 로 매핑.

### 4.4 `GetOrders`

- **Query** — `record GetOrdersQuery(int Page = 1, int PageSize = 20) : IRequest<IReadOnlyList<OrderDto>>`.
- **Handler** — `CreatedAt` 내림차순, Page/PageSize 클램프 (1~100), `Include(Items)` 후 매핑.

### 4.5 DTOs

```csharp
public record OrderDto
{
    public Guid Id { get; init; }
    public string CustomerName { get; init; } = string.Empty;
    public OrderStatus Status { get; init; }
    public decimal TotalAmount { get; init; }      // Order.TotalAmount 매핑
    public IReadOnlyList<OrderItemDto> Items { get; init; } = new List<OrderItemDto>();
    public DateTime CreatedAt { get; init; }
    public DateTime? UpdatedAt { get; init; }
}

public record OrderItemDto
{
    public Guid Id { get; init; }
    public Guid ProductId { get; init; }
    public string ProductName { get; init; } = string.Empty;
    public decimal UnitPrice { get; init; }
    public int Quantity { get; init; }
    public decimal LineTotal { get; init; }
}
```

`MappingProfile` 에는 `CreateMap<Order, OrderDto>()` 와 `CreateMap<OrderItem, OrderItemDto>()` 만 추가 — 모든 매핑이 같은 이름의 속성이므로 explicit `ForMember` 불필요.

## 5. Infrastructure 매핑

### 5.1 `ApplicationDbContext`

```csharp
public DbSet<Order> Orders => Set<Order>();
```

`OrderItem` 는 Order 애그리거트 내부 엔티티이므로 별도 `DbSet` 으로 노출하지 않습니다 — `Include(o => o.Items)` 로만 접근.

### 5.2 `OrderConfiguration`

- PK = `Id`
- `CustomerName` required, max 200
- `Status` 는 `int` 변환
- `TotalAmount` 는 `Ignore`
- `HasMany(o => o.Items).WithOne().HasForeignKey(i => i.OrderId).OnDelete(Cascade)`
- `Items` 네비게이션은 백킹 필드 모드 (`PropertyAccessMode.Field`)

### 5.3 `OrderItemConfiguration`

- PK = `Id`
- `ProductName` required, max 200
- `UnitPrice` precision (18,2)
- `Quantity` required
- `LineTotal` 은 `Ignore`

## 6. API

| 메서드 | 경로 | 본문 | 응답 |
|--------|------|------|------|
| `GET` | `/api/orders?page=1&pageSize=20` | — | `200 OrderDto[]` |
| `GET` | `/api/orders/{id}` | — | `200 OrderDto` / `404` |
| `POST` | `/api/orders` | `CreateOrderCommand` | `201 Guid` (Location: `/api/orders/{id}`) / `400` (validation) / `404` (unknown productId) |
| `POST` | `/api/orders/{id}/cancel` | — | `204` / `404` |

`PUT /orders/{id}` 같은 일반 업데이트 엔드포인트는 의도적으로 두지 않습니다 — Order 의 변경은 도메인 메서드 (Cancel/Confirm) 를 통해서만 발생.

## 7. 예외 매핑

기존 `ApiExceptionFilter` 가 그대로 적용됩니다.

| 예외 | HTTP |
|------|------|
| `Application.Common.Exceptions.ValidationException` | 400 ValidationProblemDetails |
| `Domain.Exceptions.DomainException` | 400 ProblemDetails |
| `Application.Common.Exceptions.NotFoundException` | 404 ProblemDetails |

핸들러 / 컨트롤러에서 `ProblemDetails` 를 직접 만들지 않습니다.

## 8. 테스트 커버리지

### Domain (`tests/Domain.UnitTests/Entities/OrderTests.cs`)

- 생성자: 정상 입력 / 빈 customer / 너무 긴 customer / null items / 빈 items.
- `OrderItem` 생성자: empty productId, zero quantity, negative price, line total 계산.
- 상태 전이: Cancel × 2, Confirm × 2, AddItem to non-pending × 2.

### Application (`tests/Application.UnitTests/Orders/**`)

- `CreateOrderCommandHandlerTests` — 정상 저장 + Id 반환 / 미존재 ProductId → NotFound / 상품 이름·가격 스냅샷 검증.
- `CreateOrderCommandValidatorTests` — 정상 / customer 빈 값 / items 빈 / quantity 0 / 빈 productId / customer 길이 초과.
- `CancelOrderCommandHandlerTests` — 정상 취소 / 미존재 Id → NotFound.
- `CancelOrderCommandValidatorTests` — 정상 / 빈 Id.
- `GetOrderByIdQueryHandlerTests` — 정상 조회 + items + TotalAmount / 미존재.

### Api (`tests/Api.IntegrationTests/OrdersControllerTests.cs`)

- Create → Get 라운드트립 (TotalAmount, items.length 검증)
- Create with invalid payload → 400 + ValidationProblemDetails
- Create with unknown productId → 404
- Get unknown → 404
- Cancel 정상 → 204 + 후속 Get 의 status == 2 (Cancelled)
- Cancel unknown → 404

## 9. 제약 / 비고

- `<LangVersion>9.0</LangVersion>` 고정. `required` / file-scoped namespace / raw string literal 등 C# 10+ 문법 사용 금지.
- Domain 은 NuGet 의존 0 — `FluentValidation`, `MediatR`, `EF Core` 등은 모두 Application/Infrastructure 에서만 참조.
- `IApplicationDbContext` 는 Application 의 경계 — 테스트는 Infrastructure 의 `ApplicationDbContext` 대신 `TestDbContext` (자체 OnModelCreating 으로 Order navigation backing field 설정) 사용.
