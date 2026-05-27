using System;
using MediatR;

namespace CleanArchitecture.Application.Orders.Commands.CancelOrder
{
    public record CancelOrderCommand(Guid Id) : IRequest;
}
