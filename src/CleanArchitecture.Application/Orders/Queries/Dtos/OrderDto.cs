using System;
using System.Collections.Generic;
using CleanArchitecture.Domain.Enums;

namespace CleanArchitecture.Application.Orders.Queries.Dtos
{
    public record OrderDto
    {
        public Guid Id { get; init; }
        public string CustomerName { get; init; } = string.Empty;
        public OrderStatus Status { get; init; }
        public decimal TotalAmount { get; init; }
        public IReadOnlyList<OrderItemDto> Items { get; init; } = new List<OrderItemDto>();
        public DateTime CreatedAt { get; init; }
        public DateTime? UpdatedAt { get; init; }
    }
}
