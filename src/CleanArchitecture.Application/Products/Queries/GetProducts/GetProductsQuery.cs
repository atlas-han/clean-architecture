using System.Collections.Generic;
using CleanArchitecture.Application.Products.Queries.Dtos;
using MediatR;

namespace CleanArchitecture.Application.Products.Queries.GetProducts
{
    public record GetProductsQuery(int Page = 1, int PageSize = 20) : IRequest<IReadOnlyList<ProductDto>>;
}
