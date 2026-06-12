using System;
using System.Collections.Generic;
using CleanArchitecture.Domain.Common;

namespace CleanArchitecture.Domain.Events
{
    // Raised when an order is placed (created). Fields are primitives — Money is flattened to its
    // decimal amount — so this record doubles as the serialized integration-event contract without
    // leaking Domain value objects onto the wire.
    public sealed record OrderPlacedDomainEvent(
        Guid OrderId,
        string CustomerName,
        decimal TotalAmount,
        IReadOnlyList<OrderPlacedLine> Items) : IDomainEvent;

    public sealed record OrderPlacedLine(
        Guid ProductId,
        string ProductName,
        decimal UnitPrice,
        int Quantity);
}
