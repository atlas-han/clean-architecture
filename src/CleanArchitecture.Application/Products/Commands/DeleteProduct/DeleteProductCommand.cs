using System;
using CleanArchitecture.Application.Common.Messaging;

namespace CleanArchitecture.Application.Products.Commands.DeleteProduct
{
    public record DeleteProductCommand(Guid Id) : IRequest;
}
