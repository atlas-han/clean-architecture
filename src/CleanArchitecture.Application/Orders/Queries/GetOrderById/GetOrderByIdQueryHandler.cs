using System.Threading;
using System.Threading.Tasks;
using CleanArchitecture.Application.Common.Exceptions;
using CleanArchitecture.Application.Common.Interfaces;
using CleanArchitecture.Application.Common.Mappings;
using CleanArchitecture.Application.Orders.Queries.Dtos;
using CleanArchitecture.Domain.Entities;
using CleanArchitecture.Application.Common.Messaging;
using Microsoft.EntityFrameworkCore;

namespace CleanArchitecture.Application.Orders.Queries.GetOrderById
{
    public class GetOrderByIdQueryHandler : IRequestHandler<GetOrderByIdQuery, OrderDto>
    {
        private readonly IApplicationDbContext _context;

        public GetOrderByIdQueryHandler(IApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<OrderDto> Handle(GetOrderByIdQuery request, CancellationToken cancellationToken)
        {
            var order = await _context.Orders
                .Include(o => o.Items)
                .AsNoTracking()
                .FirstOrDefaultAsync(o => o.Id == request.Id, cancellationToken);

            if (order is null)
                throw new NotFoundException(nameof(Order), request.Id);

            return OrderMappings.ToDto(order);
        }
    }
}
