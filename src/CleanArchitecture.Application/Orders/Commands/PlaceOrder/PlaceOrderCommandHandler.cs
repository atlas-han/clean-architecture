using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CleanArchitecture.Application.Common.Exceptions;
using CleanArchitecture.Application.Common.Interfaces;
using CleanArchitecture.Domain.Entities;
using CleanArchitecture.Application.Common.Messaging;
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

        // Intentional demo of the "multiple SaveChanges + explicit transaction" pattern,
        // kept as a counterpart to CreateOrderCommandHandler's single-SaveChanges path.
        // Functionally a single SaveChangesAsync would persist the stock decrements and
        // the new Order atomically via EF Core's implicit transaction, so the explicit
        // BeginTransactionAsync below is not strictly required for the current logic.
        // It is preserved to illustrate how to wrap several SaveChanges calls in one
        // atomic unit — useful when work needs to happen between writes (e.g. reading
        // a generated key, calling another aggregate, publishing an event) while still
        // rolling back every table together on failure.
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
