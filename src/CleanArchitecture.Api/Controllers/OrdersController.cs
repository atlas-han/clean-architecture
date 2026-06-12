using System;
using System.Threading.Tasks;
using CleanArchitecture.Api.Common;
using CleanArchitecture.Api.Common.Responses;
using CleanArchitecture.Application.Common.Messaging;
using CleanArchitecture.Application.Orders.Commands.CancelOrder;
using CleanArchitecture.Application.Orders.Commands.ConfirmOrder;
using CleanArchitecture.Application.Orders.Commands.CreateOrder;
using CleanArchitecture.Application.Orders.Commands.PlaceOrder;
using CleanArchitecture.Application.Orders.Queries.GetOrderById;
using CleanArchitecture.Application.Orders.Queries.GetOrders;
using Microsoft.AspNetCore.Mvc;

namespace CleanArchitecture.Api.Controllers
{
    public class OrdersController : ApiControllerBase
    {
        public OrdersController(ISender sender) : base(sender)
        {
        }

        [HttpGet]
        public async Task<IActionResult> GetAll(
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20)
        {
            var result = await Sender.Send(new GetOrdersQuery(page, pageSize));
            // List response: Data is the array, pagination goes in Meta (§4.2).
            var meta = new PaginationMeta(result.Page, result.PageSize, result.TotalCount, result.TotalPages);
            return Ok(ApiResult.Success(HttpContext, result.Items, meta));
        }

        [HttpGet("{id:guid}")]
        public async Task<IActionResult> GetById(Guid id)
        {
            var order = await Sender.Send(new GetOrderByIdQuery(id));
            return Ok(ApiResult.Success(HttpContext, order));
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] CreateOrderCommand command)
        {
            var id = await Sender.Send(command);
            var dto = await Sender.Send(new GetOrderByIdQuery(id));
            return CreatedAtAction(nameof(GetById), new { id }, ApiResult.Success(HttpContext, dto));
        }

        [HttpPost("place")]
        public async Task<IActionResult> Place([FromBody] PlaceOrderCommand command)
        {
            var id = await Sender.Send(command);
            var dto = await Sender.Send(new GetOrderByIdQuery(id));
            return CreatedAtAction(nameof(GetById), new { id }, ApiResult.Success(HttpContext, dto));
        }

        [HttpPost("{id:guid}/cancel")]
        public async Task<IActionResult> Cancel(Guid id)
        {
            await Sender.Send(new CancelOrderCommand(id));
            return NoContent();
        }

        [HttpPost("{id:guid}/confirm")]
        public async Task<IActionResult> Confirm(Guid id)
        {
            await Sender.Send(new ConfirmOrderCommand(id));
            return NoContent();
        }
    }
}
