using System;
using System.Collections.Generic;
using System.Threading.Tasks;
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
        [HttpGet]
        public async Task<ActionResult<IReadOnlyList<OrderDto>>> GetAll(
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20)
        {
            var orders = await Mediator.Send(new GetOrdersQuery(page, pageSize));
            return Ok(orders);
        }

        [HttpGet("{id:guid}")]
        public async Task<ActionResult<OrderDto>> GetById(Guid id)
        {
            var order = await Mediator.Send(new GetOrderByIdQuery(id));
            return Ok(order);
        }

        [HttpPost]
        public async Task<ActionResult<Guid>> Create([FromBody] CreateOrderCommand command)
        {
            var id = await Mediator.Send(command);
            return CreatedAtAction(nameof(GetById), new { id }, id);
        }

        [HttpPost("place")]
        public async Task<ActionResult<Guid>> Place([FromBody] PlaceOrderCommand command)
        {
            var id = await Mediator.Send(command);
            return CreatedAtAction(nameof(GetById), new { id }, id);
        }

        [HttpPost("{id:guid}/cancel")]
        public async Task<IActionResult> Cancel(Guid id)
        {
            await Mediator.Send(new CancelOrderCommand(id));
            return NoContent();
        }
    }
}
