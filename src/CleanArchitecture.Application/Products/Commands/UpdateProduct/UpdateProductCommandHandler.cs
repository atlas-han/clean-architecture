using System.Threading;
using System.Threading.Tasks;
using CleanArchitecture.Application.Common.Exceptions;
using CleanArchitecture.Application.Common.Interfaces;
using CleanArchitecture.Domain.Entities;
using CleanArchitecture.Application.Common.Messaging;

namespace CleanArchitecture.Application.Products.Commands.UpdateProduct
{
    public class UpdateProductCommandHandler : IRequestHandler<UpdateProductCommand>
    {
        private readonly IApplicationDbContext _context;

        public UpdateProductCommandHandler(IApplicationDbContext context)
        {
            _context = context;
        }

        public async Task Handle(UpdateProductCommand request, CancellationToken cancellationToken)
        {
            var product = await _context.Products.FindAsync(new object[] { request.Id }, cancellationToken);

            if (product is null)
                throw new NotFoundException(nameof(Product), request.Id);

            product.Rename(request.Name);
            product.ChangeDescription(request.Description);
            product.ChangePrice(request.Price);
            product.AdjustStock(request.Stock);

            await _context.SaveChangesAsync(cancellationToken);
        }
    }
}
