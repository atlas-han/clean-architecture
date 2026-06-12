using System;
using System.Buffers;
using System.Collections.Generic;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace CleanArchitecture.Api.Logging
{
    // Masks personally-identifiable values inside request/response bodies before they
    // are written to the access log (§14.6). Matching is by JSON property name,
    // case-insensitive, at any nesting depth. String values are partially masked
    // (first character kept, the rest replaced with '*'); non-string values under a
    // PII key are fully redacted. Bodies that are not valid JSON are returned
    // unchanged — key-based masking cannot apply to them.
    public static class PiiMasker
    {
        private const string Redacted = "***";

        // Relaxed escaping keeps non-ASCII (e.g. Korean names) readable in the log
        // instead of emitting \uXXXX. The output is log text, never HTML, so the
        // relaxed encoder's looser escaping of <, >, & is not a concern here.
        private static readonly JsonWriterOptions WriterOptions = new JsonWriterOptions
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        // PII field names. Compared with the body's JSON keys case-insensitively, so
        // both camelCase (the API's serialized casing) and other casings are covered.
        // Note: a bare "name" is intentionally NOT here — that is the (non-PII) Product
        // name; the PII customer name is carried as "customerName".
        private static readonly HashSet<string> PiiKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "customerName",
            "firstName",
            "lastName",
            "fullName",
            "email",
            "emailAddress",
            "phone",
            "phoneNumber",
            "mobile",
            "password",
            "ssn",
            "socialSecurityNumber",
            "address",
            "cardNumber",
            "creditCardNumber",
            "cvv",
            "dateOfBirth",
            "dob"
        };

        public static string? Mask(string? body)
        {
            if (string.IsNullOrEmpty(body))
            {
                return body;
            }

            try
            {
                using (var document = JsonDocument.Parse(body))
                {
                    var buffer = new ArrayBufferWriter<byte>();
                    using (var writer = new Utf8JsonWriter(buffer, WriterOptions))
                    {
                        WriteMaskedElement(writer, document.RootElement);
                    }

                    return Encoding.UTF8.GetString(buffer.WrittenSpan);
                }
            }
            catch (JsonException)
            {
                // Not JSON (e.g. form-encoded or plain text). Leave it untouched.
                return body;
            }
        }

        private static void WriteMaskedElement(Utf8JsonWriter writer, JsonElement element)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    writer.WriteStartObject();
                    foreach (var property in element.EnumerateObject())
                    {
                        writer.WritePropertyName(property.Name);
                        if (PiiKeys.Contains(property.Name))
                        {
                            WriteRedactedValue(writer, property.Value);
                        }
                        else
                        {
                            WriteMaskedElement(writer, property.Value);
                        }
                    }
                    writer.WriteEndObject();
                    break;
                case JsonValueKind.Array:
                    writer.WriteStartArray();
                    foreach (var item in element.EnumerateArray())
                    {
                        WriteMaskedElement(writer, item);
                    }
                    writer.WriteEndArray();
                    break;
                case JsonValueKind.String:
                    writer.WriteStringValue(element.GetString());
                    break;
                case JsonValueKind.Number:
                    // Preserve the exact numeric token (int/decimal/exponent).
                    writer.WriteRawValue(element.GetRawText());
                    break;
                case JsonValueKind.True:
                case JsonValueKind.False:
                    writer.WriteBooleanValue(element.GetBoolean());
                    break;
                case JsonValueKind.Null:
                    writer.WriteNullValue();
                    break;
                default:
                    writer.WriteNullValue();
                    break;
            }
        }

        private static void WriteRedactedValue(Utf8JsonWriter writer, JsonElement value)
        {
            switch (value.ValueKind)
            {
                case JsonValueKind.String:
                    writer.WriteStringValue(PartialMask(value.GetString()));
                    break;
                case JsonValueKind.Null:
                    writer.WriteNullValue();
                    break;
                default:
                    // Numbers, booleans, or a nested object/array under a PII key:
                    // partial masking is meaningless, so redact the whole value.
                    writer.WriteStringValue(Redacted);
                    break;
            }
        }

        // Keeps the first character and replaces the remainder with '*'. A single
        // character (or shorter) is fully masked so nothing is revealed.
        private static string? PartialMask(string? value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return value;
            }

            if (value.Length == 1)
            {
                return "*";
            }

            return value.Substring(0, 1) + new string('*', value.Length - 1);
        }
    }
}
