using System;
using System.Collections.Generic;
using CleanArchitecture.Application.Common.Exceptions;
using CleanArchitecture.Domain.Exceptions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace CleanArchitecture.Api.Filters
{
    public class ApiExceptionFilter : ExceptionFilterAttribute
    {
        private readonly IDictionary<Type, Action<ExceptionContext>> _handlers;

        public ApiExceptionFilter()
        {
            _handlers = new Dictionary<Type, Action<ExceptionContext>>
            {
                [typeof(ValidationException)] = HandleValidation,
                [typeof(NotFoundException)] = HandleNotFound,
                [typeof(DomainException)] = HandleDomain,
            };
        }

        public override void OnException(ExceptionContext context)
        {
            HandleException(context);
            base.OnException(context);
        }

        private void HandleException(ExceptionContext context)
        {
            var type = context.Exception.GetType();
            if (_handlers.TryGetValue(type, out var handler))
            {
                handler.Invoke(context);
                return;
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
                Type = "https://tools.ietf.org/html/rfc7231#section-6.5.1"
            };

            context.Result = new BadRequestObjectResult(details);
            context.ExceptionHandled = true;
        }

        private static void HandleNotFound(ExceptionContext context)
        {
            var details = new ProblemDetails
            {
                Title = "The specified resource was not found.",
                Status = StatusCodes.Status404NotFound,
                Detail = context.Exception.Message
            };

            context.Result = new NotFoundObjectResult(details);
            context.ExceptionHandled = true;
        }

        private static void HandleDomain(ExceptionContext context)
        {
            var details = new ProblemDetails
            {
                Title = "A domain rule was violated.",
                Status = StatusCodes.Status400BadRequest,
                Detail = context.Exception.Message
            };

            context.Result = new BadRequestObjectResult(details);
            context.ExceptionHandled = true;
        }

        private static void HandleUnknown(ExceptionContext context)
        {
            var details = new ProblemDetails
            {
                Status = StatusCodes.Status500InternalServerError,
                Title = "An error occurred while processing your request.",
                Type = "https://tools.ietf.org/html/rfc7231#section-6.6.1"
            };

            context.Result = new ObjectResult(details)
            {
                StatusCode = StatusCodes.Status500InternalServerError
            };
            context.ExceptionHandled = true;
        }
    }
}
