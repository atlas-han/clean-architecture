namespace CleanArchitecture.Api.Common
{
    // Domain error codes per API design guide §6.2 (UPPER_SNAKE_CASE). Only the
    // codes this API actually emits are declared; the guide also lists
    // UNAUTHORIZED/FORBIDDEN/RATE_LIMIT_EXCEEDED, which belong to middleware
    // this sample does not yet host.
    public static class ErrorCodes
    {
        public const string ValidationError = "VALIDATION_ERROR";              // 400
        public const string NotFound = "NOT_FOUND";                            // 404
        public const string Conflict = "CONFLICT";                             // 409
        public const string BusinessRuleViolation = "BUSINESS_RULE_VIOLATION"; // 422
        public const string DeadlineExceeded = "DEADLINE_EXCEEDED";            // 504 (§7.4)
        public const string InternalError = "INTERNAL_ERROR";                  // 500
        public const string ServiceUnavailable = "SERVICE_UNAVAILABLE";        // 503 (maintenance)
    }
}
