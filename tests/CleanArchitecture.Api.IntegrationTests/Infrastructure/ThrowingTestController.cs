using System;
using CleanArchitecture.Api.Middleware;
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

        // Echoes the deadline DeadlinePropagationMiddleware stashed for a live budget, so a test
        // can verify the §7.4 step 2/3 extension point (HttpContext.Items) is actually populated.
        [HttpGet("deadline-echo")]
        public IActionResult DeadlineEcho()
        {
            long? stored = HttpContext.Items.TryGetValue(DeadlinePropagationMiddleware.DeadlineItemKey, out var value)
                && value is DateTimeOffset deadline
                ? deadline.ToUnixTimeMilliseconds()
                : (long?)null;
            return Ok(new { stored });
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
