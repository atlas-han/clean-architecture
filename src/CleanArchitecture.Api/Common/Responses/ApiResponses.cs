using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace CleanArchitecture.Api.Common.Responses
{
    // Success/Error response envelopes per API design guide §4.5.
    // Success and Error are strictly separate types; both carry traceId and
    // timestamp at the top level. There is no `success` flag (§4.2) — clients
    // branch on the HTTP status code, then deserialize into the matching type.

    // T is a single object (e.g. ProductDto) for single-resource reads/creates,
    // or a list (e.g. IReadOnlyList<ProductDto>) for collection reads. A list
    // serializes Data as a JSON array and carries pagination in Meta (§4.2).
    public record SuccessResponse<T>(
        string TraceId,
        DateTimeOffset Timestamp,
        T Data,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] PaginationMeta? Meta = null
    );

    // No `data` field on failures (§4.3) — only the error object.
    public record ErrorResponse(
        string TraceId,
        DateTimeOffset Timestamp,
        ApiError Error
    );

    public record ApiError(
        string Code,
        string Message,
        // Field-level errors; present only on validation failures (§4.3). For
        // every other error the key is omitted entirely (not null, not []).
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<FieldError>? Details = null
    );

    public record FieldError(string Field, string Message);

    public record PaginationMeta(int Page, int PageSize, long TotalCount, int TotalPages);
}
