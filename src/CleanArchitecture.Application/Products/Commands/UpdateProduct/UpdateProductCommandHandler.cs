using System.Threading;
using System.Threading.Tasks;
using CleanArchitecture.Application.Common.Extensions;
using CleanArchitecture.Application.Common.Interfaces;
using CleanArchitecture.Application.Common.Messaging;
using CleanArchitecture.Domain.ValueObjects;

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
            var product = await _context.Products.FindOrThrowAsync(request.Id, cancellationToken);

            product.Rename(request.Name);
            product.ChangeDescription(request.Description);
            product.ChangePrice(new Money(request.Price));
            product.AdjustStock(request.Stock);

            await _context.SaveChangesAsync(cancellationToken);
        }
    }
}
