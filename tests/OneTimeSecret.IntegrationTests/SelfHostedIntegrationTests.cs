using System;
using System.Net;
using System.Threading;
using OneTimeSecret.Ots;
using Xunit;

namespace OneTimeSecret.IntegrationTests
{
    /// <summary>
    /// Hits a real OneTimeSecret instance. Gated behind OTS_TEST_BASE_URL per SPEC.md
    /// section 9 - every test returns immediately, passing trivially, when it is unset.
    /// See README.md for how to bring up a local instance and set the environment.
    ///
    /// OTS_TEST_USER / OTS_TEST_TOKEN are optional: unset, only the anonymous-share half
    /// of each test runs - burning needs an owning account, per SPEC.md section 5.
    /// </summary>
    public class SelfHostedIntegrationTests
    {
        static SelfHostedIntegrationTests()
        {
            // The plugin calls this once at Initialize(); a standalone test process never
            // does, so it is called here instead - otherwise a TLS negotiation failure
            // could masquerade as whatever is actually being tested.
            HttpTransport.EnableModernTls();
        }

        private static string BaseUrl { get { return Environment.GetEnvironmentVariable("OTS_TEST_BASE_URL"); } }
        private static string User { get { return Environment.GetEnvironmentVariable("OTS_TEST_USER"); } }
        private static string Token { get { return Environment.GetEnvironmentVariable("OTS_TEST_TOKEN"); } }

        private static bool HasCredentials { get { return !string.IsNullOrEmpty(User) && !string.IsNullOrEmpty(Token); } }

        private static OtsCredentials Credentials
        {
            get { return HasCredentials ? new OtsCredentials(User, Token) : OtsCredentials.Anonymous; }
        }

        [Fact]
        public void ShareRevealBurn_FullCycle_V1()
        {
            if (string.IsNullOrEmpty(BaseUrl)) return;

            var client = new OtsClientV1(new Uri(BaseUrl), new HttpTransport(), Credentials);
            RunFullCycle(client);
        }

        [Fact]
        public void ShareRevealBurn_FullCycle_V2()
        {
            if (string.IsNullOrEmpty(BaseUrl)) return;

            var client = new OtsClientV2(new Uri(BaseUrl), new HttpTransport(), Credentials);
            RunFullCycle(client);
        }

        private static void RunFullCycle(IOtsClient client)
        {
            ShareResult result = client.Share(new ShareRequest("integration-test-" + Guid.NewGuid(), 300));

            Assert.False(string.IsNullOrEmpty(result.ShareUrl));
            Assert.False(string.IsNullOrEmpty(result.ReceiptUrl));

            // Reveal: the link must actually resolve, not just look plausible. A plain GET
            // of the confirmation page - actually decrypting the secret is a separate,
            // explicit step the real client always takes through the browser.
            var request = (HttpWebRequest)WebRequest.Create(result.ShareUrl);
            using (var response = (HttpWebResponse)request.GetResponse())
            {
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            }

            if (!HasCredentials) return;

            BurnResult burn = client.Burn(result.MetadataKey, CancellationToken.None);
            Assert.True(burn.Burned, burn.Detail);
        }

        [Fact]
        public void CapabilityDetection_MatchesWhatTheInstanceOffers()
        {
            if (string.IsNullOrEmpty(BaseUrl)) return;

            string version = OtsClientFactory.Detect(
                new Uri(BaseUrl), new HttpTransport(), OtsClientV1.DefaultTimeoutMs, null, CancellationToken.None);

            Assert.True(version == OtsClientFactory.VersionV1 || version == OtsClientFactory.VersionV2);
        }
    }

    /// <summary>
    /// Gated behind OTS_TEST_PATHPREFIX_URL - a base URL with a path prefix (e.g.
    /// http://localhost:8080/ots), kept on its own plain-HTTP port in docker/Caddyfile so
    /// this is isolated from the self-signed-certificate case below. UrlPolicy and the
    /// client URL construction already have unit coverage for a path prefix (since M1);
    /// this proves it against a real reverse proxy that actually strips the prefix,
    /// which a unit test cannot.
    /// </summary>
    public class PathPrefixedBaseUrlTests
    {
        static PathPrefixedBaseUrlTests()
        {
            HttpTransport.EnableModernTls();
        }

        private static string PathPrefixUrl
        {
            get { return Environment.GetEnvironmentVariable("OTS_TEST_PATHPREFIX_URL"); }
        }

        [Fact]
        public void Share_WorksAgainstAPathPrefixedBaseUrl()
        {
            if (string.IsNullOrEmpty(PathPrefixUrl)) return;

            var client = new OtsClientV2(new Uri(PathPrefixUrl), new HttpTransport());
            ShareResult result = client.Share(new ShareRequest("integration-test-" + Guid.NewGuid(), 300));

            Assert.StartsWith(PathPrefixUrl, result.ShareUrl);

            var request = (HttpWebRequest)WebRequest.Create(result.ShareUrl);
            using (var response = (HttpWebResponse)request.GetResponse())
            {
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            }
        }
    }

    /// <summary>
    /// Gated behind OTS_TEST_SELFSIGNED_URL - kept separate from OTS_TEST_BASE_URL so the
    /// full-cycle tests above can run against a plain-HTTP local instance with no
    /// certificate involved at all. See README.md.
    /// </summary>
    public class SelfSignedCertificateTests
    {
        static SelfSignedCertificateTests()
        {
            HttpTransport.EnableModernTls();
        }

        private static string SelfSignedUrl
        {
            get { return Environment.GetEnvironmentVariable("OTS_TEST_SELFSIGNED_URL"); }
        }

        [Fact]
        public void SelfSignedCertificate_IsRejectedWithNoOptionToProceed()
        {
            if (string.IsNullOrEmpty(SelfSignedUrl)) return;

            var client = new OtsClientV1(new Uri(SelfSignedUrl), new HttpTransport());

            // Non-negotiable #6: never offer to bypass certificate validation. Exercising
            // the real network stack against a genuinely untrusted certificate proves the
            // absence of a bypass, rather than just reading the source and trusting it.
            var ex = Assert.Throws<OtsException>(() =>
                client.Share(new ShareRequest("integration-test-" + Guid.NewGuid(), 300)));

            Assert.Contains("certificate", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
    }
}
