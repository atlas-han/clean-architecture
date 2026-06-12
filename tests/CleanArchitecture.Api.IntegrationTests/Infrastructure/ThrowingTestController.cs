using System;
using System.Threading.Tasks;
using Asp.Versioning;
using CleanArchitecture.Api.Middleware;
using CleanArchitecture.Domain.Exceptions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CleanArchitecture.Api.IntegrationTests.Infrastructure
{
    // Version-neutral: this probe lives at /api/_test (no version segment) and must keep
    // resolving once URI versioning is enabled on the real controllers.
    [ApiController]
    [ApiVersionNeutral]
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

        // Honors the §7.4 step-2 deadline token via GetRequestCancellationToken(): a long await
        // the deadline cancels mid-flight, exercising the OperationCanceledException -> 504 mapping.
        [HttpGet("slow")]
        public async Task<IActionResult> Slow()
        {
            await Task.Delay(TimeSpan.FromSeconds(30), HttpContext.GetRequestCancellationToken());
            return Ok();
        }

        // Emits a Set-Cookie response header so a test can verify response-side header
        // masking (§14.6) flows end-to-end through the access-log res_header_* path.
        [HttpGet("set-cookie")]
        public IActionResult SetCookieHeader()
        {
            Response.Headers["Set-Cookie"] = "session=secret-cookie-value; Path=/";
            return Ok();
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
