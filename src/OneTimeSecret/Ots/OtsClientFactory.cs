using System;
using System.Globalization;
using System.Threading;

namespace OneTimeSecret.Ots
{
    /// <summary>
    /// Where the version cache actually lives - KeePass's CustomConfig - so this stays
    /// free of KeePass references. Implemented in KeePassAdapters.
    /// </summary>
    public interface IApiVersionCache
    {
        /// <summary>
        /// Returns the cached version for <paramref name="cacheKey"/>, or null if nothing
        /// is cached, the cache is for a different key, or it is older than
        /// <paramref name="maxAge"/>.
        /// </summary>
        string TryGetCachedVersion(string cacheKey, TimeSpan maxAge);

        void SetCachedVersion(string cacheKey, string version);
    }

    /// <summary>
    /// Decides v1 vs v2 for a base URL, per SPEC.md section 5: probe once, cache for up to
    /// 7 days, never silently fall back to v1 on an unexpected status.
    /// </summary>
    public static class OtsClientFactory
    {
        public const string VersionV1 = "v1";
        public const string VersionV2 = "v2";

        public static readonly TimeSpan CacheLifetime = TimeSpan.FromDays(7);

        public static string Detect(Uri baseUri, IHttpTransport transport, int timeoutMs,
            IApiVersionCache cache, CancellationToken cancellationToken)
        {
            if (baseUri == null) throw new ArgumentNullException("baseUri");
            if (transport == null) throw new ArgumentNullException("transport");

            // Scheme + host + port + path, not just the host: two path-prefixed instances
            // on one host, or the same host on two ports (the M5 Docker setup does both),
            // are different servers and must not share one cached version. Found by code
            // review - a host-only key would have conflated them.
            string cacheKey = CacheKey(baseUri);

            if (cache != null)
            {
                string cached = cache.TryGetCachedVersion(cacheKey, CacheLifetime);
                if (cached != null) return cached;
            }

            string detected = Probe(baseUri, transport, timeoutMs, cancellationToken);

            if (cache != null) cache.SetCachedVersion(cacheKey, detected);

            return detected;
        }

        /// <summary>
        /// The same normalization <see cref="Detect"/> uses internally, exposed so callers
        /// that need to write the cache themselves (the auto-fallback path, when it decides
        /// a cached v2 was wrong) use an identical key rather than risking drift between two
        /// separately-written normalizations.
        /// </summary>
        public static string CacheKey(Uri baseUri)
        {
            return baseUri.GetLeftPart(UriPartial.Path).TrimEnd('/');
        }

        private static string Probe(Uri baseUri, IHttpTransport transport, int timeoutMs,
            CancellationToken cancellationToken)
        {
            var endpoint = new Uri(baseUri.AbsoluteUri.TrimEnd('/') + "/api/v2/status");

            HttpResult response;
            try
            {
                response = transport.Get(endpoint, null, timeoutMs, cancellationToken);
            }
            catch (HttpTransportException ex)
            {
                throw OtsErrorTranslator.TranslateTransportFailure(ex);
            }

            if (response.StatusCode == 200) return VersionV2;
            if (response.StatusCode == 404) return VersionV1;

            // SPEC.md section 5: "else -> surface the error, do not silently fall back".
            // Guessing v1 here would mask a real outage or a misconfigured base URL as a
            // successful, quietly-degraded share.
            throw new OtsException(
                "Could not determine which OneTimeSecret API version this server supports " +
                "(unexpected status " + response.StatusCode.ToString(CultureInfo.InvariantCulture) +
                " from the capability check).", response.StatusCode, null, null);
        }
    }
}
