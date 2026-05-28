using System;
using System.Threading.Tasks;
using CleanArchitecture.Application.Common.Messaging;
using CleanArchitecture.Application.Common.Models;
using CleanArchitecture.Application.Orders.Commands.CancelOrder;
using CleanArchitecture.Application.Orders.Commands.CreateOrder;
using CleanArchitecture.Application.Orders.Commands.PlaceOrder;
using CleanArchitecture.Application.Orders.Queries.Dtos;
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
        public async Task<ActionResult<PagedResult<OrderDto>>> GetAll(
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20)
        {
            var result = await Sender.Send(new GetOrdersQuery(page, pageSize));
            return Ok(result);
        }

        [HttpGet("{id:guid}")]
        public async Task<ActionResult<OrderDto>> GetById(Guid id)
        {
            var order = await Sender.Send(new GetOrderByIdQuery(id));
            return Ok(order);
        }

        [HttpPost]
        public async Task<ActionResult<OrderDto>> Create([FromBody] CreateOrderCommand command)
        {
            var id = await Sender.Send(command);
            var dto = await Sender.Send(new GetOrderByIdQuery(id));
            return CreatedAtAction(nameof(GetById), new { id }, dto);
        }

        [HttpPost("place")]
        public async Task<ActionResult<OrderDto>> Place([FromBody] PlaceOrderCommand command)
        {
            var id = await Sender.Send(command);
            var dto = await Sender.Send(new GetOrderByIdQuery(id));
            return CreatedAtAction(nameof(GetById), new { id }, dto);
        }

        [HttpPost("{id:guid}/cancel")]
        public async Task<IActionResult> Cancel(Guid id)
        {
            await Sender.Send(new CancelOrderCommand(id));
            return NoContent();
        }
    }
}
