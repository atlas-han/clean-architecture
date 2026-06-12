using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using CleanArchitecture.Api.Http;
using CleanArchitecture.Api.Middleware;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace CleanArchitecture.Api.IntegrationTests
{
    // API design guide §7.4 step 3 — re-propagate X-Request-Deadline to downstream HTTP calls.
    public class DeadlinePropagationHandlerTests
    {
        private sealed class CapturingHandler : HttpMessageHandler
        {
            public HttpRequestMessage? LastRequest;

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                LastRequest = request;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
            }
        }

        private static (HttpClient client, CapturingHandler inner) BuildClient(HttpContext? context)
        {
            var accessor = new HttpContextAccessor { HttpContext = context };
            var inner = new CapturingHandler();
            var sut = new DeadlinePropagationHandler(accessor) { InnerHandler = inner };
            return (new HttpClient(sut), inner);
        }

        private static HttpContext ContextWithDeadline(DateTimeOffset deadline)
        {
            var context = new DefaultHttpContext();
            context.Items[DeadlinePropagationMiddleware.DeadlineItemKey] = deadline;
            return context;
        }

        private static bool HasDeadlineHeader(HttpRequestMessage request)
            => request.Headers.Contains(DeadlinePropagationMiddleware.DeadlineHeader);

        [Fact]
        public async Task InjectsAbsoluteDeadline_WhenRequestHasLiveDeadline()
        {
            var deadline = DateTimeOffset.FromUnixTimeMilliseconds(1893456000000); // fixed absolute instant
            var (client, inner) = BuildClient(ContextWithDeadline(deadline));

            await client.GetAsync("http://downstream/resource");

            Assert.True(inner.LastRequest!.Headers.TryGetValues(DeadlinePropagationMiddleware.DeadlineHeader, out var values));
            // Absolute deadline forwarded unchanged so the downstream service shares the same budget.
            Assert.Equal(deadline.ToUnixTimeMilliseconds().ToString(), values!.Single());
        }

        [Fact]
        public async Task DoesNotInject_WhenNoDeadlineInContext()
        {
            var (client, inner) = BuildClient(new DefaultHttpContext());

            await client.GetAsync("http://downstream/resource");

            Assert.False(HasDeadlineHeader(inner.LastRequest!));
        }

        [Fact]
        public async Task DoesNotInject_WhenNoHttpContext()
        {
            var (client, inner) = BuildClient(context: null);

            await client.GetAsync("http://downstream/resource");

            Assert.False(HasDeadlineHeader(inner.LastRequest!));
        }

        [Fact]
        public async Task DoesNotOverride_ExistingDeadlineHeader()
        {
            var (client, inner) = BuildClient(ContextWithDeadline(DateTimeOffset.UtcNow.AddSeconds(5)));

            var request = new HttpRequestMessage(HttpMethod.Get, "http://downstream/resource");
            request.Headers.TryAddWithoutValidation(DeadlinePropagationMiddleware.DeadlineHeader, "111");
            await client.SendAsync(request);

            // Explicit caller intent wins; the handler must not clobber a header that is already set.
            Assert.Equal("111", inner.LastRequest!.Headers.GetValues(DeadlinePropagationMiddleware.DeadlineHeader).Single());
        }
    }
}
