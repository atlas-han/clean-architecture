using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AutoMapper;
using CleanArchitecture.Application.Common.Interfaces;
using CleanArchitecture.Application.Orders.Queries.Dtos;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace CleanArchitecture.Application.Orders.Queries.GetOrders
{
    public class GetOrdersQueryHandler : IRequestHandler<GetOrdersQuery, IReadOnlyList<OrderDto>>
    {
        private readonly IApplicationDbContext _context;
        private readonly IMapper _mapper;

        public GetOrdersQueryHandler(IApplicationDbContext context, IMapper mapper)
        {
            _context = context;
            _mapper = mapper;
        }

        public async Task<IReadOnlyList<OrderDto>> Handle(GetOrdersQuery request, CancellationToken cancellationToken)
        {
            var page = Math.Max(1, request.Page);
            var size = Math.Clamp(request.PageSize, 1, 100);

            var orders = await _context.Orders
                .Include(o => o.Items)
                .AsNoTracking()
                .OrderByDescending(o => o.CreatedAt)
                .Skip((page - 1) * size)
                .Take(size)
                .ToListAsync(cancellationToken);

            return _mapper.Map<IReadOnlyList<OrderDto>>(orders);
        }
    }
}
