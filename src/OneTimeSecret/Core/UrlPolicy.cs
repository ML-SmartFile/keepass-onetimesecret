using System;
using System.Net;

namespace OneTimeSecret.Core
{
    /// <summary>Validates and normalizes the OneTimeSecret base URL.</summary>
    public static class UrlPolicy
    {
        /// <summary>
        /// Returns the normalized base URI, or throws <see cref="UrlPolicyException"/>.
        /// Plain http is refused unless the host is loopback AND allowInsecureHttp is set.
        /// </summary>
        public static Uri Validate(string baseUrl, bool allowInsecureHttp)
        {
            if (string.IsNullOrEmpty(baseUrl) || baseUrl.Trim().Length == 0)
                throw new UrlPolicyException("No base URL configured.");

            Uri uri;
            if (!Uri.TryCreate(baseUrl.Trim(), UriKind.Absolute, out uri))
                throw new UrlPolicyException("The base URL is not a valid absolute URL.");

            bool isHttps = string.Equals(uri.Scheme, "https", StringComparison.OrdinalIgnoreCase);
            bool isHttp = string.Equals(uri.Scheme, "http", StringComparison.OrdinalIgnoreCase);

            if (!isHttps && !isHttp)
                throw new UrlPolicyException("The base URL must use https (or http on loopback). Got: " + uri.Scheme);

            if (string.IsNullOrEmpty(uri.Host))
                throw new UrlPolicyException("The base URL has no host.");

            if (isHttp)
            {
                if (!IsLoopback(uri))
                    throw new UrlPolicyException(
                        "Refusing to send a secret over plain http to a non-loopback host. Use https.");

                if (!allowInsecureHttp)
                    throw new UrlPolicyException(
                        "Plain http to loopback requires the 'allow insecure http' option to be enabled.");
            }

            return Normalize(uri);
        }

        /// <summary>
        /// Loopback is decided from the literal host only - no DNS resolution, so this stays
        /// deterministic offline and a hostname that merely points at 127.0.0.1 is refused.
        /// </summary>
        public static bool IsLoopback(Uri uri)
        {
            if (uri == null) return false;

            string host = uri.Host;
            if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase))
                return true;

            IPAddress address;
            if (IPAddress.TryParse(host, out address))
                return IPAddress.IsLoopback(address);

            return false;
        }

        /// <summary>Strips trailing slashes from the path so callers can append "/api/v1/...".</summary>
        private static Uri Normalize(Uri uri)
        {
            var builder = new UriBuilder(uri);
            builder.Path = (builder.Path ?? string.Empty).TrimEnd('/');
            builder.Query = string.Empty;
            builder.Fragment = string.Empty;
            return builder.Uri;
        }
    }

    public sealed class UrlPolicyException : Exception
    {
        public UrlPolicyException(string message) : base(message) { }
    }
}
