using System;
using CleanArchitecture.Application.Common.Messaging;

namespace CleanArchitecture.Application.Orders.Commands.CancelOrder
{
    public record CancelOrderCommand(Guid Id) : IRequest;
}
