using System;

namespace OneTimeSecret.Ots
{
    public sealed class ShareRequest
    {
        public ShareRequest(string secret, int ttlSeconds)
        {
            if (secret == null) throw new ArgumentNullException("secret");
            Secret = secret;
            TtlSeconds = ttlSeconds;
        }

        public string Secret { get; private set; }
        public int TtlSeconds { get; private set; }
    }

    public sealed class ShareResult
    {
        public ShareResult(string secretKey, string metadataKey, string shareUrl, string receiptUrl)
        {
            SecretKey = secretKey;
            MetadataKey = metadataKey;
            ShareUrl = shareUrl;
            ReceiptUrl = receiptUrl;
        }

        public string SecretKey { get; private set; }
        public string MetadataKey { get; private set; }
        public string ShareUrl { get; private set; }
        public string ReceiptUrl { get; private set; }
    }

    public interface IOtsClient
    {
        ShareResult Share(ShareRequest request);
    }

    /// <summary>
    /// A failure with a message fit to show the user. Never carries the shared secret:
    /// messages are built from status codes and the API's own error text only.
    /// </summary>
    public sealed class OtsException : Exception
    {
        public OtsException(string message) : this(message, 0, null, null) { }

        public OtsException(string message, int statusCode, string requestId, Exception inner)
            : base(message, inner)
        {
            StatusCode = statusCode;
            RequestId = requestId;
        }

        /// <summary>HTTP status, or 0 when the failure happened below the HTTP layer.</summary>
        public int StatusCode { get; private set; }

        /// <summary>Server-supplied correlation id, when the API returned one.</summary>
        public string RequestId { get; private set; }
    }
}
