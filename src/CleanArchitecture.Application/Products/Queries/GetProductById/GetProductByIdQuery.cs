using System;
using CleanArchitecture.Application.Products.Queries.Dtos;
using CleanArchitecture.Application.Common.Messaging;

namespace CleanArchitecture.Application.Products.Queries.GetProductById
{
    public record GetProductByIdQuery(Guid Id) : IRequest<ProductDto>;
}
