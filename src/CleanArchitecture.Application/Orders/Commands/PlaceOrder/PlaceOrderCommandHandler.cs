using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CleanArchitecture.Application.Common.Exceptions;
using CleanArchitecture.Application.Common.Interfaces;
using CleanArchitecture.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace CleanArchitecture.Application.Orders.Commands.PlaceOrder
{
    public class PlaceOrderCommandHandler : IRequestHandler<PlaceOrderCommand, Guid>
    {
        private readonly IApplicationDbContext _context;

        public PlaceOrderCommandHandler(IApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<Guid> Handle(PlaceOrderCommand request, CancellationToken cancellationToken)
        {
            await using var transaction = await _context.BeginTransactionAsync(cancellationToken);

            var orderItems = new List<OrderItem>();

            foreach (var line in request.Items)
            {
                var product = await _context.Products
                    .FirstOrDefaultAsync(p => p.Id == line.ProductId, cancellationToken);

                if (product is null)
                    throw new NotFoundException(nameof(Product), line.ProductId);

                product.DecreaseStock(line.Quantity);
                orderItems.Add(new OrderItem(product.Id, product.Name, product.Price, line.Quantity));
            }

            // First write: persist the stock decrements on the Products table.
            await _context.SaveChangesAsync(cancellationToken);

            // Second write: persist the new order on the Orders table.
            var order = new Order(request.CustomerName, orderItems);
            _context.Orders.Add(order);
            await _context.SaveChangesAsync(cancellationToken);

            // Both writes commit atomically; an exception before this disposes the
            // transaction and rolls both tables back.
            await transaction.CommitAsync(cancellationToken);

            return order.Id;
        }
    }
}
