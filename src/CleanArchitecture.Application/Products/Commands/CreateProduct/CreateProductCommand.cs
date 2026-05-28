using System;
using CleanArchitecture.Application.Common.Messaging;

namespace CleanArchitecture.Application.Products.Commands.CreateProduct
{
    public record CreateProductCommand(
        string Name,
        string Description,
        decimal Price,
        int Stock) : IRequest<Guid>;
}
