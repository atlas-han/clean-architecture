using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace CleanArchitecture.Api.Middleware
{
    // Attaches the common security response headers to every response per API design guide §9.1.
    // Registered at the front of the pipeline so even short-circuited responses (maintenance 503,
    // deadline 504) carry the headers. Headers are set before calling _next — while the response
    // body is still unsent — so they are present regardless of how the downstream pipeline replies.
    // HSTS (Strict-Transport-Security) is handled separately by UseHsts() in Program.cs, since it
    // is only meaningful over HTTPS and must be excluded in development.
    public class SecurityHeadersMiddleware
    {
        private readonly RequestDelegate _next;

        public SecurityHeadersMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public Task InvokeAsync(HttpContext context)
        {
            var headers = context.Response.Headers;

            // Block MIME sniffing so the browser honors the declared Content-Type.
            headers["X-Content-Type-Options"] = "nosniff";
            // Clickjacking defense for legacy browsers (modern ones use CSP frame-ancestors).
            headers["X-Frame-Options"] = "SAMEORIGIN";
            // Legacy reflected-XSS filter; superseded by CSP but kept for older clients.
            headers["X-XSS-Protection"] = "1; mode=block";
            // Primary defense on modern browsers — restricts resource origins and framing.
            headers["Content-Security-Policy"] = "default-src 'self'; frame-ancestors 'self'";

            return _next(context);
        }
    }
}
