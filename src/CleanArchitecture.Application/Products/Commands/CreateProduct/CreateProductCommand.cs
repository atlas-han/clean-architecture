using System;
using MediatR;

namespace CleanArchitecture.Application.Products.Commands.CreateProduct
{
    public record CreateProductCommand(
        string Name,
        string Description,
        decimal Price,
        int Stock) : IRequest<Guid>;
}
