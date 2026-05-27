using System;
using MediatR;

namespace CleanArchitecture.Application.Products.Commands.UpdateProduct
{
    public record UpdateProductCommand(
        Guid Id,
        string Name,
        string Description,
        decimal Price,
        int Stock) : IRequest;
}
