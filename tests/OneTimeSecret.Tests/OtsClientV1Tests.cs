using System;
using System.Linq;
using System.Text;
using OneTimeSecret.Ots;
using Xunit;

namespace OneTimeSecret.Tests
{
    public class OtsClientV1Tests
    {
        private const string Secret = "alice\r\nPässwörd&special=stuff!";

        private sealed class StubTransport : IHttpTransport
        {
            private readonly HttpResult m_result;
            private readonly HttpTransportException m_failure;

            public StubTransport(HttpResult result) { m_result = result; }
            public StubTransport(HttpTransportException failure) { m_failure = failure; }

            public Uri LastUri { get; private set; }
            public string LastContentType { get; private set; }
            public string LastBodyText { get; private set; }
            public byte[] LastBodyBuffer { get; private set; }

            public HttpResult Post(Uri uri, string contentType, byte[] body, int timeoutMs)
            {
                LastUri = uri;
                LastContentType = contentType;
                LastBodyBuffer = body;                              // kept to observe scrubbing
                LastBodyText = Encoding.UTF8.GetString(body);       // snapshot before the caller zeroes it

                if (m_failure != null) throw m_failure;
                return m_result;
            }
        }

        private static StubTransport Success()
        {
            return new StubTransport(new HttpResult(200,
                "{\"secret_key\":\"SECRETKEY\",\"metadata_key\":\"METAKEY\",\"ttl\":86400}"));
        }

        private static OtsClientV1 Client(IHttpTransport transport, string baseUrl = "https://eu.onetimesecret.com")
        {
            return new OtsClientV1(new Uri(baseUrl), transport);
        }

        [Fact]
        public void Share_PostsFormEncodedSecretAndTtl()
        {
            var transport = Success();
            Client(transport).Share(new ShareRequest(Secret, 86400));

            Assert.Equal("https://eu.onetimesecret.com/api/v1/share", transport.LastUri.AbsoluteUri);
            Assert.Equal("application/x-www-form-urlencoded", transport.LastContentType);
            Assert.Contains("ttl=86400", transport.LastBodyText);
            Assert.StartsWith("secret=", transport.LastBodyText);

            // The secret must be escaped, not passed raw.
            Assert.DoesNotContain("&special=stuff", transport.LastBodyText);
            Assert.Contains(Uri.EscapeDataString(Secret), transport.LastBodyText);
        }

        [Fact]
        public void Share_BuildsShareAndReceiptUrls()
        {
            ShareResult result = Client(Success()).Share(new ShareRequest(Secret, 86400));

            Assert.Equal("SECRETKEY", result.SecretKey);
            Assert.Equal("METAKEY", result.MetadataKey);
            Assert.Equal("https://eu.onetimesecret.com/secret/SECRETKEY", result.ShareUrl);
            Assert.Equal("https://eu.onetimesecret.com/private/METAKEY", result.ReceiptUrl);
        }

        [Fact]
        public void Share_HonoursAPathPrefixedBaseUrl()
        {
            ShareResult result = Client(Success(), "https://example.com/ots").Share(new ShareRequest(Secret, 86400));

            Assert.Equal("https://example.com/ots/secret/SECRETKEY", result.ShareUrl);
        }

        [Fact]
        public void Share_PrefersTheServerSuppliedReceiptUrl()
        {
            // Constructing this ourselves produced a 404: the hosted service serves the
            // receipt from /receipt/, not the /private/ path SPEC.md documented.
            var transport = new StubTransport(new HttpResult(200,
                "{\"secret_key\":\"SK\",\"metadata_key\":\"MK\"," +
                "\"receipt_url\":\"https://eu.onetimesecret.com/receipt/MK\"," +
                "\"metadata_url\":\"https://eu.onetimesecret.com/receipt/MK\"}"));

            ShareResult result = Client(transport).Share(new ShareRequest(Secret, 86400));
            Assert.Equal("https://eu.onetimesecret.com/receipt/MK", result.ReceiptUrl);
        }

        [Fact]
        public void Share_FallsBackToMetadataUrlWhenReceiptUrlIsAbsent()
        {
            var transport = new StubTransport(new HttpResult(200,
                "{\"secret_key\":\"SK\",\"metadata_key\":\"MK\"," +
                "\"metadata_url\":\"https://eu.onetimesecret.com/receipt/MK\"}"));

            ShareResult result = Client(transport).Share(new ShareRequest(Secret, 86400));
            Assert.Equal("https://eu.onetimesecret.com/receipt/MK", result.ReceiptUrl);
        }

        [Fact]
        public void Share_FallsBackToTheLegacyPrivatePathForOlderInstances()
        {
            // Self-hosted instances predating these fields still serve /private/.
            var transport = new StubTransport(new HttpResult(200,
                "{\"secret_key\":\"SK\",\"metadata_key\":\"MK\"}"));

            ShareResult result = Client(transport, "https://ots.internal").Share(new ShareRequest(Secret, 86400));
            Assert.Equal("https://ots.internal/private/MK", result.ReceiptUrl);
        }

        [Fact]
        public void Share_ReadsARealWorldV1Payload()
        {
            // Captured verbatim from eu.onetimesecret.com, trimmed of nothing but whitespace.
            var transport = new StubTransport(new HttpResult(200,
                "{\"custid\":\"anon\",\"metadata_key\":\"MK\",\"secret_key\":\"SK\",\"ttl\":300," +
                "\"metadata_ttl\":600,\"secret_ttl\":300," +
                "\"metadata_url\":\"https://eu.onetimesecret.com/receipt/MK\",\"state\":\"new\"," +
                "\"updated\":1790198498,\"created\":1790198498,\"recipient\":[],\"share_domain\":\"\"," +
                "\"passphrase_required\":false,\"identifier\":\"MK\",\"secret_identifier\":\"SK\"," +
                "\"has_passphrase\":false,\"recipients\":[],\"receipt_ttl\":600," +
                "\"receipt_url\":\"https://eu.onetimesecret.com/receipt/MK\"}"));

            ShareResult result = Client(transport).Share(new ShareRequest(Secret, 300));

            Assert.Equal("https://eu.onetimesecret.com/secret/SK", result.ShareUrl);
            Assert.Equal("https://eu.onetimesecret.com/receipt/MK", result.ReceiptUrl);
        }

        [Fact]
        public void Share_ZeroesTheRequestBodyBuffer()
        {
            var transport = Success();
            Client(transport).Share(new ShareRequest(Secret, 86400));

            Assert.True(transport.LastBodyBuffer.All(b => b == 0),
                "the form body holding the secret should be zeroed once the request is done");
        }

        [Fact]
        public void Share_ZeroesTheRequestBodyBufferEvenWhenTheRequestFails()
        {
            var transport = new StubTransport(
                new HttpTransportException(HttpFailureKind.Timeout, "timed out", null));

            Assert.Throws<OtsException>(() => Client(transport).Share(new ShareRequest(Secret, 86400)));
            Assert.True(transport.LastBodyBuffer.All(b => b == 0));
        }

        [Theory]
        [InlineData(401)]
        [InlineData(403)]
        public void Share_MapsAuthFailures(int status)
        {
            var transport = new StubTransport(new HttpResult(status, ""));
            var ex = Assert.Throws<OtsException>(() => Client(transport).Share(new ShareRequest(Secret, 86400)));

            Assert.Contains("credentials", ex.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(status, ex.StatusCode);
        }

        [Fact]
        public void Share_MapsRateLimiting()
        {
            var transport = new StubTransport(new HttpResult(429, ""));
            var ex = Assert.Throws<OtsException>(() => Client(transport).Share(new ShareRequest(Secret, 86400)));

            Assert.Contains("rate-limit", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void Share_CallsOutTheAnonymousTtlCapWhenTheServerBlamesTtl()
        {
            var transport = new StubTransport(new HttpResult(422,
                "{\"error\":\"ttl too long\",\"field\":\"ttl\",\"request_id\":\"req-9\"}"));
            var ex = Assert.Throws<OtsException>(() => Client(transport).Share(new ShareRequest(Secret, 86400)));

            Assert.Contains("lifetime", ex.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("ttl too long", ex.Message);
            Assert.Contains("req-9", ex.Message);
            Assert.Equal("req-9", ex.RequestId);
        }

        [Fact]
        public void Share_MapsTransportFailures()
        {
            var timeout = new StubTransport(new HttpTransportException(HttpFailureKind.Timeout, "t", null));
            Assert.Contains("timed out",
                Assert.Throws<OtsException>(() => Client(timeout).Share(new ShareRequest(Secret, 86400))).Message,
                StringComparison.OrdinalIgnoreCase);

            var dns = new StubTransport(new HttpTransportException(HttpFailureKind.NameResolution, "d", null));
            Assert.Contains("could not be resolved",
                Assert.Throws<OtsException>(() => Client(dns).Share(new ShareRequest(Secret, 86400))).Message,
                StringComparison.OrdinalIgnoreCase);

            var tls = new StubTransport(new HttpTransportException(HttpFailureKind.Tls, "x", null));
            var tlsEx = Assert.Throws<OtsException>(() => Client(tls).Share(new ShareRequest(Secret, 86400)));
            Assert.Contains("certificate", tlsEx.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("not sent", tlsEx.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void Share_SurfacesAnUnreadableSuccessBody()
        {
            var transport = new StubTransport(new HttpResult(200, "<html>not json</html>"));
            Assert.Throws<OtsException>(() => Client(transport).Share(new ShareRequest(Secret, 86400)));
        }

        [Fact]
        public void Share_MissingKeyInSuccessBody_FailsLoudly()
        {
            var transport = new StubTransport(new HttpResult(200, "{\"metadata_key\":\"METAKEY\"}"));
            Assert.Throws<JsonException>(() => Client(transport).Share(new ShareRequest(Secret, 86400)));
        }

        [Fact]
        public void Share_NeverPutsTheSecretInAnErrorMessage()
        {
            // Security regression guard for non-negotiable #4.
            var cases = new IHttpTransport[]
            {
                new StubTransport(new HttpResult(401, "")),
                new StubTransport(new HttpResult(429, "")),
                new StubTransport(new HttpResult(422, "{\"error\":\"bad\",\"field\":\"ttl\"}")),
                new StubTransport(new HttpResult(500, "<html>boom</html>")),
                new StubTransport(new HttpResult(200, "not json")),
                new StubTransport(new HttpTransportException(HttpFailureKind.Tls, "x", null)),
            };

            foreach (IHttpTransport transport in cases)
            {
                var ex = Assert.ThrowsAny<Exception>(
                    () => Client(transport).Share(new ShareRequest(Secret, 86400)));

                Assert.DoesNotContain("Pässwörd", ex.ToString());
                Assert.DoesNotContain("alice", ex.ToString());
            }
        }
    }
}
