namespace CleanArchitecture.Api.Common
{
    public static class ErrorCodes
    {
        public const string ValidationFailed = "VALIDATION_FAILED";
        public const string DomainRuleViolated = "DOMAIN_RULE_VIOLATED";
        public const string ResourceNotFound = "RESOURCE_NOT_FOUND";
        public const string InternalError = "INTERNAL_ERROR";

        public const string TypeBase = "https://api.cleanarchitecture/errors/";
        public const string TypeValidationFailed = TypeBase + "validation-failed";
        public const string TypeDomainRuleViolated = TypeBase + "domain-rule-violated";
        public const string TypeResourceNotFound = TypeBase + "resource-not-found";
        public const string TypeInternalError = TypeBase + "internal-error";
    }
}
