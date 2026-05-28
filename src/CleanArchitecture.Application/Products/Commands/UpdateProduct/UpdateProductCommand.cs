using System;
using CleanArchitecture.Application.Common.Messaging;

namespace CleanArchitecture.Application.Products.Commands.UpdateProduct
{
    public record UpdateProductCommand(
        Guid Id,
        string Name,
        string Description,
        decimal Price,
        int Stock) : IRequest;
}
