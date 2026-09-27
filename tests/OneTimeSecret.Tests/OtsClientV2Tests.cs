using System;
using System.Linq;
using System.Text;
using System.Threading;
using OneTimeSecret.Ots;
using Xunit;

namespace OneTimeSecret.Tests
{
    public class OtsClientV2Tests
    {
        private const string Secret = "alice\r\nPässwörd&special=stuff!";

        private sealed class StubTransport : IHttpTransport
        {
            private readonly HttpResult m_result;
            private readonly HttpTransportException m_failure;
            private readonly bool m_throwCancelled;

            public StubTransport(HttpResult result) { m_result = result; }
            public StubTransport(HttpTransportException failure) { m_failure = failure; }
            private StubTransport(bool throwCancelled) { m_throwCancelled = throwCancelled; }

            public static StubTransport Cancelling() { return new StubTransport(true); }

            public Uri LastUri { get; private set; }
            public string LastContentType { get; private set; }
            public string LastBodyText { get; private set; }
            public byte[] LastBodyBuffer { get; private set; }
            public string LastAuthorization { get; private set; }

            public HttpResult Post(Uri uri, string contentType, byte[] body,
                string authorizationHeader, int timeoutMs, CancellationToken cancellationToken)
            {
                LastUri = uri;
                LastContentType = contentType;
                LastAuthorization = authorizationHeader;
                LastBodyBuffer = body;
                LastBodyText = Encoding.UTF8.GetString(body);

                if (m_throwCancelled) throw new OperationCanceledException(cancellationToken);
                if (m_failure != null) throw m_failure;
                return m_result;
            }

            public HttpResult Get(Uri uri, string authorizationHeader, int timeoutMs,
                CancellationToken cancellationToken)
            {
                throw new NotSupportedException();
            }
        }

        // Captured verbatim from a live anonymous call to eu.onetimesecret.com's v2 conceal
        // endpoint, September 2026, trimmed of nothing but whitespace. The shape here is what
        // corrected SPEC.md's guess: wrapped in success/details, no ready-made URLs anywhere,
        // record.receipt and record.secret carry far more fields than the earlier draft
        // expected.
        private const string RealConcealPayload =
            "{\"success\":true,\"record\":{\"receipt\":{\"identifier\":\"lpnyp050hv9saj1bzjp9d4hqm03icd9qperyjfi0jvq0acjfxg25p4mm3yrokl\"," +
            "\"key\":\"lpnyp050hv9saj1bzjp9d4hqm03icd9qperyjfi0jvq0acjfxg25p4mm3yrokl\",\"custid\":null,\"owner_id\":null," +
            "\"state\":\"new\",\"secret_shortid\":\"c4n2ljdr\",\"secret_ttl\":300,\"metadata_ttl\":300,\"receipt_ttl\":300," +
            "\"lifespan\":300,\"share_domain\":null,\"has_passphrase\":false,\"kind\":\"conceal\",\"source\":\"standard\"}," +
            "\"metadata\":{\"identifier\":\"lpnyp050hv9saj1bzjp9d4hqm03icd9qperyjfi0jvq0acjfxg25p4mm3yrokl\"," +
            "\"key\":\"lpnyp050hv9saj1bzjp9d4hqm03icd9qperyjfi0jvq0acjfxg25p4mm3yrokl\",\"secret_ttl\":300}," +
            "\"secret\":{\"identifier\":\"c4n2ljdrshq7vklmrw4w5wb3xcxnn6z57f5n8pon4cf9ajvxq9xbedhj52oynh\"," +
            "\"key\":\"c4n2ljdrshq7vklmrw4w5wb3xcxnn6z57f5n8pon4cf9ajvxq9xbedhj52oynh\",\"shortid\":\"c4n2ljdr\"," +
            "\"state\":\"new\",\"lifespan\":300,\"secret_ttl\":300,\"has_passphrase\":false,\"verification\":false}," +
            "\"share_domain\":null},\"details\":{\"kind\":\"conceal\",\"recipient\":[],\"recipient_safe\":[]}}";

        // Captured verbatim from a live burn attempt against that same anonymous secret,
        // replaying its own session cookie and CSRF token - still "success":false, because
        // (per SPEC.md section 5) burning an anonymous secret apparently never succeeds.
        private const string RealBurnDeclinedPayload =
            "{\"success\":false,\"record\":{\"identifier\":\"lpnyp050hv9saj1bzjp9d4hqm03icd9qperyjfi0jvq0acjfxg25p4mm3yrokl\"," +
            "\"key\":\"lpnyp050hv9saj1bzjp9d4hqm03icd9qperyjfi0jvq0acjfxg25p4mm3yrokl\",\"is_burned\":false," +
            "\"share_url\":\"https://eu.onetimesecret.com/secret/c4n2ljdrshq7vklmrw4w5wb3xcxnn6z57f5n8pon4cf9ajvxq9xbedhj52oynh\"," +
            "\"receipt_url\":\"https://eu.onetimesecret.com/receipt/lpnyp050hv9saj1bzjp9d4hqm03icd9qperyjfi0jvq0acjfxg25p4mm3yrokl\"}," +
            "\"details\":{\"type\":\"record\",\"title\":\"Secret burned\",\"show_receipt\":true}}";

        private static StubTransport Success()
        {
            return new StubTransport(new HttpResult(200,
                "{\"success\":true,\"record\":{" +
                "\"receipt\":{\"key\":\"RECEIPTKEY\",\"secret_ttl\":86400}," +
                "\"secret\":{\"key\":\"SECRETKEY\",\"secret_ttl\":86400}," +
                "\"share_domain\":null}}"));
        }

        private static OtsClientV2 Client(IHttpTransport transport, string baseUrl = "https://eu.onetimesecret.com")
        {
            return new OtsClientV2(new Uri(baseUrl), transport);
        }

        [Fact]
        public void Share_PostsJsonBodyWithSecretAndTtl()
        {
            var transport = Success();
            Client(transport).Share(new ShareRequest(Secret, 86400));

            Assert.Equal("https://eu.onetimesecret.com/api/v2/secret/conceal", transport.LastUri.AbsoluteUri);
            Assert.Equal("application/json", transport.LastContentType);
            Assert.Contains("\"kind\":\"conceal\"", transport.LastBodyText);
            Assert.Contains("\"ttl\":\"86400\"", transport.LastBodyText);
        }

        [Fact]
        public void Share_EscapesTheSecretInTheJsonBody()
        {
            var transport = Success();
            Client(transport).Share(new ShareRequest(Secret, 86400));

            // A raw CR/LF inside a JSON string would produce invalid JSON.
            Assert.Contains("\\r\\n", transport.LastBodyText);
            Assert.DoesNotContain("\r\n", transport.LastBodyText);
        }

        [Fact]
        public void Share_EscapesAQuoteAndBackslashInTheSecret()
        {
            var transport = Success();
            Client(transport).Share(new ShareRequest("a\"quote\\and backslash", 86400));

            Assert.Contains("a\\\"quote\\\\and backslash", transport.LastBodyText);
        }

        [Fact]
        public void Share_ReadsARealWorldV2Payload()
        {
            var transport = new StubTransport(new HttpResult(200, RealConcealPayload));

            ShareResult result = Client(transport).Share(new ShareRequest(Secret, 300));

            Assert.Equal("c4n2ljdrshq7vklmrw4w5wb3xcxnn6z57f5n8pon4cf9ajvxq9xbedhj52oynh", result.SecretKey);
            Assert.Equal("lpnyp050hv9saj1bzjp9d4hqm03icd9qperyjfi0jvq0acjfxg25p4mm3yrokl", result.MetadataKey);
            Assert.Equal(
                "https://eu.onetimesecret.com/secret/c4n2ljdrshq7vklmrw4w5wb3xcxnn6z57f5n8pon4cf9ajvxq9xbedhj52oynh",
                result.ShareUrl);
            Assert.Equal(
                "https://eu.onetimesecret.com/receipt/lpnyp050hv9saj1bzjp9d4hqm03icd9qperyjfi0jvq0acjfxg25p4mm3yrokl",
                result.ReceiptUrl);
            Assert.Equal(300, result.EffectiveTtlSeconds);
        }

        [Fact]
        public void Share_BuildsBothUrlsFromBareKeysUnlikeV1()
        {
            // Unlike v1, the conceal response hands back no receipt_url/share_url at all -
            // both links must be constructed from record.secret.key and record.receipt.key.
            ShareResult result = Client(Success()).Share(new ShareRequest(Secret, 86400));

            Assert.Equal("https://eu.onetimesecret.com/secret/SECRETKEY", result.ShareUrl);
            Assert.Equal("https://eu.onetimesecret.com/receipt/RECEIPTKEY", result.ReceiptUrl);
        }

        [Fact]
        public void Share_HonoursAPathPrefixedBaseUrl()
        {
            ShareResult result = Client(Success(), "https://example.com/ots")
                .Share(new ShareRequest(Secret, 86400));

            Assert.Equal("https://example.com/ots/secret/SECRETKEY", result.ShareUrl);
        }

        [Fact]
        public void Share_PrefersShareDomainOverTheConfiguredBaseUrl()
        {
            var transport = new StubTransport(new HttpResult(200,
                "{\"success\":true,\"record\":{" +
                "\"receipt\":{\"key\":\"RK\",\"secret_ttl\":86400}," +
                "\"secret\":{\"key\":\"SK\",\"secret_ttl\":86400}," +
                "\"share_domain\":\"share.example.com\"}}"));

            ShareResult result = Client(transport).Share(new ShareRequest(Secret, 86400));

            Assert.Equal("https://share.example.com/secret/SK", result.ShareUrl);
            Assert.Equal("https://share.example.com/receipt/RK", result.ReceiptUrl);
        }

        [Fact]
        public void Share_MissingRecordFailsLoudly()
        {
            var transport = new StubTransport(new HttpResult(200, "{\"success\":true}"));
            Assert.Throws<OtsException>(() => Client(transport).Share(new ShareRequest(Secret, 86400)));
        }

        [Fact]
        public void Share_MissingSecretOrReceiptFailsLoudly()
        {
            var transport = new StubTransport(new HttpResult(200,
                "{\"success\":true,\"record\":{\"share_domain\":null}}"));
            Assert.Throws<OtsException>(() => Client(transport).Share(new ShareRequest(Secret, 86400)));
        }

        [Fact]
        public void Share_ZeroesTheRequestBodyBuffer()
        {
            var transport = Success();
            Client(transport).Share(new ShareRequest(Secret, 86400));

            Assert.True(transport.LastBodyBuffer.All(b => b == 0));
        }

        [Fact]
        public void Share_ZeroesTheRequestBodyBufferEvenWhenTheRequestFails()
        {
            var transport = new StubTransport(
                new HttpTransportException(HttpFailureKind.Timeout, "timed out", null));

            Assert.Throws<OtsException>(() => Client(transport).Share(new ShareRequest(Secret, 86400)));
            Assert.True(transport.LastBodyBuffer.All(b => b == 0));
        }

        [Fact]
        public void Share_ReportsTheLifetimeTheServerActuallyApplied()
        {
            // Verified live: v2 anonymous shares clamp to 604800 seconds exactly like v1.
            var transport = new StubTransport(new HttpResult(200,
                "{\"success\":true,\"record\":{" +
                "\"receipt\":{\"key\":\"RK\",\"secret_ttl\":604800}," +
                "\"secret\":{\"key\":\"SK\",\"secret_ttl\":604800},\"share_domain\":null}}"));

            ShareResult result = Client(transport).Share(new ShareRequest(Secret, 31536000));

            Assert.Equal(31536000, result.RequestedTtlSeconds);
            Assert.Equal(604800, result.EffectiveTtlSeconds);
            Assert.True(result.TtlWasShortened);
        }

        [Fact]
        public void Share_TreatsAMissingTtlAsUnknownRatherThanShortened()
        {
            var transport = new StubTransport(new HttpResult(200,
                "{\"success\":true,\"record\":{" +
                "\"receipt\":{\"key\":\"RK\"},\"secret\":{\"key\":\"SK\"},\"share_domain\":null}}"));

            ShareResult result = Client(transport).Share(new ShareRequest(Secret, 31536000));

            Assert.Equal(0, result.EffectiveTtlSeconds);
            Assert.False(result.TtlWasShortened);
        }

        [Fact]
        public void Share_OmitsPassphraseAndRecipientWhenNotSet()
        {
            var transport = Success();
            Client(transport).Share(new ShareRequest(Secret, 86400));

            Assert.DoesNotContain("passphrase", transport.LastBodyText);
            Assert.DoesNotContain("recipient", transport.LastBodyText);
        }

        [Fact]
        public void Share_SendsThePassphraseAndRecipientWhenSet()
        {
            var transport = Success();
            Client(transport).Share(new ShareRequest(Secret, 86400, "Corr3ctHorse", "them@example.com"));

            Assert.Contains("\"passphrase\":\"Corr3ctHorse\"", transport.LastBodyText);
            Assert.Contains("\"recipient\":\"them@example.com\"", transport.LastBodyText);
        }

        [Fact]
        public void Share_KeepsThePassphraseOutOfTheShareUrl()
        {
            ShareResult result = Client(Success())
                .Share(new ShareRequest(Secret, 86400, "Corr3ctHorse", null));

            Assert.DoesNotContain("Corr3ctHorse", result.ShareUrl);
            Assert.DoesNotContain("Corr3ctHorse", result.ReceiptUrl);
        }

        [Fact]
        public void Share_SendsNoAuthorizationHeaderWhenAnonymous()
        {
            var transport = Success();
            new OtsClientV2(new Uri("https://eu.onetimesecret.com"), transport, OtsCredentials.Anonymous)
                .Share(new ShareRequest(Secret, 86400));

            Assert.Null(transport.LastAuthorization);
        }

        [Fact]
        public void Share_SendsBasicAuthWhenCredentialsAreConfigured()
        {
            var transport = Success();
            var credentials = new OtsCredentials("me@example.com", "tok3n");

            new OtsClientV2(new Uri("https://eu.onetimesecret.com"), transport, credentials)
                .Share(new ShareRequest(Secret, 86400));

            Assert.Equal("Basic " + Convert.ToBase64String(
                Encoding.UTF8.GetBytes("me@example.com:tok3n")), transport.LastAuthorization);
        }

        [Fact]
        public void Share_MapsBadCredentialsAs401UnlikeV1()
        {
            // Verified live: unlike v1's 404 quirk, v2 correctly returns 401.
            var transport = new StubTransport(new HttpResult(401,
                "{\"error\":\"Authentication Required\",\"message\":\"[CREDENTIALS_INVALID] Invalid credentials\"}"));

            var ex = Assert.Throws<OtsException>(() =>
                new OtsClientV2(new Uri("https://eu.onetimesecret.com"), transport,
                        new OtsCredentials("me@example.com", "wrong"))
                    .Share(new ShareRequest(Secret, 86400)));

            Assert.Equal(401, ex.StatusCode);
            Assert.Contains("rejected your credentials", ex.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("CREDENTIALS_INVALID", ex.Message);
        }

        [Fact]
        public void Share_DistinguishesAnEntitlementLimitFromRejectedCredentials()
        {
            // Verified live with a real account: requesting a longer-than-default lifetime
            // came back 401 with "...entitlement" in the message - a plan/feature limit, not
            // bad credentials. The blanket "rejected your credentials" wording was actively
            // misleading here and sent the user to check the wrong thing.
            var transport = new StubTransport(new HttpResult(401,
                "{\"error\":\"Authentication Required\"," +
                "\"message\":\"[FEATURE_RESTRICTED] Feature requires extended default expiration entitlement\"}"));

            var ex = Assert.Throws<OtsException>(() =>
                new OtsClientV2(new Uri("https://eu.onetimesecret.com"), transport,
                        new OtsCredentials("me@example.com", "tok3n"))
                    .Share(new ShareRequest(Secret, 2592000)));

            Assert.Contains("plan limitation", ex.Message, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("rejected your credentials", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void Share_PrefersTheMoreSpecificMessageFieldOverError()
        {
            // Verified live: the auth-failure body carries both `error` (generic) and
            // `message` (specific) - the specific one is the more useful thing to show.
            var transport = new StubTransport(new HttpResult(401,
                "{\"error\":\"Authentication Required\",\"message\":\"[CREDENTIALS_INVALID] Invalid credentials\"}"));

            var ex = Assert.Throws<OtsException>(() => Client(transport).Share(new ShareRequest(Secret, 86400)));

            Assert.Contains("[CREDENTIALS_INVALID] Invalid credentials", ex.Message);
        }

        [Fact]
        public void Share_FallsBackToErrorWhenMessageIsAbsent()
        {
            // Verified live: the field-validation error only ever carries `error`, no `message`.
            var transport = new StubTransport(new HttpResult(422,
                "{\"error\":\"You did not provide anything to share\",\"error_type\":\"missing\"," +
                "\"field\":\"secret\",\"request_id\":\"req-1\"}"));

            var ex = Assert.Throws<OtsException>(() => Client(transport).Share(new ShareRequest(Secret, 86400)));

            Assert.Contains("You did not provide anything to share", ex.Message);
            Assert.Equal("req-1", ex.RequestId);
        }

        [Fact]
        public void Share_CallsOutTheAnonymousTtlCapWhenTheServerBlamesTtl()
        {
            var transport = new StubTransport(new HttpResult(422,
                "{\"error\":\"ttl too long\",\"field\":\"ttl\",\"request_id\":\"req-9\"}"));
            var ex = Assert.Throws<OtsException>(() => Client(transport).Share(new ShareRequest(Secret, 86400)));

            Assert.Contains("lifetime", ex.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("ttl too long", ex.Message);
        }

        [Fact]
        public void Share_MapsRateLimiting()
        {
            var transport = new StubTransport(new HttpResult(429, ""));
            var ex = Assert.Throws<OtsException>(() => Client(transport).Share(new ShareRequest(Secret, 86400)));

            Assert.Contains("rate-limit", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void Share_Maps404AsAPossiblyStaleCapabilityDetection()
        {
            var transport = new StubTransport(new HttpResult(404, ""));
            var ex = Assert.Throws<OtsException>(() => Client(transport).Share(new ShareRequest(Secret, 86400)));

            Assert.Equal(404, ex.StatusCode);
        }

        [Fact]
        public void Share_MapsTransportFailures()
        {
            var timeout = new StubTransport(new HttpTransportException(HttpFailureKind.Timeout, "t", null));
            Assert.Contains("timed out",
                Assert.Throws<OtsException>(() => Client(timeout).Share(new ShareRequest(Secret, 86400))).Message,
                StringComparison.OrdinalIgnoreCase);

            var tls = new StubTransport(new HttpTransportException(HttpFailureKind.Tls, "x", null));
            Assert.Contains("certificate",
                Assert.Throws<OtsException>(() => Client(tls).Share(new ShareRequest(Secret, 86400))).Message,
                StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void Share_SurfacesAnUnreadableSuccessBody()
        {
            var transport = new StubTransport(new HttpResult(200, "<html>not json</html>"));
            Assert.Throws<OtsException>(() => Client(transport).Share(new ShareRequest(Secret, 86400)));
        }

        [Fact]
        public void Share_NeverPutsTheSecretOrTokenOrPassphraseInAnErrorMessage()
        {
            var credentials = new OtsCredentials("me@example.com", "sup3r-s3cret-token");
            var cases = new IHttpTransport[]
            {
                new StubTransport(new HttpResult(401, "")),
                new StubTransport(new HttpResult(429, "")),
                new StubTransport(new HttpResult(500, "<html>boom</html>")),
                new StubTransport(new HttpTransportException(HttpFailureKind.Tls, "x", null)),
            };

            foreach (IHttpTransport transport in cases)
            {
                var ex = Assert.ThrowsAny<Exception>(() =>
                    new OtsClientV2(new Uri("https://eu.onetimesecret.com"), transport, credentials)
                        .Share(new ShareRequest(Secret, 86400, "Corr3ctHorse", null)));

                Assert.DoesNotContain("Pässwörd", ex.ToString());
                Assert.DoesNotContain("sup3r-s3cret-token", ex.ToString());
                Assert.DoesNotContain("Corr3ctHorse", ex.ToString());
            }
        }

        [Fact]
        public void Share_LetsCancellationPropagateRatherThanWrappingItAsAnError()
        {
            var transport = StubTransport.Cancelling();

            using (var cts = new CancellationTokenSource())
            {
                Assert.Throws<OperationCanceledException>(() =>
                    Client(transport).Share(new ShareRequest(Secret, 86400), cts.Token));
            }
        }

        [Fact]
        public void Burn_PostsToTheExpectedEndpointWithTheRequiredContinueField()
        {
            // Verified against api.onetimesecret.com's published docs: "continue" is a
            // required body field on all three burn endpoint variants (v1 and v2 alike),
            // undocumented beyond its presence. Sending "{}" - what this client used to do -
            // is the most likely reason every burn attempt reported back "success":false.
            var transport = new StubTransport(new HttpResult(200, "{\"success\":true}"));
            Client(transport).Burn("RECEIPTKEY", CancellationToken.None);

            Assert.Equal("https://eu.onetimesecret.com/api/v2/private/RECEIPTKEY/burn",
                transport.LastUri.AbsoluteUri);
            Assert.Equal("{\"continue\":\"true\"}", transport.LastBodyText);
        }

        [Fact]
        public void Burn_EscapesTheReceiptKeyInTheUrl()
        {
            var transport = new StubTransport(new HttpResult(200, "{\"success\":true}"));
            Client(transport).Burn("has space", CancellationToken.None);

            Assert.Contains(Uri.EscapeDataString("has space"), transport.LastUri.AbsoluteUri);
        }

        [Fact]
        public void Burn_ReturnsBurnedTrueOnSuccess()
        {
            var transport = new StubTransport(new HttpResult(200, "{\"success\":true}"));
            BurnResult result = Client(transport).Burn("RECEIPTKEY", CancellationToken.None);

            Assert.True(result.Burned);
        }

        [Fact]
        public void Burn_ReturnsBurnedFalseWithDetailWhenTheServerDeclines()
        {
            // Captured before the required "continue" body field was added - "success":false
            // came back as an ordinary HTTP 200, which this client reports as data rather
            // than an exception either way.
            var transport = new StubTransport(new HttpResult(200, RealBurnDeclinedPayload));
            BurnResult result = Client(transport).Burn("RECEIPTKEY", CancellationToken.None);

            Assert.False(result.Burned);
            Assert.False(string.IsNullOrEmpty(result.Detail));
        }

        [Fact]
        public void Burn_MapsHttpErrorsLikeShare()
        {
            var transport = new StubTransport(new HttpResult(401, ""));
            var ex = Assert.Throws<OtsException>(() =>
                Client(transport).Burn("RECEIPTKEY", CancellationToken.None));

            Assert.Equal(401, ex.StatusCode);
        }

        [Fact]
        public void Burn_ThrowsOnAnUnreadableBody()
        {
            var transport = new StubTransport(new HttpResult(200, "<html>not json</html>"));
            Assert.Throws<OtsException>(() => Client(transport).Burn("RECEIPTKEY", CancellationToken.None));
        }

        [Fact]
        public void Burn_RejectsAnEmptyReceiptKey()
        {
            var transport = new StubTransport(new HttpResult(200, "{\"success\":true}"));
            Assert.Throws<ArgumentException>(() => Client(transport).Burn("", CancellationToken.None));
        }
    }
}
