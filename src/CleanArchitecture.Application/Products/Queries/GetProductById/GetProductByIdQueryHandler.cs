using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CleanArchitecture.Application.Common.Exceptions;
using CleanArchitecture.Application.Common.Interfaces;
using CleanArchitecture.Application.Common.Mappings;
using CleanArchitecture.Application.Products.Queries.Dtos;
using CleanArchitecture.Domain.Entities;
using CleanArchitecture.Application.Common.Messaging;
using Microsoft.EntityFrameworkCore;

namespace CleanArchitecture.Application.Products.Queries.GetProductById
{
    public class GetProductByIdQueryHandler : IRequestHandler<GetProductByIdQuery, ProductDto>
    {
        private readonly IApplicationDbContext _context;

        public GetProductByIdQueryHandler(IApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<ProductDto> Handle(GetProductByIdQuery request, CancellationToken cancellationToken)
        {
            var dto = await _context.Products
                .Where(p => p.Id == request.Id)
                .Select(ProductMappings.ToDto)
                .SingleOrDefaultAsync(cancellationToken);

            if (dto is null)
                throw new NotFoundException(nameof(Product), request.Id);

            return dto;
        }
    }
}
