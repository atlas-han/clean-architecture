using System;
using System.Collections.Generic;
using MediatR;

namespace CleanArchitecture.Application.Orders.Commands.CreateOrder
{
    public record CreateOrderCommand(
        string CustomerName,
        IReadOnlyList<CreateOrderItemDto> Items) : IRequest<Guid>;

    public record CreateOrderItemDto(
        Guid ProductId,
        int Quantity);
}
