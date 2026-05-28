using CleanArchitecture.Application.Common.Messaging;
using Microsoft.AspNetCore.Mvc;

namespace CleanArchitecture.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public abstract class ApiControllerBase : ControllerBase
    {
        protected ISender Sender { get; }

        protected ApiControllerBase(ISender sender)
        {
            Sender = sender;
        }
    }
}
