using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading;

namespace OneTimeSecret.Ots
{
    /// <summary>
    /// The current stable API (M4 scope: share and burn).
    ///
    /// The request/response shapes here were corrected against a live call to
    /// eu.onetimesecret.com in September 2026 - see SPEC.md section 5 for what the
    /// published OpenAPI spec got wrong. In particular, unlike v1, the conceal response
    /// hands back no ready-made URLs at all - both the share link and the receipt link are
    /// constructed here from the bare keys the response does carry.
    /// </summary>
    public sealed class OtsClientV2 : IOtsClient
    {
        public const int DefaultTimeoutMs = 20000;

        private readonly Uri m_baseUri;
        private readonly IHttpTransport m_transport;
        private readonly OtsCredentials m_credentials;
        private readonly int m_timeoutMs;

        public OtsClientV2(Uri baseUri, IHttpTransport transport)
            : this(baseUri, transport, OtsCredentials.Anonymous, DefaultTimeoutMs) { }

        public OtsClientV2(Uri baseUri, IHttpTransport transport, OtsCredentials credentials)
            : this(baseUri, transport, credentials, DefaultTimeoutMs) { }

        public OtsClientV2(Uri baseUri, IHttpTransport transport, OtsCredentials credentials, int timeoutMs)
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

            var endpoint = new Uri(m_baseUri.AbsoluteUri.TrimEnd('/') + "/api/v2/secret/conceal");

            byte[] body = null;
            HttpResult response;
            try
            {
                body = BuildJsonBody(request);
                response = m_transport.Post(
                    endpoint, "application/json", body,
                    BasicAuth.Header(m_credentials), m_timeoutMs, cancellationToken);
            }
            catch (HttpTransportException ex)
            {
                throw OtsErrorTranslator.TranslateTransportFailure(ex);
            }
            finally
            {
                // Same limit as v1: the composed secret also lives in an immutable string we
                // cannot erase. Zero what we can.
                if (body != null) Array.Clear(body, 0, body.Length);
            }

            if (!response.IsSuccess) throw TranslateHttpError(response, m_credentials.IsAnonymous);

            return ParseShareResponse(response.Body, request.TtlSeconds);
        }

        public BurnResult Burn(string receiptKey, string passphrase, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(receiptKey)) throw new ArgumentException("receiptKey is required.", "receiptKey");

            var endpoint = new Uri(m_baseUri.AbsoluteUri.TrimEnd('/') +
                "/api/v2/private/" + Uri.EscapeDataString(receiptKey) + "/burn");

            HttpResult response;
            try
            {
                // Verified against api.onetimesecret.com's published docs (not the OpenAPI
                // guess this endpoint was originally built from): "continue" is a required
                // body field with no documented meaning beyond its presence. Omitting it -
                // as this client did until now - is very likely why every burn attempt came
                // back HTTP 200 with "success":false, authenticated or not.
                //
                // The operation's own description text (not its formal parameter schema,
                // which omits this) reads "Requires the receipt identifier and a passphrase
                // if one was set" - confirmed by testing: burning a secret shared with a
                // PassphraseMode.Entry passphrase was rejected with a 4xx naming the
                // passphrase, until it was included here too.
                var sb = new StringBuilder();
                sb.Append("{\"continue\":\"true\"");
                if (!string.IsNullOrEmpty(passphrase))
                {
                    sb.Append(",\"passphrase\":");
                    AppendJsonString(sb, passphrase);
                }
                sb.Append('}');

                response = m_transport.Post(
                    endpoint, "application/json", Encoding.UTF8.GetBytes(sb.ToString()),
                    BasicAuth.Header(m_credentials), m_timeoutMs, cancellationToken);
            }
            catch (HttpTransportException ex)
            {
                throw OtsErrorTranslator.TranslateTransportFailure(ex);
            }

            if (!response.IsSuccess) throw TranslateHttpError(response, m_credentials.IsAnonymous);

            Dictionary<string, object> obj;
            try
            {
                obj = Json.ParseObject(response.Body);
            }
            catch (JsonException ex)
            {
                throw new OtsException(
                    "The server returned a response this plugin could not read. " + ex.Message, 200, null, ex);
            }

            object successValue;
            bool burned = obj.TryGetValue("success", out successValue) && successValue is bool && (bool)successValue;

            // The API returns HTTP 200 with "success":false rather than an error when it
            // declines to burn, so this is reported back as data, not thrown - it is not a
            // failure of the request itself. Earlier testing without the required "continue"
            // body field (see the request above) got this response unconditionally,
            // authenticated or not; with that field present the cause of a genuine decline
            // is not yet known and is deliberately left unguessed rather than asserted.
            string detail = burned
                ? null
                : "OneTimeSecret did not confirm the secret was burned.";

            return new BurnResult(burned, detail);
        }

        private static byte[] BuildJsonBody(ShareRequest request)
        {
            var sb = new StringBuilder();
            sb.Append("{\"secret\":{\"kind\":\"conceal\",\"secret\":");
            AppendJsonString(sb, request.Secret);
            sb.Append(",\"ttl\":\"").Append(request.TtlSeconds.ToString(CultureInfo.InvariantCulture)).Append('"');

            if (!string.IsNullOrEmpty(request.Passphrase))
            {
                sb.Append(",\"passphrase\":");
                AppendJsonString(sb, request.Passphrase);
            }

            if (!string.IsNullOrEmpty(request.Recipient))
            {
                sb.Append(",\"recipient\":");
                AppendJsonString(sb, request.Recipient);
            }

            sb.Append("}}");
            return Encoding.UTF8.GetBytes(sb.ToString());
        }

        private static void AppendJsonString(StringBuilder sb, string value)
        {
            sb.Append('"');
            foreach (char c in value)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < ' ')
                            sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else
                            sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
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

            Dictionary<string, object> record = Json.GetObjectOrNull(obj, "record");
            if (record == null)
                throw new OtsException(
                    "The server response was missing the expected 'record' field.", 200, null, null);

            Dictionary<string, object> secret = Json.GetObjectOrNull(record, "secret");
            Dictionary<string, object> receipt = Json.GetObjectOrNull(record, "receipt");
            if (secret == null || receipt == null)
                throw new OtsException(
                    "The server response was missing the expected secret or receipt data.", 200, null, null);

            string secretKey = Json.GetString(secret, "key");
            string receiptKey = Json.GetString(receipt, "key");

            // Verified against eu.onetimesecret.com: the conceal response gives us nothing
            // but bare keys, unlike v1's ready-made receipt_url/metadata_url - only the
            // burn endpoint's response carries full URLs, and only after the fact. Both
            // links are built from the same {host}/secret/{key} and {host}/receipt/{key}
            // pattern the site itself uses, confirmed by actually opening the constructed
            // share link. share_domain, when set, is a bare host - only the server knows
            // the right one, so it always wins over the configured base URL.
            string shareDomain = Json.GetStringOrNull(record, "share_domain");
            string root = string.IsNullOrEmpty(shareDomain)
                ? m_baseUri.AbsoluteUri.TrimEnd('/')
                : "https://" + shareDomain;

            int effectiveTtl = Json.GetIntOrNull(secret, "secret_ttl")
                ?? Json.GetIntOrNull(receipt, "secret_ttl")
                ?? 0;

            return new ShareResult(
                secretKey,
                receiptKey,
                root + "/secret/" + secretKey,
                root + "/receipt/" + receiptKey,
                requestedTtlSeconds,
                effectiveTtl);
        }

        private static OtsException TranslateHttpError(HttpResult response, bool isAnonymous)
        {
            string serverMessage, errorField, requestId;
            OtsErrorTranslator.ReadErrorBody(response.Body, out serverMessage, out errorField, out requestId);

            string message;
            switch (response.StatusCode)
            {
                case 401:
                case 403:
                    // Verified against eu.onetimesecret.com with a real account: a request
                    // for a longer-than-default lifetime came back 401 with "Feature requires
                    // extended default expiration entitlement" - a plan/feature limit, not
                    // rejected credentials. 401/403 is not the unambiguous "bad credentials"
                    // signal v2's own docs imply, so a message mentioning an entitlement gets
                    // its own wording rather than the blanket credentials one.
                    if (!string.IsNullOrEmpty(serverMessage) &&
                        serverMessage.IndexOf("entitlement", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        message = "OneTimeSecret rejected this request because of an account plan " +
                            "limitation, not bad credentials - most likely the requested lifetime " +
                            "exceeds what your plan allows. Try a shorter lifetime.";
                    }
                    else
                    {
                        message = isAnonymous
                            ? "OneTimeSecret refused an anonymous share. Configure an account and " +
                              "API token in the OneTimeSecret options."
                            : "OneTimeSecret rejected your credentials. Check the account and API " +
                              "token in the OneTimeSecret options.";
                    }
                    break;

                case 404:
                    // SPEC.md section 8: a 404 here most likely means cached capability
                    // detection is stale (e.g. a self-hosted instance downgraded). The
                    // caller is expected to re-probe and retry as v1 once - see
                    // AutoFallbackOtsClient - so reaching this message means that already
                    // happened and failed too, or v2 was forced in the options dialog.
                    message = "OneTimeSecret's v2 API did not respond at the expected endpoint. " +
                        "The detected API version may be out of date - re-open the OneTimeSecret " +
                        "options and click OK to refresh it, or check the base URL.";
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
    }
}
