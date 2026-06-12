using System;
using System.Threading.Tasks;
using CleanArchitecture.Api.Common;
using CleanArchitecture.Api.Common.Responses;
using CleanArchitecture.Application.Common.Messaging;
using CleanArchitecture.Application.Products.Commands.CreateProduct;
using CleanArchitecture.Application.Products.Commands.DeleteProduct;
using CleanArchitecture.Application.Products.Commands.UpdateProduct;
using CleanArchitecture.Application.Products.Queries.GetProductById;
using CleanArchitecture.Application.Products.Queries.GetProducts;
using Microsoft.AspNetCore.Mvc;

namespace CleanArchitecture.Api.Controllers
{
    public class ProductsController : ApiControllerBase
    {
        public ProductsController(ISender sender) : base(sender)
        {
        }

        [HttpGet]
        public async Task<IActionResult> GetAll(
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20)
        {
            var result = await Sender.Send(new GetProductsQuery(page, pageSize));
            // List response: Data is the array, pagination goes in Meta (§4.2).
            var meta = new PaginationMeta(result.Page, result.PageSize, result.TotalCount, result.TotalPages);
            return Ok(ApiResult.Success(HttpContext, result.Items, meta));
        }

        [HttpGet("{id:guid}")]
        public async Task<IActionResult> GetById(Guid id)
        {
            var product = await Sender.Send(new GetProductByIdQuery(id));
            return Ok(ApiResult.Success(HttpContext, product));
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] CreateProductCommand command)
        {
            var id = await Sender.Send(command);
            var dto = await Sender.Send(new GetProductByIdQuery(id));
            return CreatedAtAction(nameof(GetById), new { id }, ApiResult.Success(HttpContext, dto));
        }

        [HttpPut("{id:guid}")]
        public async Task<IActionResult> Update(Guid id, [FromBody] UpdateProductCommand command)
        {
            if (id != command.Id)
            {
                return BadRequest(ApiResult.Error(HttpContext, ErrorCodes.ValidationError,
                    "Route id does not match body id."));
            }

            await Sender.Send(command);
            return NoContent();
        }

        [HttpDelete("{id:guid}")]
        public async Task<IActionResult> Delete(Guid id)
        {
            await Sender.Send(new DeleteProductCommand(id));
            return NoContent();
        }
    }
}
