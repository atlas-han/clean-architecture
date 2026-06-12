using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CleanArchitecture.Application.Common.Interfaces;
using CleanArchitecture.Application.Common.Mappings;
using CleanArchitecture.Application.Common.Messaging;
using CleanArchitecture.Application.Common.Models;
using CleanArchitecture.Application.Orders.Queries.Dtos;
using Microsoft.EntityFrameworkCore;

namespace CleanArchitecture.Application.Orders.Queries.GetOrders
{
    public class GetOrdersQueryHandler : IRequestHandler<GetOrdersQuery, PagedResult<OrderDto>>
    {
        private readonly IApplicationDbContext _context;

        public GetOrdersQueryHandler(IApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<PagedResult<OrderDto>> Handle(GetOrdersQuery request, CancellationToken cancellationToken)
        {
            var (page, size) = PageRequest.Normalize(request.Page, request.PageSize);

            var totalCount = await _context.Orders.CountAsync(cancellationToken);

            var orders = await _context.Orders
                .Include(o => o.Items)
                .AsNoTracking()
                .OrderByDescending(o => o.CreatedAt)
                .Skip((page - 1) * size)
                .Take(size)
                .ToListAsync(cancellationToken);

            var items = orders.Select(OrderMappings.ToDto).ToList();
            return PagedResult<OrderDto>.Create(items, totalCount, page, size);
        }
    }
}
