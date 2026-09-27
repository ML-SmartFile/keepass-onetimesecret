using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

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
        private readonly int m_timeoutMs;

        public OtsClientV1(Uri baseUri, IHttpTransport transport)
            : this(baseUri, transport, DefaultTimeoutMs) { }

        public OtsClientV1(Uri baseUri, IHttpTransport transport, int timeoutMs)
        {
            if (baseUri == null) throw new ArgumentNullException("baseUri");
            if (transport == null) throw new ArgumentNullException("transport");

            m_baseUri = baseUri;
            m_transport = transport;
            m_timeoutMs = timeoutMs;
        }

        public ShareResult Share(ShareRequest request)
        {
            if (request == null) throw new ArgumentNullException("request");

            var endpoint = new Uri(m_baseUri.AbsoluteUri.TrimEnd('/') + "/api/v1/share");

            byte[] body = null;
            HttpResult response;
            try
            {
                body = BuildFormBody(request);
                response = m_transport.Post(
                    endpoint, "application/x-www-form-urlencoded", body, m_timeoutMs);
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

            if (!response.IsSuccess) throw TranslateHttpError(response);

            return ParseShareResponse(response.Body);
        }

        private static byte[] BuildFormBody(ShareRequest request)
        {
            var sb = new StringBuilder();
            sb.Append("secret=").Append(Uri.EscapeDataString(request.Secret));
            sb.Append("&ttl=").Append(request.TtlSeconds.ToString(CultureInfo.InvariantCulture));
            return Encoding.UTF8.GetBytes(sb.ToString());
        }

        private ShareResult ParseShareResponse(string responseBody)
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

            return new ShareResult(
                secretKey,
                metadataKey,
                root + "/secret/" + secretKey,
                receiptUrl);
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

        private static OtsException TranslateHttpError(HttpResult response)
        {
            string serverMessage, errorField, requestId;
            ReadErrorBody(response.Body, out serverMessage, out errorField, out requestId);

            string message;
            switch (response.StatusCode)
            {
                case 401:
                case 403:
                    message = "OneTimeSecret rejected the request. Check your OneTimeSecret credentials.";
                    break;

                case 429:
                    message = "OneTimeSecret rate-limited this request. Anonymous shares have a lower " +
                              "limit - configuring an API token raises it.";
                    break;

                case 400:
                case 422:
                    message = "OneTimeSecret rejected the request.";
                    if (string.Equals(errorField, "ttl", StringComparison.OrdinalIgnoreCase))
                        message += " The requested lifetime is too long; anonymous shares have a capped TTL.";
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
