using System.Collections.Generic;

namespace OneTimeSecret.Ots
{
    /// <summary>
    /// Shared between the v1 and v2 clients: translating a transport-level failure into an
    /// <see cref="OtsException"/>, and reading an error body, are identical for both - only
    /// the per-status-code wording differs, which each client still owns itself.
    /// </summary>
    internal static class OtsErrorTranslator
    {
        public static OtsException TranslateTransportFailure(HttpTransportException ex)
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

        /// <summary>
        /// Best effort: an error body may be JSON, HTML, or empty. Never throws - a failure
        /// to parse the error must not replace the real error with a parser error. Both API
        /// generations use the same field names (`message`/`error`/`field`/`request_id`),
        /// but never all of them at once - every field here is genuinely optional.
        /// </summary>
        public static void ReadErrorBody(string body, out string message, out string field, out string requestId)
        {
            message = null;
            field = null;
            requestId = null;

            if (string.IsNullOrEmpty(body)) return;

            try
            {
                Dictionary<string, object> obj = Json.ParseObject(body);
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
