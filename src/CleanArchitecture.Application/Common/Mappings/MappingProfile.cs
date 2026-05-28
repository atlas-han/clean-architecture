using AutoMapper;
using CleanArchitecture.Application.Orders.Queries.Dtos;
using CleanArchitecture.Application.Products.Queries.Dtos;
using CleanArchitecture.Domain.Entities;
using CleanArchitecture.Domain.ValueObjects;

namespace CleanArchitecture.Application.Common.Mappings
{
    public class MappingProfile : Profile
    {
        public MappingProfile()
        {
            CreateMap<Money, decimal>().ConvertUsing(m => m.Amount);

            CreateMap<Product, ProductDto>();
            CreateMap<Order, OrderDto>();
            CreateMap<OrderItem, OrderItemDto>();
        }
    }
}
