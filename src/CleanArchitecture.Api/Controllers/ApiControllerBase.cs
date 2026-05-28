using CleanArchitecture.Application.Common.Messaging;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace CleanArchitecture.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public abstract class ApiControllerBase : ControllerBase
    {
        private ISender? _sender;

        protected ISender Sender =>
            _sender ??= HttpContext.RequestServices.GetRequiredService<ISender>();
    }
}
