using System;
using CleanArchitecture.Domain.Exceptions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

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

        [HttpGet("throw-concurrency")]
        public IActionResult ThrowConcurrency()
        {
            throw new DbUpdateConcurrencyException("simulated concurrency conflict");
        }
    }

    public class DerivedDomainException : DomainException
    {
        public DerivedDomainException(string message) : base(message) { }
    }
}
