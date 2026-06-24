using System.Threading;
using System.Threading.Tasks;
using CleanArchitecture.Application.Common.Extensions;
using CleanArchitecture.Application.Common.Interfaces;
using CleanArchitecture.Application.Common.Messaging;

namespace CleanArchitecture.Application.Orders.Commands.CancelOrder
{
    public class CancelOrderCommandHandler : IRequestHandler<CancelOrderCommand>
    {
        private readonly IApplicationDbContext _context;

        public CancelOrderCommandHandler(IApplicationDbContext context)
        {
            _context = context;
        }

        public async Task Handle(CancelOrderCommand request, CancellationToken cancellationToken)
        {
            var order = await _context.Orders.FindOrThrowAsync(request.Id, cancellationToken);

            order.Cancel();

            await _context.SaveChangesAsync(cancellationToken);
        }
    }
}
