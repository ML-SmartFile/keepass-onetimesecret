using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading;

namespace OneTimeSecret.Ots
{
    /// <summary>
    /// The legacy v1 API, supported by every self-hosted instance ever deployed.
    /// M1 scope: anonymous shares only.
    /// </summary>
    public sealed class OtsClientV1 : IOtsClient
    {
        public const int DefaultTimeoutMs = 20000;

        private readonly Uri m_baseUri;
        private readonly IHttpTransport m_transport;
        private readonly OtsCredentials m_credentials;
        private readonly int m_timeoutMs;

        public OtsClientV1(Uri baseUri, IHttpTransport transport)
            : this(baseUri, transport, OtsCredentials.Anonymous, DefaultTimeoutMs) { }

        public OtsClientV1(Uri baseUri, IHttpTransport transport, OtsCredentials credentials)
            : this(baseUri, transport, credentials, DefaultTimeoutMs) { }

        public OtsClientV1(Uri baseUri, IHttpTransport transport, OtsCredentials credentials, int timeoutMs)
        {
            if (baseUri == null) throw new ArgumentNullException("baseUri");
            if (transport == null) throw new ArgumentNullException("transport");

            m_baseUri = baseUri;
            m_transport = transport;
            m_credentials = credentials ?? OtsCredentials.Anonymous;
            m_timeoutMs = timeoutMs;
        }

        public ShareResult Share(ShareRequest request)
        {
            return Share(request, CancellationToken.None);
        }

        public ShareResult Share(ShareRequest request, CancellationToken cancellationToken)
        {
            if (request == null) throw new ArgumentNullException("request");

            var endpoint = new Uri(m_baseUri.AbsoluteUri.TrimEnd('/') + "/api/v1/share");

            byte[] body = null;
            HttpResult response;
            try
            {
                body = BuildFormBody(request);
                response = m_transport.Post(
                    endpoint, "application/x-www-form-urlencoded", body,
                    BasicAuth.Header(m_credentials), m_timeoutMs, cancellationToken);
            }
            catch (HttpTransportException ex)
            {
                throw Translate(ex);
            }
            finally
            {
                // The composed secret also lives in an immutable string we cannot erase;
                // that limit is documented rather than pretended away. Zero what we can.
                if (body != null) Array.Clear(body, 0, body.Length);
            }

            if (!response.IsSuccess) throw TranslateHttpError(response, m_credentials.IsAnonymous);

            return ParseShareResponse(response.Body, request.TtlSeconds);
        }

        private static byte[] BuildFormBody(ShareRequest request)
        {
            var sb = new StringBuilder();
            sb.Append("secret=").Append(Uri.EscapeDataString(request.Secret));
            sb.Append("&ttl=").Append(request.TtlSeconds.ToString(CultureInfo.InvariantCulture));

            if (!string.IsNullOrEmpty(request.Passphrase))
                sb.Append("&passphrase=").Append(Uri.EscapeDataString(request.Passphrase));

            if (!string.IsNullOrEmpty(request.Recipient))
                sb.Append("&recipient=").Append(Uri.EscapeDataString(request.Recipient));

            return Encoding.UTF8.GetBytes(sb.ToString());
        }

        private ShareResult ParseShareResponse(string responseBody, int requestedTtlSeconds)
        {
            Dictionary<string, object> obj;
            try
            {
                obj = Json.ParseObject(responseBody);
            }
            catch (JsonException ex)
            {
                throw new OtsException(
                    "The server returned a response this plugin could not read. " + ex.Message, 200, null, ex);
            }

            string secretKey = Json.GetString(obj, "secret_key");
            string metadataKey = Json.GetString(obj, "metadata_key");

            string root = m_baseUri.AbsoluteUri.TrimEnd('/');

            // Prefer the URL the server hands back. The hosted service moved this page from
            // /private/ to /receipt/, and only the server knows the right host once
            // share_domain is configured - constructing it ourselves produced a 404.
            // Older self-hosted instances return neither field, hence the legacy fallback.
            string receiptUrl =
                Json.GetStringOrNull(obj, "receipt_url") ??
                Json.GetStringOrNull(obj, "metadata_url") ??
                (root + "/private/" + metadataKey);

            // The server reports the lifetime it actually applied. It silently clamps rather
            // than rejecting an over-long request, so reading this back is the only way to
            // avoid telling the user their link lives longer than it does.
            int effectiveTtl = Json.GetIntOrNull(obj, "secret_ttl")
                ?? Json.GetIntOrNull(obj, "ttl")
                ?? 0;

            return new ShareResult(
                secretKey,
                metadataKey,
                root + "/secret/" + secretKey,
                receiptUrl,
                requestedTtlSeconds,
                effectiveTtl);
        }

        private static OtsException Translate(HttpTransportException ex)
        {
            switch (ex.Kind)
            {
                case HttpFailureKind.Timeout:
                    return new OtsException(
                        "The request to OneTimeSecret timed out. Nothing was shared.", 0, null, ex);

                case HttpFailureKind.NameResolution:
                    return new OtsException(
                        "Could not reach OneTimeSecret: the server name could not be resolved. " +
                        "Check the base URL and your network connection.", 0, null, ex);

                case HttpFailureKind.Tls:
                    return new OtsException(
                        "The secure connection to OneTimeSecret failed: the server's certificate " +
                        "was not trusted. The secret was not sent.", 0, null, ex);

                default:
                    return new OtsException(
                        "Could not reach OneTimeSecret. " + ex.Message, 0, null, ex);
            }
        }

        private static OtsException TranslateHttpError(HttpResult response, bool isAnonymous)
        {
            string serverMessage, errorField, requestId;
            ReadErrorBody(response.Body, out serverMessage, out errorField, out requestId);

            string message;
            switch (response.StatusCode)
            {
                case 401:
                case 403:
                    message = isAnonymous
                        ? "OneTimeSecret refused an anonymous share. Configure an account and " +
                          "API token in the OneTimeSecret options."
                        : "OneTimeSecret rejected your credentials. Check the account and API " +
                          "token in the OneTimeSecret options.";
                    break;

                case 404:
                    // Verified against eu.onetimesecret.com: invalid credentials come back as
                    // 404, not 401/403, and the body is sometimes empty and sometimes carries
                    // "Not authorized" - so this must not depend on the body. A 404 here is
                    // equally consistent with a base URL that has no v1 API, hence both causes.
                    message = isAnonymous
                        ? "OneTimeSecret could not find the share endpoint. Check the base URL in " +
                          "the OneTimeSecret options - this instance may not offer the v1 API."
                        : "OneTimeSecret could not find the share endpoint. This usually means the " +
                          "account or API token was rejected, but it can also mean the base URL is " +
                          "wrong. Check both in the OneTimeSecret options.";
                    break;

                case 429:
                    message = "OneTimeSecret rate-limited this request.";
                    if (isAnonymous)
                        message += " Anonymous shares have a lower limit - configuring an API " +
                                   "token in the options raises it.";
                    break;

                case 400:
                case 422:
                    message = "OneTimeSecret rejected the request.";
                    if (string.Equals(errorField, "ttl", StringComparison.OrdinalIgnoreCase))
                    {
                        message += " The requested lifetime is too long";
                        message += isAnonymous
                            ? "; anonymous shares have a capped TTL."
                            : " for this account.";
                    }
                    break;

                default:
                    message = "OneTimeSecret returned HTTP " +
                              response.StatusCode.ToString(CultureInfo.InvariantCulture) + ".";
                    break;
            }

            if (!string.IsNullOrEmpty(serverMessage))
                message += " Server said: " + serverMessage;

            if (!string.IsNullOrEmpty(requestId))
                message += " (request id " + requestId + ")";

            return new OtsException(message, response.StatusCode, requestId, null);
        }

        /// <summary>
        /// Best effort: an error body may be JSON, HTML, or empty. Never throws - a failure
        /// to parse the error must not replace the real error with a parser error.
        /// </summary>
        private static void ReadErrorBody(string body, out string message, out string field, out string requestId)
        {
            message = null;
            field = null;
            requestId = null;

            if (string.IsNullOrEmpty(body)) return;

            try
            {
                var obj = Json.ParseObject(body);
                message = Json.GetStringOrNull(obj, "message") ?? Json.GetStringOrNull(obj, "error");
                field = Json.GetStringOrNull(obj, "field");
                requestId = Json.GetStringOrNull(obj, "request_id");
            }
            catch (JsonException)
            {
                // Not JSON. Leave everything null rather than echoing an HTML error page.
            }
        }
    }
}
