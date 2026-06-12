using System;
using System.Collections.Generic;

namespace CleanArchitecture.Api.Logging
{
    // Decides how a single HTTP header is logged in the access log
    // (§14.3 req_header_*/res_header_*). Sensitive headers (§14.6 —
    // Authorization, Cookie, Set-Cookie, X-Api-Key and the proxy-auth sibling) are
    // redacted to "***", so the header's presence stays visible for debugging while
    // its value never reaches the log. Every other header is logged verbatim.
    // Matching is case-insensitive, mirroring HTTP header semantics.
    public static class HeaderMasker
    {
        private const string Redacted = "***";

        private static readonly HashSet<string> SensitiveHeaders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Authorization",
            "Proxy-Authorization",
            "Cookie",
            "Set-Cookie",
            "X-Api-Key"
        };

        public static bool IsSensitive(string name) => SensitiveHeaders.Contains(name);

        // Returns the value to log for a header: the original value, or "***" when
        // the header carries a credential/PII that must not be written in clear text.
        public static string Mask(string name, string value)
        {
            return SensitiveHeaders.Contains(name) ? Redacted : value;
        }
    }
}
