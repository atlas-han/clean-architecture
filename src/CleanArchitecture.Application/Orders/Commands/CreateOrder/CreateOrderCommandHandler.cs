using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CleanArchitecture.Application.Common.Exceptions;
using CleanArchitecture.Application.Common.Interfaces;
using CleanArchitecture.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace CleanArchitecture.Application.Orders.Commands.CreateOrder
{
    public class CreateOrderCommandHandler : IRequestHandler<CreateOrderCommand, Guid>
    {
        private readonly IApplicationDbContext _context;

        public CreateOrderCommandHandler(IApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<Guid> Handle(CreateOrderCommand request, CancellationToken cancellationToken)
        {
            var orderItems = new List<OrderItem>();

            foreach (var line in request.Items)
            {
                var product = await _context.Products
                    .FirstOrDefaultAsync(p => p.Id == line.ProductId, cancellationToken);

                if (product is null)
                    throw new NotFoundException(nameof(Product), line.ProductId);

                orderItems.Add(new OrderItem(product.Id, product.Name, product.Price, line.Quantity));
            }

            var order = new Order(request.CustomerName, orderItems);

            _context.Orders.Add(order);
            await _context.SaveChangesAsync(cancellationToken);

            return order.Id;
        }
    }
}
