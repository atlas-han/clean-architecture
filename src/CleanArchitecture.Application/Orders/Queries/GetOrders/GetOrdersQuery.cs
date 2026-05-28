using CleanArchitecture.Application.Common.Messaging;
using CleanArchitecture.Application.Common.Models;
using CleanArchitecture.Application.Orders.Queries.Dtos;

namespace CleanArchitecture.Application.Orders.Queries.GetOrders
{
    public record GetOrdersQuery(int Page = 1, int PageSize = 20) : IRequest<PagedResult<OrderDto>>;
}
