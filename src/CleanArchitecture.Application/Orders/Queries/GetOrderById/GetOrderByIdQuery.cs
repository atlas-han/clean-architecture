using System;
using CleanArchitecture.Application.Orders.Queries.Dtos;
using CleanArchitecture.Application.Common.Messaging;

namespace CleanArchitecture.Application.Orders.Queries.GetOrderById
{
    public record GetOrderByIdQuery(Guid Id) : IRequest<OrderDto>;
}
