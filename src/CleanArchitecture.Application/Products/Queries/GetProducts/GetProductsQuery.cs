using System.Collections.Generic;
using CleanArchitecture.Application.Products.Queries.Dtos;
using CleanArchitecture.Application.Common.Messaging;

namespace CleanArchitecture.Application.Products.Queries.GetProducts
{
    public record GetProductsQuery(int Page = 1, int PageSize = 20) : IRequest<IReadOnlyList<ProductDto>>;
}
