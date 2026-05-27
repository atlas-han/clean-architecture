using System;
using CleanArchitecture.Application.Orders.Queries.Dtos;
using MediatR;

namespace CleanArchitecture.Application.Orders.Queries.GetOrderById
{
    public record GetOrderByIdQuery(Guid Id) : IRequest<OrderDto>;
}
