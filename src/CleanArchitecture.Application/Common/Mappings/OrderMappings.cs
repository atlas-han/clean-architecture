using System.Linq;
using CleanArchitecture.Application.Orders.Queries.Dtos;
using CleanArchitecture.Domain.Entities;

namespace CleanArchitecture.Application.Common.Mappings
{
    public static class OrderMappings
    {
        // Orders are materialized (Include + ToList/First) before mapping, so the
        // computed TotalAmount/Items navigation evaluate in memory.
        public static OrderDto ToDto(Order order) => new OrderDto
        {
            Id = order.Id,
            CustomerName = order.CustomerName,
            Status = order.Status,
            TotalAmount = order.TotalAmount.Amount,
            Items = order.Items.Select(i => ToDto(i)).ToList(),
            CreatedAt = order.CreatedAt,
            UpdatedAt = order.UpdatedAt
        };

        public static OrderItemDto ToDto(OrderItem item) => new OrderItemDto
        {
            Id = item.Id,
            ProductId = item.ProductId,
            ProductName = item.ProductName,
            UnitPrice = item.UnitPrice.Amount,
            Quantity = item.Quantity,
            LineTotal = item.LineTotal.Amount
        };
    }
}
