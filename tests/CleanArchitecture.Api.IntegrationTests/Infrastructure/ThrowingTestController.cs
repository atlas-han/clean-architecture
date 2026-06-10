using System;
using CleanArchitecture.Domain.Exceptions;
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

        [HttpGet("throw-derived-domain")]
        public IActionResult ThrowDerivedDomain()
        {
            throw new DerivedDomainException("Derived domain rule was violated.");
        }
    }

    public class DerivedDomainException : DomainException
    {
        public DerivedDomainException(string message) : base(message) { }
    }
}
