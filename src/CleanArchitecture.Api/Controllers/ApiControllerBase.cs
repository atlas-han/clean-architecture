using Asp.Versioning;
using CleanArchitecture.Application.Common.Messaging;
using Microsoft.AspNetCore.Mvc;

namespace CleanArchitecture.Api.Controllers
{
    // URI versioning per API design guide §2.1/§5.2: every resource is exposed under
    // /api/v{version}/{resource}. v1.0 is the default (Program.cs AssumeDefaultVersionWhenUnspecified),
    // so a missing version segment or X-Api-Version header resolves to v1. Derived controllers
    // (Orders, Products) inherit this route and version.
    [ApiController]
    [ApiVersion("1.0")]
    [Route("api/v{version:apiVersion}/[controller]")]
    public abstract class ApiControllerBase : ControllerBase
    {
        protected ISender Sender { get; }

        protected ApiControllerBase(ISender sender)
        {
            Sender = sender;
        }
    }
}
