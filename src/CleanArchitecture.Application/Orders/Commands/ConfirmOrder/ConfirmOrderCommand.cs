using System;
using CleanArchitecture.Application.Common.Messaging;

namespace CleanArchitecture.Application.Orders.Commands.ConfirmOrder
{
    public record ConfirmOrderCommand(Guid Id) : IRequest;
}
