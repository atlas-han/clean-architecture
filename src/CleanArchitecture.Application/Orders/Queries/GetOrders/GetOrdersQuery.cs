using System.Collections.Generic;
using CleanArchitecture.Application.Orders.Queries.Dtos;
using CleanArchitecture.Application.Common.Messaging;

namespace CleanArchitecture.Application.Orders.Queries.GetOrders
{
    public record GetOrdersQuery(int Page = 1, int PageSize = 20) : IRequest<IReadOnlyList<OrderDto>>;
}
