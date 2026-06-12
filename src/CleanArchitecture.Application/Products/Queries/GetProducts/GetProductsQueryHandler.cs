using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CleanArchitecture.Application.Common.Interfaces;
using CleanArchitecture.Application.Common.Mappings;
using CleanArchitecture.Application.Common.Messaging;
using CleanArchitecture.Application.Common.Models;
using CleanArchitecture.Application.Products.Queries.Dtos;
using Microsoft.EntityFrameworkCore;

namespace CleanArchitecture.Application.Products.Queries.GetProducts
{
    public class GetProductsQueryHandler : IRequestHandler<GetProductsQuery, PagedResult<ProductDto>>
    {
        private readonly IApplicationDbContext _context;

        public GetProductsQueryHandler(IApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<PagedResult<ProductDto>> Handle(GetProductsQuery request, CancellationToken cancellationToken)
        {
            var (page, size) = PageRequest.Normalize(request.Page, request.PageSize);

            var totalCount = await _context.Products.CountAsync(cancellationToken);

            var items = await _context.Products
                .OrderByDescending(p => p.CreatedAt)
                .Skip((page - 1) * size)
                .Take(size)
                .Select(ProductMappings.ToDto)
                .ToListAsync(cancellationToken);

            return PagedResult<ProductDto>.Create(items, totalCount, page, size);
        }
    }
}
