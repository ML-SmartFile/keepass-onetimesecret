using System;
using System.Threading;
using OneTimeSecret.Ots;
using Xunit;

namespace OneTimeSecret.Tests
{
    public class OtsClientFactoryTests
    {
        private sealed class StubTransport : IHttpTransport
        {
            private readonly HttpResult m_result;
            private readonly HttpTransportException m_failure;

            public StubTransport(HttpResult result) { m_result = result; }
            public StubTransport(HttpTransportException failure) { m_failure = failure; }

            public Uri LastGetUri { get; private set; }
            public int CallCount { get; private set; }

            public HttpResult Get(Uri uri, string authorizationHeader, int timeoutMs,
                CancellationToken cancellationToken)
            {
                CallCount++;
                LastGetUri = uri;
                if (m_failure != null) throw m_failure;
                return m_result;
            }

            public HttpResult Post(Uri uri, string contentType, byte[] body, string authorizationHeader,
                int timeoutMs, CancellationToken cancellationToken)
            {
                throw new NotSupportedException();
            }
        }

        private sealed class FakeCache : IApiVersionCache
        {
            public string CachedVersion;
            public string CachedHost;
            public DateTime CachedAtUtc;
            public bool Fresh;

            public string TryGetCachedVersion(string host, TimeSpan maxAge)
            {
                if (CachedVersion == null) return null;
                if (!string.Equals(CachedHost, host, StringComparison.OrdinalIgnoreCase)) return null;
                return Fresh ? CachedVersion : null;
            }

            public void SetCachedVersion(string host, string version)
            {
                CachedHost = host;
                CachedVersion = version;
                CachedAtUtc = DateTime.UtcNow;
                Fresh = true;
            }
        }

        [Fact]
        public void Detect_ReturnsV2On200()
        {
            var transport = new StubTransport(new HttpResult(200, "{\"success\":true,\"status\":\"nominal\"}"));

            string version = OtsClientFactory.Detect(
                new Uri("https://eu.onetimesecret.com"), transport, 1000, null, CancellationToken.None);

            Assert.Equal(OtsClientFactory.VersionV2, version);
            Assert.Equal("https://eu.onetimesecret.com/api/v2/status", transport.LastGetUri.AbsoluteUri);
        }

        [Fact]
        public void Detect_ReturnsV1On404()
        {
            var transport = new StubTransport(new HttpResult(404, ""));

            string version = OtsClientFactory.Detect(
                new Uri("https://ots.internal"), transport, 1000, null, CancellationToken.None);

            Assert.Equal(OtsClientFactory.VersionV1, version);
        }

        [Fact]
        public void Detect_ThrowsOnAnUnexpectedStatusRatherThanSilentlyFallingBack()
        {
            var transport = new StubTransport(new HttpResult(500, ""));

            var ex = Assert.Throws<OtsException>(() => OtsClientFactory.Detect(
                new Uri("https://eu.onetimesecret.com"), transport, 1000, null, CancellationToken.None));

            Assert.Equal(500, ex.StatusCode);
        }

        [Fact]
        public void Detect_MapsTransportFailures()
        {
            var transport = new StubTransport(new HttpTransportException(HttpFailureKind.Timeout, "t", null));

            Assert.Throws<OtsException>(() => OtsClientFactory.Detect(
                new Uri("https://eu.onetimesecret.com"), transport, 1000, null, CancellationToken.None));
        }

        [Fact]
        public void Detect_UsesTheCachedValueWithoutProbingWhenFresh()
        {
            var transport = new StubTransport(new HttpResult(200, "{}"));
            var cache = new FakeCache();
            cache.SetCachedVersion("eu.onetimesecret.com", OtsClientFactory.VersionV1);

            string version = OtsClientFactory.Detect(
                new Uri("https://eu.onetimesecret.com"), transport, 1000, cache, CancellationToken.None);

            Assert.Equal(OtsClientFactory.VersionV1, version);
            Assert.Equal(0, transport.CallCount);
        }

        [Fact]
        public void Detect_ProbesAgainWhenTheCacheIsForADifferentHost()
        {
            var transport = new StubTransport(new HttpResult(200, "{}"));
            var cache = new FakeCache();
            cache.SetCachedVersion("other.example.com", OtsClientFactory.VersionV1);

            string version = OtsClientFactory.Detect(
                new Uri("https://eu.onetimesecret.com"), transport, 1000, cache, CancellationToken.None);

            Assert.Equal(OtsClientFactory.VersionV2, version);
            Assert.Equal(1, transport.CallCount);
        }

        [Fact]
        public void Detect_ProbesAgainWhenTheCacheIsStale()
        {
            var transport = new StubTransport(new HttpResult(200, "{}"));
            var cache = new FakeCache();
            cache.SetCachedVersion("eu.onetimesecret.com", OtsClientFactory.VersionV1);
            cache.Fresh = false; // simulates older than the 7-day cache lifetime

            string version = OtsClientFactory.Detect(
                new Uri("https://eu.onetimesecret.com"), transport, 1000, cache, CancellationToken.None);

            Assert.Equal(OtsClientFactory.VersionV2, version);
            Assert.Equal(1, transport.CallCount);
        }

        [Fact]
        public void Detect_WritesTheResultToCacheAfterProbing()
        {
            var transport = new StubTransport(new HttpResult(200, "{}"));
            var cache = new FakeCache();

            OtsClientFactory.Detect(
                new Uri("https://eu.onetimesecret.com"), transport, 1000, cache, CancellationToken.None);

            Assert.Equal(OtsClientFactory.VersionV2, cache.CachedVersion);
            Assert.Equal("eu.onetimesecret.com", cache.CachedHost);
        }

        [Fact]
        public void Detect_DoesNotWriteToCacheWhenDetectionFails()
        {
            var transport = new StubTransport(new HttpResult(500, ""));
            var cache = new FakeCache();

            Assert.Throws<OtsException>(() => OtsClientFactory.Detect(
                new Uri("https://eu.onetimesecret.com"), transport, 1000, cache, CancellationToken.None));

            Assert.Null(cache.CachedVersion);
        }
    }

    public class AutoFallbackOtsClientTests
    {
        private sealed class StubClient : IOtsClient
        {
            private readonly ShareResult m_result;
            private readonly OtsException m_failure;

            public StubClient(ShareResult result) { m_result = result; }
            public StubClient(OtsException failure) { m_failure = failure; }

            public bool WasCalled { get; private set; }

            public ShareResult Share(ShareRequest request) { return Share(request, CancellationToken.None); }

            public ShareResult Share(ShareRequest request, CancellationToken cancellationToken)
            {
                WasCalled = true;
                if (m_failure != null) throw m_failure;
                return m_result;
            }

            public BurnResult Burn(string receiptKey, CancellationToken cancellationToken)
            {
                WasCalled = true;
                if (m_failure != null) throw m_failure;
                return new BurnResult(true, null);
            }
        }

        private static ShareResult FakeResult()
        {
            return new ShareResult("SK", "MK", "https://x/secret/SK", "https://x/receipt/MK", 86400, 86400);
        }

        [Fact]
        public void Share_UsesThePrimaryClientWhenItSucceeds()
        {
            var primary = new StubClient(FakeResult());
            var fallback = new StubClient(FakeResult());

            var client = new AutoFallbackOtsClient(primary, () => fallback, null);
            ShareResult result = client.Share(new ShareRequest("s", 86400));

            Assert.Equal("SK", result.SecretKey);
            Assert.False(fallback.WasCalled);
        }

        [Fact]
        public void Share_FallsBackToV1OnceOnA404()
        {
            var primary = new StubClient(new OtsException("stale", 404, null, null));
            var fallback = new StubClient(FakeResult());

            var client = new AutoFallbackOtsClient(primary, () => fallback, null);
            ShareResult result = client.Share(new ShareRequest("s", 86400));

            Assert.True(fallback.WasCalled);
            Assert.Equal("SK", result.SecretKey);
        }

        [Fact]
        public void Share_InvokesTheFallbackCallback()
        {
            var primary = new StubClient(new OtsException("stale", 404, null, null));
            var fallback = new StubClient(FakeResult());
            bool called = false;

            var client = new AutoFallbackOtsClient(primary, () => fallback, () => called = true);
            client.Share(new ShareRequest("s", 86400));

            Assert.True(called);
        }

        [Fact]
        public void Share_RethrowsNonNotFoundErrorsWithoutFallingBack()
        {
            var primary = new StubClient(new OtsException("bad creds", 401, null, null));
            var fallback = new StubClient(FakeResult());

            var client = new AutoFallbackOtsClient(primary, () => fallback, null);

            var ex = Assert.Throws<OtsException>(() => client.Share(new ShareRequest("s", 86400)));
            Assert.Equal(401, ex.StatusCode);
            Assert.False(fallback.WasCalled);
        }

        [Fact]
        public void Burn_FallsBackToV1OnceOnA404()
        {
            // v1 has its own (deprecated but present) burn endpoint - verified against
            // api.onetimesecret.com's docs - so this follows the same fallback policy as Share.
            var primary = new StubClient(new OtsException("stale", 404, null, null));
            var fallback = new StubClient(FakeResult());

            var client = new AutoFallbackOtsClient(primary, () => fallback, null);
            client.Burn("RK", CancellationToken.None);

            Assert.True(fallback.WasCalled);
        }

        [Fact]
        public void Burn_RethrowsNonNotFoundErrorsWithoutFallingBack()
        {
            var primary = new StubClient(new OtsException("bad creds", 401, null, null));
            var fallback = new StubClient(FakeResult());

            var client = new AutoFallbackOtsClient(primary, () => fallback, null);

            var ex = Assert.Throws<OtsException>(() => client.Burn("RK", CancellationToken.None));
            Assert.Equal(401, ex.StatusCode);
            Assert.False(fallback.WasCalled);
        }
    }
}
