using System;
using CleanArchitecture.Application.Products.Queries.Dtos;
using MediatR;

namespace CleanArchitecture.Application.Products.Queries.GetProductById
{
    public record GetProductByIdQuery(Guid Id) : IRequest<ProductDto>;
}
