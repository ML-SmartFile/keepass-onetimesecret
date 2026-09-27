using System.Collections.Generic;
using OneTimeSecret.Ots;
using Xunit;

namespace OneTimeSecret.Tests
{
    public class JsonTests
    {
        [Fact]
        public void V1SuccessPayload_IsRead()
        {
            var obj = Json.ParseObject("{\"secret_key\":\"abc123\",\"metadata_key\":\"def456\"}");
            Assert.Equal("abc123", Json.GetString(obj, "secret_key"));
            Assert.Equal("def456", Json.GetString(obj, "metadata_key"));
        }

        [Fact]
        public void UnknownFields_AreIgnored()
        {
            var obj = Json.ParseObject(
                "{\"secret_key\":\"abc\",\"metadata_key\":\"def\",\"future_field\":{\"nested\":[1,2,3]},\"ttl\":86400}");
            Assert.Equal("abc", Json.GetString(obj, "secret_key"));
        }

        [Fact]
        public void MissingExpectedField_FailsLoudly()
        {
            var obj = Json.ParseObject("{\"metadata_key\":\"def\"}");
            var ex = Assert.Throws<JsonException>(() => Json.GetString(obj, "secret_key"));
            Assert.Contains("secret_key", ex.Message);
        }

        [Fact]
        public void NullValuedField_CountsAsMissing()
        {
            var obj = Json.ParseObject("{\"secret_key\":null}");
            Assert.Throws<JsonException>(() => Json.GetString(obj, "secret_key"));
        }

        [Fact]
        public void NonStringField_FailsLoudly()
        {
            var obj = Json.ParseObject("{\"secret_key\":12345}");
            Assert.Throws<JsonException>(() => Json.GetString(obj, "secret_key"));
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("{")]
        [InlineData("{\"a\":")]
        [InlineData("{\"a\":\"unterminated")]
        [InlineData("{\"a\":1}garbage")]
        public void TruncatedOrMalformedBody_Throws(string body)
        {
            Assert.Throws<JsonException>(() => Json.ParseObject(body));
        }

        [Fact]
        public void NonObjectTopLevel_Throws()
        {
            Assert.Throws<JsonException>(() => Json.ParseObject("[1,2,3]"));
        }

        [Fact]
        public void StringEscapes_AreDecoded()
        {
            var obj = Json.ParseObject("{\"a\":\"line1\\nline2\\ttab \\\"quoted\\\" back\\\\slash \\u00e4\"}");
            Assert.Equal("line1\nline2\ttab \"quoted\" back\\slash \u00e4", Json.GetString(obj, "a"));
        }

        [Fact]
        public void NestedObjects_AreTraversable()
        {
            // The v2 shape in SPEC.md is nested; the reader must already cope with it.
            var obj = Json.ParseObject(
                "{\"record\":{\"secret\":{\"key\":\"sk\"},\"receipt\":{\"key\":\"rk\"}},\"share_domain\":null}");
            var record = (Dictionary<string, object>)obj["record"];
            var secret = (Dictionary<string, object>)record["secret"];
            Assert.Equal("sk", Json.GetString(secret, "key"));
        }

        [Fact]
        public void ErrorPayload_FieldsAreReadable()
        {
            var obj = Json.ParseObject(
                "{\"error\":\"bad ttl\",\"error_type\":\"validation\",\"field\":\"ttl\",\"request_id\":\"req-1\"}");
            Assert.Equal("bad ttl", Json.GetStringOrNull(obj, "error"));
            Assert.Equal("ttl", Json.GetStringOrNull(obj, "field"));
            Assert.Equal("req-1", Json.GetStringOrNull(obj, "request_id"));
        }

        [Fact]
        public void GetStringOrNull_ReturnsNullForAbsentOrNonString()
        {
            var obj = Json.ParseObject("{\"n\":123}");
            Assert.Null(Json.GetStringOrNull(obj, "missing"));
            Assert.Null(Json.GetStringOrNull(obj, "n"));
        }

        [Fact]
        public void EmptyObjectAndArray_Parse()
        {
            Assert.Empty(Json.ParseObject("{}"));
            Assert.Equal(new List<object>(), Json.Parse("[]"));
        }
    }
}
