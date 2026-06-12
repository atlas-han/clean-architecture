using System.Text.Json;
using CleanArchitecture.Api.Logging;
using Xunit;

namespace CleanArchitecture.Api.IntegrationTests
{
    public class PiiMaskerTests
    {
        private static JsonElement MaskAndParse(string body)
        {
            var masked = PiiMasker.Mask(body);
            Assert.NotNull(masked);
            return JsonDocument.Parse(masked!).RootElement;
        }

        [Fact]
        public void Mask_PartiallyMasksCustomerName_KeepsFirstChar()
        {
            var root = MaskAndParse("{\"customerName\":\"홍길동\",\"items\":[]}");

            // "홍길동" (3 chars) -> first char kept, rest starred.
            Assert.Equal("홍**", root.GetProperty("customerName").GetString());
        }

        [Fact]
        public void Mask_LeavesNonPiiFieldsUntouched()
        {
            var root = MaskAndParse("{\"name\":\"Widget\",\"description\":\"A product\",\"price\":9.5,\"stock\":3}");

            // Product name/description are not PII and must pass through verbatim.
            Assert.Equal("Widget", root.GetProperty("name").GetString());
            Assert.Equal("A product", root.GetProperty("description").GetString());
            Assert.Equal(9.5, root.GetProperty("price").GetDouble());
            Assert.Equal(3, root.GetProperty("stock").GetInt32());
        }

        [Fact]
        public void Mask_IsCaseInsensitiveOnKeyName()
        {
            var root = MaskAndParse("{\"CustomerName\":\"Alice\",\"EMAIL\":\"a@b.com\"}");

            Assert.Equal("A****", root.GetProperty("CustomerName").GetString());
            Assert.Equal("a******", root.GetProperty("EMAIL").GetString());
        }

        [Fact]
        public void Mask_MasksNestedAndArrayOccurrences()
        {
            var root = MaskAndParse(
                "{\"order\":{\"customerName\":\"Bob\"},\"people\":[{\"customerName\":\"Carol\"},{\"customerName\":\"Dave\"}]}");

            Assert.Equal("B**", root.GetProperty("order").GetProperty("customerName").GetString());

            var people = root.GetProperty("people");
            Assert.Equal("C****", people[0].GetProperty("customerName").GetString());
            Assert.Equal("D***", people[1].GetProperty("customerName").GetString());
        }

        [Fact]
        public void Mask_MasksCommonPiiKeys()
        {
            var root = MaskAndParse("{\"password\":\"secret\",\"phoneNumber\":\"01012345678\"}");

            Assert.Equal("s*****", root.GetProperty("password").GetString());
            Assert.Equal("0**********", root.GetProperty("phoneNumber").GetString());
        }

        [Fact]
        public void Mask_SingleCharValue_IsFullyMasked()
        {
            var root = MaskAndParse("{\"customerName\":\"X\"}");

            // Keeping the first char of a 1-char value would reveal all of it.
            Assert.Equal("*", root.GetProperty("customerName").GetString());
        }

        [Fact]
        public void Mask_NonStringPiiValue_IsFullyRedacted()
        {
            var root = MaskAndParse("{\"cardNumber\":1234567890123456}");

            // Partial masking is meaningless for a number, so the whole value is redacted.
            Assert.Equal("***", root.GetProperty("cardNumber").GetString());
        }

        [Fact]
        public void Mask_NullPiiValue_StaysNull()
        {
            var root = MaskAndParse("{\"customerName\":null}");

            Assert.Equal(JsonValueKind.Null, root.GetProperty("customerName").ValueKind);
        }

        [Fact]
        public void Mask_NonJsonBody_IsReturnedUnchanged()
        {
            const string body = "customerName=Bob&plain text, not json";
            Assert.Equal(body, PiiMasker.Mask(body));
        }

        [Fact]
        public void Mask_NullOrEmpty_IsReturnedUnchanged()
        {
            Assert.Null(PiiMasker.Mask(null));
            Assert.Equal("", PiiMasker.Mask(""));
        }

        [Fact]
        public void Mask_ObjectOrArrayUnderPiiKey_IsFullyRedacted()
        {
            // Partial masking is meaningless for a structured value, so the whole
            // thing collapses to the redaction marker — no sub-field can leak.
            var nestedObject = MaskAndParse("{\"address\":{\"street\":\"Main St\",\"zip\":\"12345\"}}");
            Assert.Equal("***", nestedObject.GetProperty("address").GetString());

            var nestedArray = MaskAndParse("{\"email\":[\"a@b.com\",\"c@d.com\"]}");
            Assert.Equal("***", nestedArray.GetProperty("email").GetString());
        }

        [Fact]
        public void Mask_RootLevelArrayOfPiiObjects_MasksEach()
        {
            var masked = PiiMasker.Mask("[{\"email\":\"a@b.com\"},{\"email\":\"c@d.com\"}]");
            Assert.NotNull(masked);

            var root = JsonDocument.Parse(masked!).RootElement;
            Assert.Equal("a******", root[0].GetProperty("email").GetString());
            Assert.Equal("c******", root[1].GetProperty("email").GetString());
        }

        [Fact]
        public void Mask_EmptyStringPiiValue_StaysEmpty()
        {
            var root = MaskAndParse("{\"email\":\"\"}");

            Assert.Equal("", root.GetProperty("email").GetString());
        }

        [Fact]
        public void Mask_LongPiiValue_IsMaskedAcrossFullLength()
        {
            // Masking runs on the full body before the middleware truncates it (4096-char
            // cap), so a PII value longer than the cap is masked end-to-end and cannot
            // survive a later cut as cleartext.
            var secret = new string('A', 5000);
            var root = MaskAndParse("{\"customerName\":\"" + secret + "\"}");

            Assert.Equal("A" + new string('*', 4999), root.GetProperty("customerName").GetString());
        }
    }
}
