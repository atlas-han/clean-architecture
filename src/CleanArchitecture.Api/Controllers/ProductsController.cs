using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CleanArchitecture.Application.Products.Commands.CreateProduct;
using CleanArchitecture.Application.Products.Commands.DeleteProduct;
using CleanArchitecture.Application.Products.Commands.UpdateProduct;
using CleanArchitecture.Application.Products.Queries.Dtos;
using CleanArchitecture.Application.Products.Queries.GetProductById;
using CleanArchitecture.Application.Products.Queries.GetProducts;
using Microsoft.AspNetCore.Mvc;

namespace CleanArchitecture.Api.Controllers
{
    public class ProductsController : ApiControllerBase
    {
        [HttpGet]
        public async Task<ActionResult<IReadOnlyList<ProductDto>>> GetAll(
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20)
        {
            var products = await Mediator.Send(new GetProductsQuery(page, pageSize));
            return Ok(products);
        }

        [HttpGet("{id:guid}")]
        public async Task<ActionResult<ProductDto>> GetById(Guid id)
        {
            var product = await Mediator.Send(new GetProductByIdQuery(id));
            return Ok(product);
        }

        [HttpPost]
        public async Task<ActionResult<Guid>> Create([FromBody] CreateProductCommand command)
        {
            var id = await Mediator.Send(command);
            return CreatedAtAction(nameof(GetById), new { id }, id);
        }

        [HttpPut("{id:guid}")]
        public async Task<IActionResult> Update(Guid id, [FromBody] UpdateProductCommand command)
        {
            if (id != command.Id)
                return BadRequest("Route id does not match body id.");

            await Mediator.Send(command);
            return NoContent();
        }

        [HttpDelete("{id:guid}")]
        public async Task<IActionResult> Delete(Guid id)
        {
            await Mediator.Send(new DeleteProductCommand(id));
            return NoContent();
        }
    }
}
