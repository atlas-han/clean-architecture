using System;
using Microsoft.AspNetCore.Mvc;

namespace CleanArchitecture.Api.IntegrationTests.Infrastructure
{
    [ApiController]
    [Route("api/_test")]
    public class ThrowingTestController : ControllerBase
    {
        [HttpGet("throw")]
        public IActionResult Throw()
        {
            throw new InvalidOperationException("internal-secret-do-not-leak");
        }
    }
}
