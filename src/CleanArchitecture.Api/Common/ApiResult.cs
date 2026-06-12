using System;
using System.Collections.Generic;
using CleanArchitecture.Api.Common.Responses;
using Microsoft.AspNetCore.Http;

namespace CleanArchitecture.Api.Common
{
    // Builds the §4.5 response envelopes with traceId + timestamp filled in, so
    // controllers and the exception filter never construct an envelope by hand.
    public static class ApiResult
    {
        public static SuccessResponse<T> Success<T>(HttpContext context, T data, PaginationMeta? meta = null)
        {
            return new SuccessResponse<T>(context.GetTraceId(), DateTimeOffset.UtcNow, data, meta);
        }

        public static ErrorResponse Error(HttpContext context, string code, string message,
            IReadOnlyList<FieldError>? details = null)
        {
            return new ErrorResponse(context.GetTraceId(), DateTimeOffset.UtcNow, new ApiError(code, message, details));
        }
    }
}
