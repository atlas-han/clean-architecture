using System;
using CleanArchitecture.Domain.Common;

namespace CleanArchitecture.Domain.Events
{
    // Raised when a product is registered (created). Price is flattened to its decimal amount so
    // the record serializes cleanly as the integration-event contract.
    public sealed record ProductRegisteredDomainEvent(
        Guid ProductId,
        string Name,
        string Description,
        decimal Price,
        int Stock) : IDomainEvent;
}
