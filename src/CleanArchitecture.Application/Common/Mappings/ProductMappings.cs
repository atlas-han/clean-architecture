using System;
using System.Linq.Expressions;
using CleanArchitecture.Application.Products.Queries.Dtos;
using CleanArchitecture.Domain.Entities;

namespace CleanArchitecture.Application.Common.Mappings
{
    public static class ProductMappings
    {
        // Kept as an Expression so EF Core projects it in the database query.
        // Price is a value-converted Money column, so Price.Amount maps straight
        // to the stored decimal — same shape AutoMapper's ProjectTo emitted.
        public static readonly Expression<Func<Product, ProductDto>> ToDto = p => new ProductDto
        {
            Id = p.Id,
            Name = p.Name,
            Description = p.Description,
            Price = p.Price.Amount,
            Stock = p.Stock,
            CreatedAt = p.CreatedAt,
            UpdatedAt = p.UpdatedAt
        };
    }
}
