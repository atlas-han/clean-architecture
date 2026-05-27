using System;
using MediatR;

namespace CleanArchitecture.Application.Products.Commands.DeleteProduct
{
    public record DeleteProductCommand(Guid Id) : IRequest;
}
