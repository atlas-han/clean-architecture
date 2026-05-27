using System.Threading;
using System.Threading.Tasks;
using CleanArchitecture.Application.Common.Exceptions;
using CleanArchitecture.Application.Common.Interfaces;
using CleanArchitecture.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

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
            var order = await _context.Orders
                .FirstOrDefaultAsync(o => o.Id == request.Id, cancellationToken);

            if (order is null)
                throw new NotFoundException(nameof(Order), request.Id);

            order.Cancel();

            await _context.SaveChangesAsync(cancellationToken);
        }
    }
}
