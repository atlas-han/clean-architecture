using System.Threading;
using System.Threading.Tasks;
using CleanArchitecture.Application.Common.Extensions;
using CleanArchitecture.Application.Common.Interfaces;
using CleanArchitecture.Application.Common.Messaging;

namespace CleanArchitecture.Application.Orders.Commands.ConfirmOrder
{
    public class ConfirmOrderCommandHandler : IRequestHandler<ConfirmOrderCommand>
    {
        private readonly IApplicationDbContext _context;

        public ConfirmOrderCommandHandler(IApplicationDbContext context)
        {
            _context = context;
        }

        public async Task Handle(ConfirmOrderCommand request, CancellationToken cancellationToken)
        {
            var order = await _context.Orders.FindOrThrowAsync(request.Id, cancellationToken);

            order.Confirm();

            await _context.SaveChangesAsync(cancellationToken);
        }
    }
}
