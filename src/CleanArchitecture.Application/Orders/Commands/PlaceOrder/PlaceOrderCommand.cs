using System;
using System.Collections.Generic;
using MediatR;

namespace CleanArchitecture.Application.Orders.Commands.PlaceOrder
{
    public record PlaceOrderCommand(
        string CustomerName,
        IReadOnlyList<PlaceOrderItemDto> Items) : IRequest<Guid>;

    public record PlaceOrderItemDto(
        Guid ProductId,
        int Quantity);
}
