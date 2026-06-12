using System;
using System.Collections.Generic;
using System.Diagnostics;
using CleanArchitecture.Api.Common;
using CleanArchitecture.Application.Common.Exceptions;
using CleanArchitecture.Domain.Exceptions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CleanArchitecture.Api.Filters
{
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
            var details = new ValidationProblemDetails(ex.Errors)
            {
                Title = "One or more validation errors occurred.",
                Status = StatusCodes.Status400BadRequest,
                Type = ErrorCodes.TypeValidationFailed,
                Detail = "Request contains invalid fields. See 'errors' for details."
            };

            Enrich(details, context, ErrorCodes.ValidationFailed);

            context.Result = new BadRequestObjectResult(details);
            context.ExceptionHandled = true;
        }

        private static void HandleNotFound(ExceptionContext context)
        {
            var details = new ProblemDetails
            {
                Title = "The specified resource was not found.",
                Status = StatusCodes.Status404NotFound,
                Type = ErrorCodes.TypeResourceNotFound,
                Detail = context.Exception.Message
            };

            Enrich(details, context, ErrorCodes.ResourceNotFound);

            context.Result = new NotFoundObjectResult(details);
            context.ExceptionHandled = true;
        }

        private static void HandleDomain(ExceptionContext context)
        {
            var details = new ProblemDetails
            {
                Title = "A domain rule was violated.",
                Status = StatusCodes.Status400BadRequest,
                Type = ErrorCodes.TypeDomainRuleViolated,
                Detail = context.Exception.Message
            };

            Enrich(details, context, ErrorCodes.DomainRuleViolated);

            context.Result = new BadRequestObjectResult(details);
            context.ExceptionHandled = true;
        }

        private static void HandleConcurrency(ExceptionContext context)
        {
            var details = new ProblemDetails
            {
                Title = "The resource was modified by another request.",
                Status = StatusCodes.Status409Conflict,
                Type = ErrorCodes.TypeConcurrencyConflict,
                Detail = "The resource changed while this request was being processed. Refresh and retry."
            };

            Enrich(details, context, ErrorCodes.ConcurrencyConflict);

            context.Result = new ConflictObjectResult(details);
            context.ExceptionHandled = true;
        }

        private void HandleUnknown(ExceptionContext context)
        {
            _logger.LogError(
                context.Exception,
                "Unhandled exception while processing {path}",
                context.HttpContext.Request.Path);

            var details = new ProblemDetails
            {
                Status = StatusCodes.Status500InternalServerError,
                Title = "An error occurred while processing your request.",
                Type = ErrorCodes.TypeInternalError,
                Detail = "An unexpected error occurred. Reference traceId for support."
            };

            Enrich(details, context, ErrorCodes.InternalError);

            context.Result = new ObjectResult(details)
            {
                StatusCode = StatusCodes.Status500InternalServerError
            };
            context.ExceptionHandled = true;
        }

        private static void Enrich(ProblemDetails details, ExceptionContext context, string code)
        {
            details.Instance = context.HttpContext.Request.Path;
            details.Extensions["code"] = code;
            details.Extensions["traceId"] = Activity.Current?.Id ?? context.HttpContext.TraceIdentifier;
            details.Extensions["timestamp"] = DateTime.UtcNow.ToString("o");
        }
    }
}
