using System;
using System.Collections.Generic;
using CleanArchitecture.Application.Common.Messaging;

namespace CleanArchitecture.Application.Orders.Commands.PlaceOrder
{
    public record PlaceOrderCommand(
        string CustomerName,
        IReadOnlyList<PlaceOrderItemDto> Items) : IRequest<Guid>;

    public record PlaceOrderItemDto(
        Guid ProductId,
        int Quantity);
}
