using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using CleanArchitecture.Api.Common;
using CleanArchitecture.Api.Common.Responses;
using CleanArchitecture.Api.Middleware;
using CleanArchitecture.Application.Common.Exceptions;
using CleanArchitecture.Domain.Exceptions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CleanArchitecture.Api.Filters
{
    // Maps the sanctioned exception families (+ EF concurrency) to the §4.3
    // ErrorResponse envelope using the §6.2 status/code mapping. Handlers never
    // build a response by hand — they throw, and this filter shapes the reply.
    public class ApiExceptionFilter : ExceptionFilterAttribute
    {
        private readonly IDictionary<Type, Action<ExceptionContext>> _handlers;
        private readonly ILogger<ApiExceptionFilter> _logger;

        public ApiExceptionFilter(ILogger<ApiExceptionFilter> logger)
        {
            _logger = logger;
            _handlers = new Dictionary<Type, Action<ExceptionContext>>
            {
                [typeof(ValidationException)] = HandleValidation,
                [typeof(NotFoundException)] = HandleNotFound,
                [typeof(DomainException)] = HandleDomain,
                [typeof(DbUpdateConcurrencyException)] = HandleConcurrency,
                [typeof(OperationCanceledException)] = HandleCanceled,
            };
        }

        public override void OnException(ExceptionContext context)
        {
            HandleException(context);
            base.OnException(context);
        }

        private void HandleException(ExceptionContext context)
        {
            // Walk the type hierarchy so derived exceptions (e.g. a subclass of
            // DomainException) map to the same response as their base type.
            for (var type = context.Exception.GetType(); type != null; type = type.BaseType)
            {
                if (_handlers.TryGetValue(type, out var handler))
                {
                    handler.Invoke(context);
                    return;
                }
            }

            HandleUnknown(context);
        }

        private static void HandleValidation(ExceptionContext context)
        {
            var ex = (ValidationException)context.Exception;

            // Flatten { field -> [messages] } into one { field, message } entry per
            // message, as required by §4.3's details array.
            var details = ex.Errors
                .SelectMany(field => field.Value.Select(message => new FieldError(field.Key, message)))
                .ToList();

            Write(context, StatusCodes.Status400BadRequest, ErrorCodes.ValidationError,
                "One or more validation errors occurred.", details);
        }

        private static void HandleNotFound(ExceptionContext context)
        {
            Write(context, StatusCodes.Status404NotFound, ErrorCodes.NotFound, context.Exception.Message);
        }

        private static void HandleDomain(ExceptionContext context)
        {
            // Business-rule violations map to 422 BUSINESS_RULE_VIOLATION (§6.2).
            Write(context, StatusCodes.Status422UnprocessableEntity, ErrorCodes.BusinessRuleViolation,
                context.Exception.Message);
        }

        private static void HandleConcurrency(ExceptionContext context)
        {
            Write(context, StatusCodes.Status409Conflict, ErrorCodes.Conflict,
                "The resource changed while this request was being processed. Refresh and retry.");
        }

        private void HandleCanceled(ExceptionContext context)
        {
            var ctx = context.HttpContext;

            // A deadline-bounded token (§7.4 step 2) that fired while the client is still
            // connected means the request budget was exhausted mid-flight → 504 DEADLINE_EXCEEDED.
            if (ctx.Items.TryGetValue(DeadlinePropagationMiddleware.DeadlineTokenItemKey, out var value)
                && value is CancellationToken token
                && token.IsCancellationRequested
                && !ctx.RequestAborted.IsCancellationRequested)
            {
                Write(context, StatusCodes.Status504GatewayTimeout, ErrorCodes.DeadlineExceeded,
                    "The request deadline (X-Request-Deadline) was exceeded before processing could complete.");
                return;
            }

            // Genuine client disconnect — this also wins a simultaneous deadline+disconnect (the
            // client is gone either way). Not a server error: swallow quietly with 499 (Client
            // Closed Request) so it isn't logged as an unhandled 500; the response is discarded.
            if (ctx.RequestAborted.IsCancellationRequested)
            {
                context.Result = new StatusCodeResult(499);
                context.ExceptionHandled = true;
                return;
            }

            // Cancellation we didn't initiate and the client didn't cause → treat as unexpected (500).
            HandleUnknown(context);
        }

        private void HandleUnknown(ExceptionContext context)
        {
            _logger.LogError(
                context.Exception,
                "Unhandled exception while processing {path}",
                context.HttpContext.Request.Path);

            // Never leak internal detail (stack trace, DB message) to the client (§4.3).
            // The traceId in the body ties the response to the logged exception.
            Write(context, StatusCodes.Status500InternalServerError, ErrorCodes.InternalError,
                "An unexpected error occurred. Reference traceId for support.");
        }

        private static void Write(ExceptionContext context, int statusCode, string code, string message,
            IReadOnlyList<FieldError>? details = null)
        {
            var body = ApiResult.Error(context.HttpContext, code, message, details);
            context.Result = new ObjectResult(body) { StatusCode = statusCode };
            context.ExceptionHandled = true;
        }
    }
}
