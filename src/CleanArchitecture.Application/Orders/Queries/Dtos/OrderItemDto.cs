using System;

namespace CleanArchitecture.Application.Orders.Queries.Dtos
{
    public record OrderItemDto
    {
        public Guid Id { get; init; }
        public Guid ProductId { get; init; }
        public string ProductName { get; init; } = string.Empty;
        public decimal UnitPrice { get; init; }
        public int Quantity { get; init; }
        public decimal LineTotal { get; init; }
    }
}
