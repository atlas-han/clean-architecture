using CleanArchitecture.Application.Common.Messaging;
using CleanArchitecture.Application.Common.Models;
using CleanArchitecture.Application.Products.Queries.Dtos;

namespace CleanArchitecture.Application.Products.Queries.GetProducts
{
    public record GetProductsQuery(int Page = 1, int PageSize = 20) : IRequest<PagedResult<ProductDto>>;
}
